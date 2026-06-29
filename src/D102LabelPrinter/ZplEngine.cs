using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text;

namespace D102LabelPrinter;

/// <summary>
/// 라벨(레이아웃+데이터) → ZPL. 바코드=네이티브 ^BCN, 한글/텍스트=Pretendard 1-bit 비트맵 ^GFA.
/// Zebra GX430t = 300dpi = 11.811 dots/mm.
/// </summary>
public static class ZplEngine
{
    public const double DotsPerMm = 300.0 / 25.4;   // ≈ 11.811

    /// <summary>내장 Pretendard(어셈블리 리소스). 설치 여부와 무관하게 동일 렌더.</summary>
    private static PrivateFontCollection _pfc;
    private static FontFamily _embeddedFamily;
    private static bool _fontInit;

    private static void EnsureEmbeddedFont()
    {
        if (_fontInit) return;
        _fontInit = true;
        try
        {
            var asm = typeof(ZplEngine).Assembly;
            var pfc = new PrivateFontCollection();
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (!(res.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
                   || res.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase))) continue;
                using var st = asm.GetManifestResourceStream(res);
                if (st == null) continue;
                using var ms = new MemoryStream();
                st.CopyTo(ms);
                byte[] bytes = ms.ToArray();
                // AddMemoryFont 가 참조하는 메모리는 앱 수명 동안 유지(해제하지 않음).
                IntPtr p = Marshal.AllocCoTaskMem(bytes.Length);
                Marshal.Copy(bytes, 0, p, bytes.Length);
                pfc.AddMemoryFont(p, bytes.Length);
            }
            _pfc = pfc;
            foreach (var fam in pfc.Families)
                if (fam.Name.IndexOf("Pretendard", StringComparison.OrdinalIgnoreCase) >= 0)
                { _embeddedFamily = fam; break; }
            if (_embeddedFamily == null && pfc.Families.Length > 0) _embeddedFamily = pfc.Families[0];
        }
        catch { _embeddedFamily = null; }   // 실패 시 설치 폰트로 폴백
    }

    /// <summary>폴백 폰트명(내장 로드 실패 시): 설치된 Pretendard → 맑은고딕.</summary>
    private static string _koreanFont;
    public static string KoreanFont
    {
        get
        {
            if (_koreanFont != null) return _koreanFont;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var c = new InstalledFontCollection())
                foreach (var fam in c.Families) names.Add(fam.Name);
            _koreanFont = names.Contains("Pretendard") ? "Pretendard"
                        : names.Contains("Pretendard Variable") ? "Pretendard Variable"
                        : "Malgun Gothic";
            return _koreanFont;
        }
    }

    /// <summary>렌더에 쓸 Font 생성 — 내장 Pretendard 우선, 실패 시 설치 폰트명.</summary>
    private static Font MakeFont(int px, FontStyle style)
    {
        EnsureEmbeddedFont();
        if (_embeddedFamily != null)
        {
            var st = _embeddedFamily.IsStyleAvailable(style) ? style
                   : _embeddedFamily.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : style;
            return new Font(_embeddedFamily, px, st, GraphicsUnit.Pixel);
        }
        return new Font(KoreanFont, px, style, GraphicsUnit.Pixel);
    }

    private static int Dots(double mm) => (int)Math.Round(mm * DotsPerMm);

    /// <summary>인쇄 1건 → ZPL 문자열.</summary>
    public static string BuildLabel(LabelItem item, List<Field> layout, FixedConfig cfg)
    {
        var sb = new StringBuilder();
        sb.Append("^XA\n^CI28\n");                                   // UTF-8(텍스트는 비트맵이라 무관하나 관례)
        sb.Append($"^PW{Dots(cfg.LabelWmm)}\n^LL{Dots(cfg.LabelHmm)}\n^LH0,0\n");

        foreach (var f in layout)
        {
            if (!f.Visible) continue;
            int x = Dots(f.X), y = Dots(f.Y);

            if (f.Type == "barcode")
            {
                int bh = Dots(f.H <= 0 ? 2.9 : f.H);
                sb.Append($"^FO{x},{y}^BY2,2.0,{bh}\n^BCN,{bh},N,N,N\n^FD{EscapeFd(item.Barcode)}^FS\n");
            }
            else if (f.Type == "box")
            {
                sb.Append($"^FO{x},{y}^GB{Dots(f.W)},{Dots(f.H <= 0 ? 3 : f.H)},1^FS\n");
            }
            else
            {
                string text = LabelModel.FieldValue(f, item, cfg);
                if (string.IsNullOrEmpty(text)) continue;
                var (w, _, gf) = RenderTextToZpl(text, f.Fs, f.Bold);
                int fx = x;
                int boxw = Dots(f.W);
                if (f.Align == "center") fx = x + (boxw - w) / 2;
                else if (f.Align == "right") fx = x + (boxw - w);
                if (fx < 0) fx = 0;
                sb.Append($"^FO{fx},{y}{gf}^FS\n");
            }
        }
        sb.Append("^XZ");
        return sb.ToString();
    }

    /// <summary>여러 건 = 라벨마다 ^XA…^XZ 연속.</summary>
    public static string BuildLabels(IEnumerable<LabelItem> items, List<Field> layout, FixedConfig cfg)
    {
        var sb = new StringBuilder();
        foreach (var it in items) sb.Append(BuildLabel(it, layout, cfg)).Append('\n');
        return sb.ToString();
    }

    /// <summary>텍스트 → 1-bit 비트맵 → ^GFA. (한글 포함, 내장 Pretendard)</summary>
    public static (int w, int h, string zpl) RenderTextToZpl(string text, double fsMm, bool bold)
    {
        int px = Math.Max(6, (int)Math.Round(fsMm * DotsPerMm));
        var style = bold ? FontStyle.Bold : FontStyle.Regular;
        using var font = MakeFont(px, style);

        SizeF sz;
        using (var tmp = new Bitmap(2, 2))
        using (var g0 = Graphics.FromImage(tmp))
            sz = g0.MeasureString(text, font);
        int w = Math.Max(1, (int)Math.Ceiling(sz.Width));
        int h = Math.Max(1, (int)Math.Ceiling(sz.Height));

        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
            g.DrawString(text, font, Brushes.Black, 0, 0);
        }

        int rowBytes = (w + 7) / 8;
        var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        byte[] buf = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, buf, 0, buf.Length);
        bmp.UnlockBits(data);

        var sb = new StringBuilder(rowBytes * h * 2);
        for (int y = 0; y < h; y++)
        {
            int rowOff = y * data.Stride;
            for (int bx = 0; bx < rowBytes; bx++)
            {
                int b = 0;
                for (int bit = 0; bit < 8; bit++)
                {
                    int xx = bx * 8 + bit;
                    if (xx < w)
                    {
                        int o = rowOff + xx * 4;       // BGRA
                        int lum = (buf[o] + buf[o + 1] + buf[o + 2]) / 3;
                        if (lum < 128) b |= (0x80 >> bit);
                    }
                }
                sb.Append(b.ToString("X2"));
            }
        }
        int total = rowBytes * h;
        return (w, h, $"^GFA,{total},{total},{rowBytes},{sb}");
    }

    private static string EscapeFd(string s) =>
        (s ?? "").Replace("^", "").Replace("~", "");
}
