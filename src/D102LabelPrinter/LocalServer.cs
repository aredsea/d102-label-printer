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
