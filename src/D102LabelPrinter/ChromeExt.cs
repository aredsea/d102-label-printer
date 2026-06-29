using Microsoft.Win32;

namespace D102LabelPrinter;

/// <summary>
/// 크롬 확장 자동 등록(현재 사용자 정책). 설치/업데이트 시 호출 → 확장이 자동 설치·갱신.
/// HKCU 정책이라 관리자 권한 불필요. force_installed = 무인 설치 + 크롬 네이티브 자동업데이트.
/// </summary>
public static class ChromeExt
{
    // 패킹한 .crx 의 확장 ID (공개키에서 파생). ubishop-barcode-ext.pem 로 서명.
    public const string ExtId = "kejfpekfhpjlaifonfdlegmnoglaaphf";
    // 확장 업데이트 매니페스트(.crx 위치) — 저장소 raw
    public const string UpdateUrl = "https://raw.githubusercontent.com/aredsea/ubishop-barcode-ext/main/update.xml";

    const string PolicyBase = @"Software\Policies\Google\Chrome\ExtensionSettings";

    public static void Register()
    {
        if (ExtId.StartsWith("__")) return;   // ID 미설정 시 스킵
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{PolicyBase}\{ExtId}");
            k.SetValue("installation_mode", "force_installed");
            k.SetValue("update_url", UpdateUrl);
        }
        catch { /* 정책 쓰기 실패 무시(다음 실행에 재시도) */ }
    }

    public static void Unregister()
    {
        if (ExtId.StartsWith("__")) return;
        try { Registry.CurrentUser.DeleteSubKeyTree($@"{PolicyBase}\{ExtId}", false); } catch { }
    }
}
