using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace D102LabelPrinter;

/// <summary>
/// 라벨 위치·문구 설정창. 확장에서 만든 편집기(editor.js)를 WebView2 로 임베드.
/// 드래그·리사이즈·줌·텍스트편집·표시토글 전부 그대로. 저장은 .NET 이 layout.json 에
/// 직접 기록(브라우저 저장소 미사용 → 완벽 저장).
/// </summary>
public class SettingsForm : Form
{
    private readonly WebView2 _web;

    public SettingsForm()
    {
        Text = "D102 라벨 위치·문구 설정";
        Width = 1040;
        Height = 460;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new System.Drawing.Size(820, 360);
        try { Icon = System.Drawing.SystemIcons.Application; } catch { }

        _web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);
        Shown += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        try
        {
            string userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "D102LabelPrinter", "WebView2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);

            _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.WebMessageReceived += OnWebMessage;
            _web.CoreWebView2.NavigationCompleted += OnNavigated;

            string html = Path.Combine(AppContext.BaseDirectory, "editor", "index.html");
            if (!File.Exists(html))
            {
                MessageBox.Show("편집기 파일을 찾을 수 없습니다:\n" + html, "D102 라벨 인쇄");
                return;
            }
            _web.CoreWebView2.Navigate(new Uri(html).AbsoluteUri);
        }
        catch (Exception e)
        {
            MessageBox.Show("설정창 초기화 실패:\n" + e.Message, "D102 라벨 인쇄",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnNavigated(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        string layoutJson = JsonSerializer.Serialize(AppState.Layout, AppState.Camel);
        string cfgJson = JsonSerializer.Serialize(AppState.Config, AppState.Camel);
        await _web.CoreWebView2.ExecuteScriptAsync($"window.__ubBoot({layoutJson}, {cfgJson})");
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.TryGetWebMessageAsString();
            var msg = JsonSerializer.Deserialize<EditorMsg>(json, AppState.Camel);
            switch (msg?.Type)
            {
                case "save":
                    if (msg.Layout is { Count: > 0 })
                    {
                        AppState.Layout = msg.Layout;
                        AppState.SaveLayout();   // ← layout.json 즉시 기록(완벽 저장)
                    }
                    break;
                case "reset":
                    AppState.Layout = LabelModel.DefaultLayout();
                    AppState.SaveLayout();
                    break;
                case "testprint":
                    Printing.TestPrint(this);
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("WebMessage 오류: " + ex.Message);
        }
    }

    private class EditorMsg
    {
        public string Type { get; set; }
        public List<Field> Layout { get; set; }
    }
}
