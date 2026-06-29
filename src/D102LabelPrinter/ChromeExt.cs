using System.Diagnostics;
using Microsoft.Win32;

namespace D102LabelPrinter;

/// <summary>
/// 크롬 확장 등록. force-install 정책은 Software\Policies(관리자 전용)에 써야 하므로
/// 관리자 권한이 필요하다. 비관리자에선 실패 → 수동 로드(폴더 동봉)로 폴백.
/// </summary>
public static class ChromeExt
{
    public const string ExtId = "kejfpekfhpjlaifonfdlegmnoglaaphf";
    public const string UpdateUrl = "https://raw.githubusercontent.com/aredsea/ubishop-barcode-ext/main/update.xml";

    const string ForceList = @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist";
    const string Settings = @"SOFTWARE\Policies\Google\Chrome\ExtensionSettings";

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

    /// <summary>force-install 정책 기록(관리자 필요). HKLM 우선, HKCU 보조. true=성공.</summary>
    public static bool WritePolicy()
    {
        bool any = false;
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using (var fl = root.CreateSubKey(ForceList))
                    fl.SetValue("1", $"{ExtId};{UpdateUrl}");
                using (var es = root.CreateSubKey($@"{Settings}\{ExtId}"))
                {
                    es.SetValue("installation_mode", "force_installed");
                    es.SetValue("update_url", UpdateUrl);
                }
                any = true;
            }
            catch { /* 권한 없음 — 다음 루트 시도 */ }
        }
        return any;
    }

    /// <summary>이미 정책이 있는지(HKLM 또는 HKCU).</summary>
    public static bool IsPolicySet()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var fl = root.OpenSubKey(ForceList);
                if (fl?.GetValue("1") is string v && v.StartsWith(ExtId)) return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>비관리자에서 호출 → 관리자 권한으로 자기 자신 재실행(--register-ext)해 정책 기록.</summary>
    public static bool RegisterElevated()
    {
        if (IsPolicySet()) return true;
        if (WritePolicy()) return true;   // 이미 관리자면 바로 성공
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule.FileName,
                Arguments = "--register-ext",
                UseShellExecute = true,
                Verb = "runas"            // UAC 승격
            };
            var p = Process.Start(psi);
            p.WaitForExit(15000);
            return IsPolicySet();
        }
        catch { return false; }            // 사용자가 UAC 취소 등
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
