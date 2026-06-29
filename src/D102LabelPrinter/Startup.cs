using Microsoft.Win32;

namespace D102LabelPrinter;

/// <summary>윈도우 로그인 시 자동 실행 등록(HKCU Run). 매 실행마다 현재 exe 경로로 갱신
/// → Velopack 업데이트로 버전 폴더가 바뀌어도 다음 실행 때 자기수정된다.</summary>
public static class Startup
{
    const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "D102LabelPrinter";

    public static void Enable()
    {
        try
        {
            string exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            k?.SetValue(ValueName, $"\"{exe}\"");
        }
        catch { /* 권한/정책 문제 시 조용히 무시 */ }
    }

    public static void Disable()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey, true); k?.DeleteValue(ValueName, false); }
        catch { }
    }

    public static bool IsEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(ValueName) != null; }
        catch { return false; }
    }
}
