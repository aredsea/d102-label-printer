using Velopack;
using Velopack.Sources;

namespace D102LabelPrinter;

/// <summary>GitHub 릴리스에서 프로그램 자동업데이트(Velopack). 설치본에서만 동작.</summary>
public static class Updater
{
    public const string Repo = "https://github.com/aredsea/d102-label-printer";

    public static async Task CheckAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(Repo, null, false));
            if (!mgr.IsInstalled) return;            // dev/포터블 실행은 스킵
            var info = await mgr.CheckForUpdatesAsync();
            if (info == null) return;                // 최신
            await mgr.DownloadUpdatesAsync(info);
            mgr.ApplyUpdatesAndRestart(info);        // 새 버전으로 재시작
        }
        catch { /* 네트워크/미설치 등 무시 */ }
    }
}
