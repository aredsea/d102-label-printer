using System.Text.Json;

namespace D102LabelPrinter;

/// <summary>--selftest &lt;layout.json&gt; : 편집기 저장 JSON → layout.json 영속화 → 재로드 → ZPL 반영 검증.
/// 결과를 %TEMP%\d102_selftest.json 에 기록(WinExe 콘솔 없음).</summary>
public static class SelfTest
{
    public static void Run(string layoutFile)
    {
        var res = new Dictionary<string, object>();
        try
        {
            string json = File.ReadAllText(layoutFile);
            var layout = JsonSerializer.Deserialize<List<Field>>(json, AppState.Camel);
            res["deserialized_count"] = layout?.Count ?? 0;

            // 편집기 저장 경로와 동일: AppState.Layout 설정 후 파일 기록
            AppState.Layout = layout;
            AppState.SaveLayout();

            // 완전 새로 읽기(파일에서)
            AppState.Layout = LabelModel.DefaultLayout();
            AppState.Load();

            var price = AppState.Layout.FirstOrDefault(f => f.Key == "price");
            var brandTop = AppState.Layout.FirstOrDefault(f => f.Key == "brandTop");
            var signbox = AppState.Layout.FirstOrDefault(f => f.Key == "signbox");
            res["reloaded_price_x"] = price?.X;
            res["reloaded_brandTop_text"] = brandTop?.Text;
            res["reloaded_signbox_visible"] = signbox?.Visible;

            var item = new LabelItem
            {
                Barcode = "2606RL", ItemName = "F-볼륨하트언발체인", Price = 1830000,
                Metal = "18K", Diameter = "17", Weight = "4.36g", Category = "FASHION", Partner = "백*심9932/G"
            };
            string zpl = ZplEngine.BuildLabel(item, AppState.Layout, AppState.Config);
            int priceDots = (int)Math.Round(12.3 * ZplEngine.DotsPerMm);
            res["zpl_price_moved"] = zpl.Contains($"^FO{priceDots},");     // 이동한 가격 위치 반영
            res["zpl_signbox_hidden"] = !zpl.Contains("^GB");              // 숨긴 서명란 박스 없음
            res["zpl_len"] = zpl.Length;
            res["ok"] = true;
        }
        catch (Exception e) { res["error"] = e.Message; res["ok"] = false; }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "d102_selftest.json"),
            JsonSerializer.Serialize(res, AppState.Camel));
    }
}
