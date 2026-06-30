using System.Text.Json;

namespace D102LabelPrinter;

/// <summary>
/// 전표 자동 분할조회 캐시 저장소(JSON 파일 기반).
///
/// 사용자 PC의 C 드라이브(%LocalAppData%\D102LabelPrinter\cache\)에 chunk 단위로 저장.
/// 사용자가 큰 범위(예: 6개월)를 검색하면 확장이 1일/1주 chunk로 분할하여 서버에 N번
/// 요청 → 응답을 여기 캐싱 → 다음 검색은 캐시 hit으로 즉시.
///
/// 파일명: `{pathSanitized}__{date}.json`  (date = chunk 시작일 YYYY-MM-DD)
/// 내용:   { path, date, html, fetchedAt(unix ms), sizeBytes }
///
/// 동시성: 파일 단위 lock(name) 으로 단순화. 서로 다른 chunk는 병렬 OK.
/// </summary>
public static class CacheStore
{
    static readonly object _statsLock = new();

    /// <summary>실제 사용 디렉토리. FixedConfig.CachePath 비었으면 기본 경로.</summary>
    public static string Dir
    {
        get
        {
            var cfg = AppState.Config?.CachePath;
            if (!string.IsNullOrWhiteSpace(cfg)) return cfg;
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "D102LabelPrinter", "cache");
        }
    }

    static string Sanitize(string s) =>
        string.Join("_", (s ?? "").Split(Path.GetInvalidFileNameChars()))
              .Replace('/', '_').Replace('\\', '_');

    static string KeyPath(string path, string date) =>
        Path.Combine(Dir, $"{Sanitize(path)}__{date}.json");

    /// <summary>캐시 조회. hit이면 entry, miss면 null.</summary>
    public static CacheEntry Get(string path, string date)
    {
        try
        {
            var f = KeyPath(path, date);
            if (!File.Exists(f)) return null;
            var json = File.ReadAllText(f);
            return JsonSerializer.Deserialize<CacheEntry>(json, AppState.Camel);
        }
        catch { return null; }
    }

    /// <summary>캐시 저장(덮어쓰기).</summary>
    public static void Put(string path, string date, string html)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var entry = new CacheEntry
            {
                Path = path,
                Date = date,
                Html = html ?? "",
                FetchedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                SizeBytes = System.Text.Encoding.UTF8.GetByteCount(html ?? "")
            };
            var f = KeyPath(path, date);
            File.WriteAllText(f, JsonSerializer.Serialize(entry, AppState.Camel));
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[Cache.Put] " + e.Message); }
    }

    /// <summary>옵션: olderThanDays(예: 30) 이전 파일 삭제. null이면 전체 삭제.</summary>
    public static int Clear(int? olderThanDays = null)
    {
        int removed = 0;
        try
        {
            if (!Directory.Exists(Dir)) return 0;
            var threshold = olderThanDays.HasValue
                ? DateTime.UtcNow.AddDays(-olderThanDays.Value)
                : (DateTime?)null;
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                if (threshold == null || File.GetLastWriteTimeUtc(f) < threshold.Value)
                {
                    try { File.Delete(f); removed++; } catch { }
                }
            }
        }
        catch { }
        return removed;
    }

    /// <summary>요약 통계.</summary>
    public static CacheStats Stats()
    {
        var s = new CacheStats { Path = Dir };
        try
        {
            if (!Directory.Exists(Dir)) return s;
            var files = Directory.GetFiles(Dir, "*.json");
            s.Entries = files.Length;
            long total = 0; long oldest = long.MaxValue, newest = 0;
            foreach (var f in files)
            {
                var fi = new FileInfo(f);
                total += fi.Length;
                var t = ((DateTimeOffset)fi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                if (t < oldest) oldest = t;
                if (t > newest) newest = t;
            }
            s.SizeBytes = total;
            s.OldestMs = (oldest == long.MaxValue) ? 0 : oldest;
            s.NewestMs = newest;
        }
        catch { }
        return s;
    }
}

public class CacheEntry
{
    public string Path { get; set; } = "";
    public string Date { get; set; } = "";
    public string Html { get; set; } = "";
    public long FetchedAt { get; set; }
    public long SizeBytes { get; set; }
}

public class CacheStats
{
    public string Path { get; set; } = "";
    public int Entries { get; set; }
    public long SizeBytes { get; set; }
    public long OldestMs { get; set; }
    public long NewestMs { get; set; }
}
