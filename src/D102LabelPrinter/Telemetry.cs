using System.Text;
using System.Text.Json;

namespace D102LabelPrinter;

/// <summary>
/// 사용 현황 + 캐시 효과 측정. events.jsonl(한 줄당 한 이벤트)로 누적.
///
/// 이벤트 종류(type):
///   page_load    : 페이지 진입. {path, ttfb, totalLoad, domNodes}
///   chunk_fetch  : 서버 chunk fetch. {path, date, ms, bytes}
///   cache_hit    : 캐시 hit. {path, date}
///   cache_miss   : 캐시 miss(→ fetch). {path, date}
///   search_done  : 분할 검색 완료. {path, chunks, hits, miss, totalMs, savedMs}
///
/// FixedConfig.TelemetryEnabled = false 면 기록 안 함(개인 PC 옵트아웃).
/// </summary>
public static class Telemetry
{
    static readonly object _writeLock = new();
    const int MaxLines = 50_000;     // 50k 줄 넘으면 절반 잘라냄

    public static string Dir
    {
        get
        {
            var cfg = AppState.Config?.CachePath;
            string baseDir = !string.IsNullOrWhiteSpace(cfg)
                ? Path.GetDirectoryName(cfg)
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "D102LabelPrinter");
            return baseDir;
        }
    }
    public static string LogPath => Path.Combine(Dir, "events.jsonl");

    public static void Add(string type, object payload = null)
    {
        if (AppState.Config != null && !AppState.Config.TelemetryEnabled) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var rec = new Dictionary<string, object>
            {
                { "ts", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                { "type", type ?? "" }
            };
            if (payload != null)
            {
                var json = JsonSerializer.Serialize(payload, AppState.Camel);
                var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, AppState.Camel);
                if (dict != null) foreach (var kv in dict) rec[kv.Key] = kv.Value;
            }
            string line = JsonSerializer.Serialize(rec, AppState.Camel) + "\n";
            lock (_writeLock)
            {
                File.AppendAllText(LogPath, line, Encoding.UTF8);
                // 너무 커지면 절반 자르기
                if (new FileInfo(LogPath).Length > 5_000_000)
                {
                    var lines = File.ReadAllLines(LogPath);
                    if (lines.Length > MaxLines)
                        File.WriteAllLines(LogPath, lines.Skip(lines.Length / 2));
                }
            }
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[Telemetry.Add] " + e.Message); }
    }

    /// <summary>최근 N일(default 7) 이벤트 요약.</summary>
    public static TelemetrySummary Summary(int days = 7)
    {
        var s = new TelemetrySummary { Days = days };
        try
        {
            if (!File.Exists(LogPath)) return s;
            long sinceMs = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeMilliseconds();
            foreach (var line in File.ReadLines(LogPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var rec = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line, AppState.Camel);
                    if (rec == null) continue;
                    if (!rec.TryGetValue("ts", out var tsEl) || !rec.TryGetValue("type", out var typeEl)) continue;
                    long ts = tsEl.ValueKind == JsonValueKind.Number ? tsEl.GetInt64() : 0;
                    if (ts < sinceMs) continue;
                    string type = typeEl.GetString() ?? "";
                    switch (type)
                    {
                        case "page_load": s.PageLoads++;
                            if (rec.TryGetValue("totalLoad", out var l) && l.ValueKind == JsonValueKind.Number)
                                s.TotalLoadMs += l.GetInt64();
                            break;
                        case "cache_hit": s.CacheHits++; break;
                        case "cache_miss": s.CacheMiss++; break;
                        case "chunk_fetch":
                            if (rec.TryGetValue("ms", out var m) && m.ValueKind == JsonValueKind.Number)
                                s.TotalChunkFetchMs += m.GetInt64();
                            s.ChunkFetches++;
                            break;
                        case "search_done":
                            s.Searches++;
                            if (rec.TryGetValue("savedMs", out var sv) && sv.ValueKind == JsonValueKind.Number)
                                s.SavedMsTotal += sv.GetInt64();
                            break;
                    }
                }
                catch { }
            }
        }
        catch { }
        if (s.PageLoads > 0) s.AvgLoadMs = s.TotalLoadMs / s.PageLoads;
        int total = s.CacheHits + s.CacheMiss;
        if (total > 0) s.HitRate = (double)s.CacheHits / total;
        return s;
    }
}

public class TelemetrySummary
{
    public int Days { get; set; }
    public int PageLoads { get; set; }
    public long TotalLoadMs { get; set; }
    public long AvgLoadMs { get; set; }
    public int CacheHits { get; set; }
    public int CacheMiss { get; set; }
    public double HitRate { get; set; }
    public int ChunkFetches { get; set; }
    public long TotalChunkFetchMs { get; set; }
    public int Searches { get; set; }
    public long SavedMsTotal { get; set; }
}
