using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace D102LabelPrinter;

public static class ExtSync
{
    public const string RawBase = "https://raw.githubusercontent.com/aredsea/ubishop-barcode-ext/main/";

    private static readonly HttpClient Client = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public class SyncResult
    {
        public bool Changed;
        public int Updated;
        public int Pending;
        public string RemoteVersion;
        public string Error;
    }

    public static async Task<SyncResult> SyncAsync()
    {
        await _gate.WaitAsync();
        var result = new SyncResult();
        try
        {
            string json = await Client.GetStringAsync(RawBase + "shell-files.json?_=" + DateTime.UtcNow.Ticks);
            var index = JsonSerializer.Deserialize<ShellIndex>(json, JsonOptions)
                ?? throw new InvalidDataException("shell-files.json 파싱 실패");
            result.RemoteVersion = index.Version;

            string stableDir = ChromeExt.StableExtensionDir;
            var diff = (index.Files ?? new List<ShellFile>())
                .Where(f => !File.Exists(LocalPath(stableDir, f.Path)) ||
                    !string.Equals(FileSha256(LocalPath(stableDir, f.Path)), f.Sha256, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => string.Equals(f.Path, "manifest.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();

            if (diff.Count == 0) return result;

            string stagingDir = stableDir + ".staging";
            Directory.CreateDirectory(stagingDir);
            var stagedThisRun = new List<string>();
            bool validationFailed = false;

            try
            {
                foreach (var file in diff)
                {
                    byte[] bytes = await Client.GetByteArrayAsync(RawBase + file.Path.Replace('\\', '/'));
                    string stagingPath = LocalPath(stagingDir, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(stagingPath));
                    stagedThisRun.Add(stagingPath);
                    await File.WriteAllBytesAsync(stagingPath, bytes);

                    if (!string.Equals(BytesSha256(bytes), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(stagingPath); } catch { }
                        result.Pending++;
                        validationFailed = true;
                    }
                }
            }
            catch (Exception e)
            {
                DeleteStagedFiles(stagedThisRun);
                result.Error = e.Message;
                return result;
            }

            if (validationFailed)
            {
                DeleteStagedFiles(stagedThisRun);
                return result;
            }

            foreach (var file in diff)
            {
                string stagingPath = LocalPath(stagingDir, file.Path);
                string stablePath = LocalPath(stableDir, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(stablePath));
                try
                {
                    // chrome이 대상 파일을 FileShare.Read|Write(Delete 불허)로 열어둔 상태에서도
                    // 교체되도록 내용 덮어쓰기를 쓴다. File.Move(overwrite)는 대상 삭제가 필요해
                    // lock 시 IOException 발생(실측 확인: Move는 pending=6, WriteAllBytes는 통과).
                    File.WriteAllBytes(stablePath, File.ReadAllBytes(stagingPath));
                    try { File.Delete(stagingPath); } catch { }
                    result.Updated++;
                }
                catch (IOException)
                {
                    result.Pending++;
                }
            }

            result.Changed = result.Updated > 0;
            return result;
        }
        catch (Exception e)
        {
            result.Error = e.Message;
            return result;
        }
        finally
        {
            DeleteStagingDirIfEmpty();
            _gate.Release();
        }
    }

    public static void FlushPendingIfChromeClosed()
    {
        try
        {
            if (BrowserIsRunning("chrome") || BrowserIsRunning("msedge") || BrowserIsRunning("whale")) return;

            string stagingDir = ChromeExt.StableExtensionDir + ".staging";
            if (!Directory.Exists(stagingDir)) return;

            var files = Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories)
                .OrderBy(f => string.Equals(Path.GetRelativePath(stagingDir, f), "manifest.json", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToList();
            foreach (var stagingPath in files)
            {
                string stablePath = LocalPath(ChromeExt.StableExtensionDir, Path.GetRelativePath(stagingDir, stagingPath));
                Directory.CreateDirectory(Path.GetDirectoryName(stablePath));
                try
                {
                    File.Move(stagingPath, stablePath, true);
                }
                catch (IOException) { }
            }

            DeleteStagingDirIfEmpty();
        }
        catch { }
    }

    private static bool BrowserIsRunning(string name)
    {
        var processes = Process.GetProcessesByName(name);
        try { return processes.Length > 0; }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private static string LocalPath(string root, string path) =>
        Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string BytesSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void DeleteStagedFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
            try { File.Delete(path); } catch { }
    }

    private static void DeleteStagingDirIfEmpty()
    {
        try
        {
            string stagingDir = ChromeExt.StableExtensionDir + ".staging";
            if (Directory.Exists(stagingDir) &&
                !Directory.EnumerateFiles(stagingDir, "*", SearchOption.AllDirectories).Any())
                Directory.Delete(stagingDir, true);
        }
        catch { }
    }

    private class ShellIndex
    {
        public string Version { get; set; }
        public List<ShellFile> Files { get; set; }
    }

    private class ShellFile
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
    }
}
