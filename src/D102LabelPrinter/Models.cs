using System.Text.Json.Serialization;

namespace D102LabelPrinter;

/// <summary>라벨 필드 정의 — 확장의 config.js layout 과 동일 모델(mm 좌표).</summary>
public class Field
{
    public string Key { get; set; }
    public string Name { get; set; }
    public string Type { get; set; } = "text";   // text | barcode | box
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Fs { get; set; }
    public bool Bold { get; set; }
    public string Align { get; set; } = "left";
    public bool Visible { get; set; } = true;
    public bool Editable { get; set; }
    public string Text { get; set; }              // 사용자가 덮어쓴 문구(브랜딩 등)
}

/// <summary>인쇄 1건의 상품 데이터(확장이 POST). 고정문구는 FixedConfig.</summary>
public class LabelItem
{
    public string Barcode { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string ItemNo { get; set; } = "";
    public long? Price { get; set; }
    public string Metal { get; set; } = "";
    public string Diameter { get; set; } = "";    // 호수/외경
    public string Weight { get; set; } = "";
    public string Category { get; set; } = "";
    public string Partner { get; set; } = "";
    public string SetNo { get; set; } = "";
}

/// <summary>고정 설정(회사명/브랜드/프린터/포트). %AppData%\D102LabelPrinter\config.json</summary>
public class FixedConfig
{
    public string Company { get; set; } = "(주)D102";
    public string BrandTop { get; set; } = "주얼리전산의 리더 지앤샵";
    public string BrandUrl { get; set; } = "www.honsu114.com";
    public string BarcodePrefix { get; set; } = "LT";
    public string PrinterName { get; set; } = "";   // 빈값=기본 프린터
    public int Port { get; set; } = 17600;
    public double LabelWmm { get; set; } = 60;
    public double LabelHmm { get; set; } = 10;
}

/// <summary>레이아웃 기본값 + 데이터키→문자열 매핑(확장 label.js 포팅).</summary>
public static class LabelModel
{
    public static List<Field> DefaultLayout() => new()
    {
        new Field{ Key="company",     Name="회사/매장",     Type="text", X=0.6,  Y=0.2, W=20,   Fs=1.9, Align="left",   Editable=true },
        new Field{ Key="itemName",    Name="품명",          Type="text", X=0.6,  Y=2.0, W=20,   Fs=1.9, Align="left" },
        new Field{ Key="price",       Name="판매가",        Type="text", X=0.6,  Y=3.8, W=20,   Fs=2.6, Bold=true, Align="left" },
        new Field{ Key="barcode",     Name="바코드",        Type="barcode", X=0.6, Y=5.9, W=20, H=2.9, Align="left" },
        new Field{ Key="barcodeLabel",Name="바코드+접두",   Type="text", X=0.6,  Y=9.0, W=20,   Fs=2.0, Align="left" },
        new Field{ Key="bnum2",       Name="바코드번호(B)", Type="text", X=22.6, Y=0.4, W=18.5, Fs=2.1, Align="left" },
        new Field{ Key="metal",       Name="금속/외경",     Type="text", X=22.6, Y=2.4, W=18.5, Fs=1.9, Align="left" },
        new Field{ Key="weight",      Name="중량",          Type="text", X=22.6, Y=4.3, W=18.5, Fs=1.9, Align="left" },
        new Field{ Key="compCat",     Name="회사+구분",     Type="text", X=22.6, Y=6.2, W=18.5, Fs=1.8, Align="left" },
        new Field{ Key="namePartner", Name="품명+거래처",   Type="text", X=22.6, Y=8.1, W=18.5, Fs=1.8, Align="left" },
        new Field{ Key="brandTop",    Name="브랜드상단",    Type="text", X=42.6, Y=0.6, W=16.8, Fs=1.7, Align="center", Editable=true },
        new Field{ Key="signbox",     Name="서명란",        Type="box",  X=43.0, Y=3.0, W=16,   H=3.6, Align="left" },
        new Field{ Key="brandUrl",    Name="브랜드URL",     Type="text", X=42.6, Y=8.4, W=16.8, Fs=1.7, Align="center", Editable=true },
    };

    /// <summary>필드 표시 문자열. f.Text 가 있으면 그것을, 없으면 데이터값.</summary>
    public static string FieldValue(Field f, LabelItem d, FixedConfig cfg)
    {
        if (!string.IsNullOrEmpty(f.Text)) return f.Text;
        switch (f.Key)
        {
            case "company":      return cfg.Company;
            case "itemName":     return d.ItemName;
            case "price":        return d.Price.HasValue ? d.Price.Value.ToString("#,0") : "";
            case "barcodeLabel": return string.IsNullOrEmpty(cfg.BarcodePrefix) ? d.Barcode : $"{cfg.BarcodePrefix}  {d.Barcode}";
            case "bnum2":        return d.Barcode;
            case "metal":        return string.IsNullOrEmpty(d.Diameter) ? d.Metal : $"{d.Metal} ({d.Diameter})".Trim();
            case "weight":       return d.Weight;
            case "compCat":      return Join("  ", cfg.Company, d.Category);
            case "namePartner":  return Join("", d.ItemName, Join("", d.Partner, d.SetNo));
            case "brandTop":     return cfg.BrandTop;
            case "brandUrl":     return cfg.BrandUrl;
            default:             return "";
        }
    }

    private static string Join(string sep, params string[] parts)
        => string.Join(sep, parts.Where(p => !string.IsNullOrEmpty(p)));

    /// <summary>저장된 레이아웃(편집기에서 온, type/name 누락 가능) → 기본 레이아웃에
    /// 위치/크기/표시/문구만 덮어쓴다. type·name·key 는 항상 기본값 유지(바코드/박스 보존).</summary>
    public static List<Field> Merge(List<Field> saved)
    {
        var baseLayout = DefaultLayout();
        if (saved == null || saved.Count == 0) return baseLayout;
        var byKey = new Dictionary<string, Field>();
        foreach (var s in saved) if (!string.IsNullOrEmpty(s?.Key)) byKey[s.Key] = s;
        foreach (var f in baseLayout)
        {
            if (!byKey.TryGetValue(f.Key, out var s)) continue;
            f.X = s.X; f.Y = s.Y; f.W = s.W; f.H = s.H; f.Fs = s.Fs;
            f.Bold = s.Bold; f.Visible = s.Visible; f.Text = s.Text;
            if (!string.IsNullOrEmpty(s.Align)) f.Align = s.Align;
            // ※ Type/Name/Key/Editable 은 기본값 유지(저장값에 없거나 틀려도 보존)
        }
        return baseLayout;
    }
}

/// <summary>POST /print 요청 본문.</summary>
public class PrintRequest
{
    [JsonPropertyName("items")] public List<LabelItem> Items { get; set; } = new();
    [JsonPropertyName("layout")] public List<Field> Layout { get; set; }   // 선택: 확장이 보낼 수도(보통 프로그램 보유)
}
