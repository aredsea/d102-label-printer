using System.Windows.Forms;
using Velopack;

namespace D102LabelPrinter;

static class Program
{
    static LocalServer _server;
    static NotifyIcon _tray;
    static System.Drawing.Icon _appIcon;

    /// <summary>앱 아이콘(penguin_tray.ico). 없으면 시스템 기본.</summary>
    public static System.Drawing.Icon AppIcon()
    {
        if (_appIcon != null) return _appIcon;
        try
        {
            string p = Path.Combine(AppContext.BaseDirectory, "penguin_tray.ico");
            if (File.Exists(p)) _appIcon = new System.Drawing.Icon(p);
        }
        catch { }
        return _appIcon ?? System.Drawing.SystemIcons.Application;
    }

    [STAThread]
    static void Main(string[] args)
    {
        // Velopack: 설치/업데이트/제거 훅 처리(반드시 최상단). 확장도 함께 등록.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => { try { ChromeExt.WritePolicy(); } catch { } })
            .OnBeforeUninstallFastCallback(_ => { try { ChromeExt.Unregister(); } catch { } })
            .Run();

        if (args.Length >= 1 && args[0] == "--register-ext") { ChromeExt.WritePolicy(); return; }  // 관리자 승격 인스턴스
        if (args.Length >= 2 && args[0] == "--selftest") { SelfTest.Run(args[1]); return; }

        using var mutex = new Mutex(true, "D102LabelPrinter_singleton", out bool isNew);
        if (!isNew) return;   // 중복 실행 방지

        ApplicationConfiguration.Initialize();
        AppState.Load();
        if (AppState.Config.AutoStart) Startup.Enable();   // 윈도우 시작 시 자동 실행(기본 ON, 현재 exe로 갱신)
        else Startup.Disable();
        try { ChromeExt.SyncStableExtension(); } catch { }   // 확장을 안정 경로로 복사(reload 만으로 최신)
        try { ChromeExt.WritePolicy(); } catch { }           // 권한 있으면 정책 기록(없으면 무시)

        _ = Updater.CheckAsync();   // 백그라운드 자동업데이트 확인

        _server = new LocalServer { Log = s => System.Diagnostics.Debug.WriteLine(s) };
        try { _server.Start(AppState.Config.Port); }
        catch (Exception e)
        {
            MessageBox.Show("로컬 서버 시작 실패:\n" + e.Message, "D102 라벨 인쇄",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        _tray = BuildTray();
        Application.ApplicationExit += (_, _) => { _server.Stop(); if (_tray != null) _tray.Visible = false; };
        Application.Run();
    }

    static NotifyIcon BuildTray()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("D102 라벨 인쇄 — 실행 중") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        var settings = new ToolStripMenuItem("라벨 위치·문구 설정…");
        settings.Click += (_, _) => OpenSettings();
        menu.Items.Add(settings);

        var printerItem = new ToolStripMenuItem("프린터 선택…");
        printerItem.Click += (_, _) => PickPrinter();
        menu.Items.Add(printerItem);

        var test = new ToolStripMenuItem("테스트 인쇄");
        test.Click += (_, _) => Printing.TestPrint();
        menu.Items.Add(test);

        var autostart = new ToolStripMenuItem("윈도우 시작 시 자동 실행") { Checked = AppState.Config.AutoStart, CheckOnClick = true };
        autostart.Click += (_, _) =>
        {
            AppState.Config.AutoStart = autostart.Checked;
            AppState.SaveConfig();
            if (autostart.Checked) Startup.Enable(); else Startup.Disable();
        };
        menu.Items.Add(autostart);

        var regAuto = new ToolStripMenuItem("크롬에 확장 설치 (자동·관리자)");
        regAuto.Click += (_, _) =>
        {
            bool ok = ChromeExt.RegisterElevated();
            MessageBox.Show(ok
                ? "확장 등록 완료.\n크롬을 완전히 닫았다가(트레이/작업관리자에 chrome 없게) 다시 여세요.\n그래도 안 보이면 \"확장 수동 설치\"를 쓰세요."
                : "자동 설치 실패(관리자 취소 또는 정책 미적용).\n\"확장 수동 설치(폴더 열기)\"로 진행하세요.",
                "D102 라벨 인쇄", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        };
        menu.Items.Add(regAuto);

        var regManual = new ToolStripMenuItem("확장 수동 설치/새로고침 (폴더 열기)");
        regManual.Click += (_, _) =>
        {
            ChromeExt.SyncStableExtension();   // 최신본을 안정 경로로
            try { System.Diagnostics.Process.Start("explorer.exe", $"\"{ChromeExt.StableExtensionDir}\""); } catch { }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("chrome.exe", "chrome://extensions") { UseShellExecute = true }); } catch { }
            MessageBox.Show(
                "크롬 확장 설치/새로고침:\n\n" +
                "[처음 설치] chrome://extensions → \"개발자 모드\" ON → \"압축해제된 확장 로드\"\n" +
                "  → 방금 열린 폴더 선택\n\n" +
                "[이미 설치됨 / 업데이트] 확장 카드의 새로고침(↻) 클릭\n" +
                "  (안되면 기존 확장 \"삭제\" 후 위 폴더로 다시 로드)\n\n" +
                "설치 후 유비샵 페이지를 새로고침하세요.",
                "D102 라벨 인쇄", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        menu.Items.Add(regManual);

        menu.Items.Add(new ToolStripSeparator());

        // ---- 전표 캐시 + 텔레메트리 ----------------------------------------
        var cacheStats = new ToolStripMenuItem("전표 캐시·사용 통계…");
        cacheStats.Click += (_, _) => ShowCacheStats();
        menu.Items.Add(cacheStats);

        var cacheFolder = new ToolStripMenuItem("캐시 폴더 열기");
        cacheFolder.Click += (_, _) => {
            try { Directory.CreateDirectory(CacheStore.Dir); System.Diagnostics.Process.Start("explorer.exe", $"\"{CacheStore.Dir}\""); } catch { }
        };
        menu.Items.Add(cacheFolder);

        var cacheClear = new ToolStripMenuItem("캐시 비우기 (30일 이전)");
        cacheClear.Click += (_, _) => {
            int n = CacheStore.Clear(30);
            MessageBox.Show($"{n}개 캐시 항목 삭제됨.", "D102 라벨 인쇄", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        menu.Items.Add(cacheClear);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("종료");
        exit.Click += (_, _) => Application.Exit();
        menu.Items.Add(exit);

        return new NotifyIcon
        {
            Icon = AppIcon(),
            Text = $"D102 라벨 인쇄 v{AppState.Version}",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    static void PickPrinter()
    {
        var printers = RawPrinter.GetPrinters().ToList();
        string cur = string.IsNullOrEmpty(AppState.Config.PrinterName) ? RawPrinter.DefaultPrinter() : AppState.Config.PrinterName;
        using var dlg = new Form { Text = "프린터 선택", Width = 420, Height = 160, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, Icon = AppIcon() };
        var combo = new ComboBox { Left = 16, Top = 20, Width = 380, DropDownStyle = ComboBoxStyle.DropDownList };
        combo.Items.AddRange(printers.ToArray());
        if (printers.Contains(cur)) combo.SelectedItem = cur; else if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        var ok = new Button { Text = "저장", Left = 240, Top = 70, Width = 70, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "취소", Left = 320, Top = 70, Width = 70, DialogResult = DialogResult.Cancel };
        dlg.Controls.AddRange(new Control[] { combo, ok, cancel });
        dlg.AcceptButton = ok; dlg.CancelButton = cancel;
        if (dlg.ShowDialog() == DialogResult.OK && combo.SelectedItem != null)
        {
            AppState.Config.PrinterName = combo.SelectedItem.ToString();
            AppState.SaveConfig();
        }
    }

    static void ShowCacheStats()
    {
        var s = CacheStore.Stats();
        var t = Telemetry.Summary(7);
        string fmtMb(long b) => (b / 1024.0 / 1024).ToString("0.00") + " MB";
        string fmtAgo(long ms) => ms == 0 ? "-" : DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        string savedTxt = t.SavedMsTotal > 60000
            ? $"{t.SavedMsTotal / 60000.0:0.0}분"
            : $"{t.SavedMsTotal / 1000.0:0.0}초";
        string msg =
            $"[캐시]\n" +
            $"  경로:  {s.Path}\n" +
            $"  엔트리: {s.Entries}개  ({fmtMb(s.SizeBytes)})\n" +
            $"  가장 오래된: {fmtAgo(s.OldestMs)}\n" +
            $"  가장 최근:   {fmtAgo(s.NewestMs)}\n\n" +
            $"[최근 {t.Days}일 사용]\n" +
            $"  페이지 로드: {t.PageLoads}회 (평균 {t.AvgLoadMs}ms)\n" +
            $"  캐시 hit:   {t.CacheHits}\n" +
            $"  캐시 miss:  {t.CacheMiss}\n" +
            $"  적중률:     {t.HitRate * 100:0.0}%\n" +
            $"  분할 검색:  {t.Searches}회\n" +
            $"  절약 시간:  약 {savedTxt} (캐시 hit 없었을 때 추가됐을 서버 응답 합)";
        MessageBox.Show(msg, "D102 캐시·사용 통계", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    static SettingsForm _settings;
    static void OpenSettings()
    {
        if (_settings != null && !_settings.IsDisposed)
        {
            _settings.WindowState = FormWindowState.Normal;
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm();
        _settings.FormClosed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }
}
