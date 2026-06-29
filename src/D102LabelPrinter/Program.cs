using System.Windows.Forms;

namespace D102LabelPrinter;

static class Program
{
    static LocalServer _server;
    static NotifyIcon _tray;

    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "D102LabelPrinter_singleton", out bool isNew);
        if (!isNew) return;   // 중복 실행 방지

        ApplicationConfiguration.Initialize();
        AppState.Load();

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

        var printerItem = new ToolStripMenuItem("프린터 선택…");
        printerItem.Click += (_, _) => PickPrinter();
        menu.Items.Add(printerItem);

        var test = new ToolStripMenuItem("테스트 인쇄");
        test.Click += (_, _) => TestPrint();
        menu.Items.Add(test);

        menu.Items.Add(new ToolStripSeparator());
        var exit = new ToolStripMenuItem("종료");
        exit.Click += (_, _) => Application.Exit();
        menu.Items.Add(exit);

        return new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = $"D102 라벨 인쇄 v{AppState.Version}",
            Visible = true,
            ContextMenuStrip = menu
        };
    }

    static void PickPrinter()
    {
        var printers = RawPrinter.GetPrinters().ToList();
        string cur = string.IsNullOrEmpty(AppState.Config.PrinterName) ? RawPrinter.DefaultPrinter() : AppState.Config.PrinterName;
        using var dlg = new Form { Text = "프린터 선택", Width = 420, Height = 160, StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
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

    static void TestPrint()
    {
        try
        {
            var item = new LabelItem
            {
                Barcode = "2606RL", ItemName = "F-볼륨하트언발체인", ItemNo = "F-BF-Q-YG-ZZ-0096",
                Price = 1830000, Metal = "18K", Diameter = "17", Weight = "4.36g",
                Category = "FASHION", Partner = "백*심9932/G"
            };
            string zpl = ZplEngine.BuildLabel(item, AppState.Layout, AppState.Config);
            RawPrinter.SendZpl(AppState.Config.PrinterName, zpl);
            MessageBox.Show("테스트 라벨을 인쇄했습니다.", "D102 라벨 인쇄", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception e)
        {
            MessageBox.Show("테스트 인쇄 실패:\n" + e.Message, "D102 라벨 인쇄", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
