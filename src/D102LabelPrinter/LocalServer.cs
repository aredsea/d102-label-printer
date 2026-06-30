using System.Net;
using System.Text;
using System.Text.Json;

namespace D102LabelPrinter;

/// <summary>
/// 127.0.0.1 로컬 HTTP 서버. 확장이 라벨 데이터를 POST → ZPL 생성 → Zebra raw 인쇄.
/// 127.0.0.1 리터럴 바인딩이라 관리자/urlacl 불필요.
/// </summary>
public class LocalServer
{
    private HttpListener _listener;
    private Thread _thread;
    private volatile bool _running;
    public Action<string> Log;

    public void Start(int port)
    {
        Stop();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "UBLocalServer" };
        _thread.Start();
        Log?.Invoke($"로컬 서버 시작: http://127.0.0.1:{port}/");
    }

    public void Stop()
    {
        _running = false;
        try { _listener?.Stop(); _listener?.Close(); } catch { }
    }

    private void Loop()
    {
        while (_running)
        {
            HttpListenerContext ctx;
            try { ctx = _listener.GetContext(); }
            catch { break; }
            ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var res = ctx.Response;
        res.Headers["Access-Control-Allow-Origin"] = "*";
        res.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        res.Headers["Access-Control-Allow-Methods"] = "POST,GET,OPTIONS";
        // ★ Private Network Access: http 공개페이지(유비샵)→127.0.0.1 fetch 허용(없으면 크롬이 차단)
        res.Headers["Access-Control-Allow-Private-Network"] = "true";
        try
        {
            if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }
            string path = req.Url.AbsolutePath.ToLowerInvariant();

            if (path == "/ping")
            {
                WriteJson(res, new { ok = true, app = "D102LabelPrinter", version = AppState.Version, printer = ActivePrinter() });
                return;
            }

            string body = "";
            if (req.HasEntityBody)
                using (var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8))
                    body = sr.ReadToEnd();

            if (path == "/print") { HandlePrint(res, body, false); return; }
            if (path == "/preview") { HandlePrint(res, body, true); return; }

            // ---- 전표 자동 분할조회 캐시 ----------------------------------------
            if (path == "/cache/get") { HandleCacheGet(res, req); return; }
            if (path == "/cache/put") { HandleCachePut(res, body); return; }
            if (path == "/cache/stats") { WriteJson(res, CacheStore.Stats()); return; }
            if (path == "/cache/clear") { HandleCacheClear(res, body); return; }
            // ---- 사용 현황 / 캐시 효과 측정 -------------------------------------
            if (path == "/telemetry/event") { HandleTelemetryEvent(res, body); return; }
            if (path == "/telemetry/summary")
            {
                int d = 7;
                int.TryParse(req.QueryString["days"] ?? "7", out d);
                WriteJson(res, Telemetry.Summary(d));
                return;
            }

            res.StatusCode = 404;
            WriteJson(res, new { ok = false, error = "unknown path" });
        }
        catch (Exception e)
        {
            try { res.StatusCode = 500; WriteJson(res, new { ok = false, error = e.Message }); } catch { }
            Log?.Invoke("요청 처리 오류: " + e.Message);
        }
    }

    private void HandlePrint(HttpListenerResponse res, string body, bool previewOnly)
    {
        var reqObj = JsonSerializer.Deserialize<PrintRequest>(body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var items = reqObj?.Items ?? new List<LabelItem>();
        if (items.Count == 0) { WriteJson(res, new { ok = false, error = "no items" }); return; }

        var layout = (reqObj.Layout is { Count: > 0 }) ? reqObj.Layout : AppState.Layout;
        string zpl = ZplEngine.BuildLabels(items, layout, AppState.Config);

        if (previewOnly) { WriteJson(res, new { ok = true, count = items.Count, zpl }); return; }

        RawPrinter.SendZpl(AppState.Config.PrinterName, zpl);
        Log?.Invoke($"인쇄 {items.Count}건 → {ActivePrinter()}");
        WriteJson(res, new { ok = true, printed = items.Count, printer = ActivePrinter() });
    }

    private void HandleCacheGet(HttpListenerResponse res, HttpListenerRequest req)
    {
        string p = req.QueryString["path"] ?? "";
        string date = req.QueryString["date"] ?? "";
        if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(date))
        { WriteJson(res, new { ok = false, error = "path,date required" }); return; }
        var entry = CacheStore.Get(p, date);
        if (entry == null) WriteJson(res, new { ok = true, hit = false });
        else WriteJson(res, new { ok = true, hit = true, entry });
    }

    private void HandleCachePut(HttpListenerResponse res, string body)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
            string p = d.GetValueOrDefault("path").ValueKind == JsonValueKind.String ? d["path"].GetString() : "";
            string date = d.GetValueOrDefault("date").ValueKind == JsonValueKind.String ? d["date"].GetString() : "";
            string html = d.GetValueOrDefault("html").ValueKind == JsonValueKind.String ? d["html"].GetString() : "";
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(date))
            { WriteJson(res, new { ok = false, error = "path,date required" }); return; }
            CacheStore.Put(p, date, html);
            WriteJson(res, new { ok = true });
        }
        catch (Exception e) { WriteJson(res, new { ok = false, error = e.Message }); }
    }

    private void HandleCacheClear(HttpListenerResponse res, string body)
    {
        int? older = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
                if (d.TryGetValue("olderThanDays", out var v) && v.ValueKind == JsonValueKind.Number)
                    older = v.GetInt32();
            }
        }
        catch { }
        int removed = CacheStore.Clear(older);
        WriteJson(res, new { ok = true, removed });
    }

    private void HandleTelemetryEvent(HttpListenerResponse res, string body)
    {
        try
        {
            var d = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
            string type = d.GetValueOrDefault("type").ValueKind == JsonValueKind.String ? d["type"].GetString() : "";
            if (string.IsNullOrEmpty(type)) { WriteJson(res, new { ok = false, error = "type required" }); return; }
            // type 외 모든 필드를 그대로 payload로
            d.Remove("type");
            Telemetry.Add(type, d);
            WriteJson(res, new { ok = true });
        }
        catch (Exception e) { WriteJson(res, new { ok = false, error = e.Message }); }
    }

    private static string ActivePrinter() =>
        string.IsNullOrEmpty(AppState.Config.PrinterName) ? RawPrinter.DefaultPrinter() : AppState.Config.PrinterName;

    private static void WriteJson(HttpListenerResponse res, object o)
    {
        byte[] b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o,
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        res.ContentType = "application/json; charset=utf-8";
        res.ContentLength64 = b.Length;
        res.OutputStream.Write(b, 0, b.Length);
        res.Close();
    }
}
