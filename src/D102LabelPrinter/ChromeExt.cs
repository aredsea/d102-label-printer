using System.Diagnostics;
using Microsoft.Win32;

namespace D102LabelPrinter;

/// <summary>
/// 크롬 확장 안정 경로·허용 목록 관리.
/// </summary>
public static class ChromeExt
{
    public const string ExtId = "kejfpekfhpjlaifonfdlegmnoglaaphf";

    const string ForceList = @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist";
    const string Settings = @"SOFTWARE\Policies\Google\Chrome\ExtensionSettings";
    static readonly string[] AllowlistPaths =
    {
        @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallAllowlist",
        @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallAllowlist"
    };

    /// <summary>동봉된 확장 폴더(설치본 내부, 업데이트마다 경로 바뀔 수 있음).</summary>
    public static string BundledExtensionDir => Path.Combine(AppContext.BaseDirectory, "extension");

    /// <summary>크롬이 unpacked 로 가리킬 "안정 경로"(프로그램 업데이트와 무관).
    /// 매 실행마다 동봉본을 여기로 복사 → 크롬에서 reload 만 하면 최신 반영.</summary>
    public static string StableExtensionDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D102LabelExtension");

    /// <summary>동봉 확장 → 안정 경로 복사(덮어쓰기).</summary>
    public static void SyncStableExtension()
    {
        try
        {
            string src = BundledExtensionDir, dst = StableExtensionDir;
            if (!Directory.Exists(src)) return;
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(src, f);
                string target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(f, target, true);
            }
        }
        catch { }
    }

    /// <summary>압축해제 확장 허용 목록 기록. HKLM/HKCU 권한이 없으면 조용히 무시.</summary>
    public static void AddToAllowlist()
    {
        TryAddToAllowlist(Registry.LocalMachine);
        TryAddToAllowlist(Registry.CurrentUser);
    }

    /// <summary>관리자 권한으로 HKLM 확장 허용 목록 기록.</summary>
    public static bool AddToAllowlistElevated()
    {
        AddToAllowlist();
        if (IsAllowlistSet(Registry.LocalMachine)) return true;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "--allowlist-ext",
                UseShellExecute = true,
                Verb = "runas"
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(15000);
            return IsAllowlistSet(Registry.LocalMachine);
        }
        catch { return false; }
    }

    private static bool TryAddToAllowlist(RegistryKey root)
    {
        bool all = true;
        foreach (string path in AllowlistPaths)
        {
            try { using (var key = root.CreateSubKey(path)) key.SetValue("1", ExtId); }
            catch { all = false; }
        }
        return all;
    }

    private static bool IsAllowlistSet(RegistryKey root)
    {
        try
        {
            foreach (string path in AllowlistPaths)
            {
                using var key = root.OpenSubKey(path);
                if (!string.Equals(key?.GetValue("1") as string, ExtId, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }
        catch { return false; }
    }

    public static void Unregister()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try { using (var fl = root.OpenSubKey(ForceList, true)) fl?.DeleteValue("1", false); } catch { }
            try { root.DeleteSubKeyTree($@"{Settings}\{ExtId}", false); } catch { }
        }
    }
}
