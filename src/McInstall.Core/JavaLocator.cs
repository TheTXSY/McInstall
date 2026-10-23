using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace McInstall.Core;

public sealed record JavaInfo(string Path, int Major, string Source)
{
    public override string ToString() => $"Java {Major}  ({Path})";
}

/// <summary>探测本机 Java；找不到时按需下载 Adoptium JRE。</summary>
public sealed class JavaLocator
{
    private readonly HttpClient _http;
    private readonly IUrlRewriter _rewriter;
    private List<JavaInfo>? _cache;

    public JavaLocator(HttpClient http, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    public IReadOnlyList<JavaInfo> Discover(bool refresh = false)
    {
        if (_cache is not null && !refresh) return _cache;

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateKnownJavaExes()) candidates.Add(path);

        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            var exe = Path.Combine(javaHome, "bin", "java.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVariable.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var exe = Path.Combine(dir.Trim(), "java.exe");
            if (File.Exists(exe)) candidates.Add(exe);
        }

        var results = new List<JavaInfo>();
        foreach (var exe in candidates)
        {
            var info = Probe(exe);
            if (info is not null) results.Add(info);
        }

        _cache = results.OrderByDescending(j => j.Major).ToList();
        return _cache;
    }

    public JavaInfo? TryProbe(string javaExe) => Probe(javaExe);

    /// <summary>找满足最低版本的 Java（优先最低的可用大版本，避免用 Java 25 跑 1.12 的老 Forge）。</summary>
    public JavaInfo? Find(int requiredMajor)
        => Discover().Where(j => j.Major >= requiredMajor).OrderBy(j => j.Major).FirstOrDefault();

    public async Task<JavaInfo> EnsureAsync(
        int requiredMajor,
        string runtimeRoot,
        IInstallReporter reporter,
        ProgressTracker tracker,
        CancellationToken ct)
    {
        // 优先用完全匹配的大版本：1.20.1 的 Forge 在 Java 25 上并不保证能跑
        var exact = Discover().FirstOrDefault(j => j.Major == requiredMajor);
        if (exact is not null)
        {
            reporter.Log($"使用 Java {exact.Major}：{exact.Path}");
            return exact;
        }

        var compatible = Find(requiredMajor);
        if (compatible is not null)
            reporter.Log($"没有 Java {requiredMajor}，先使用 Java {compatible.Major} 执行安装器");

        reporter.Log($"没有找到 Java {requiredMajor}，开始下载 JRE {requiredMajor}（约 45 MB）");
        Directory.CreateDirectory(runtimeRoot);

        var url = $"https://api.adoptium.net/v3/binary/latest/{requiredMajor}/ga/windows/x64/jre/hotspot/normal/eclipse";
        var zipName = $"jre-{requiredMajor}.zip";
        var zipPath = Path.Combine(runtimeRoot, zipName);

        try
        {
            var engine = new DownloadEngine(_http, runtimeRoot, 2, _rewriter);
            tracker.SetPhase($"下载 Java {requiredMajor}");
            tracker.SetTotals(1, 50_000_000);
            await engine.RunAsync(new[]
            {
                new DownloadItem(url, zipName, null, null, VerifyExisting: false, DisplayName: $"JRE {requiredMajor}"),
            }, tracker, ct).ConfigureAwait(false);

            var extractDir = Path.Combine(runtimeRoot, $"jre-{requiredMajor}");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            reporter.Log("解压 JRE…");
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            TryDelete(zipPath);

            var javaExe = Directory.EnumerateFiles(extractDir, "java.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidOperationException("下载的 JRE 里没有找到 java.exe");

            var info = Probe(javaExe) ?? throw new InvalidOperationException("下载的 Java 无法运行");
            _cache = null;
            reporter.Log($"已安装 Java {info.Major}：{info.Path}");
            return info;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && compatible is not null)
        {
            reporter.Log($"下载 Java {requiredMajor} 失败（{ex.Message}），先改用 Java {compatible.Major}");
            return compatible;
        }
    }

    public static string DefaultRuntimeRoot()
    {
        // 优先放在工具自己旁边（<exe 同目录>\McInstall\runtime），避免动用户 AppData
        var toolRoot = Path.Combine(AppContext.BaseDirectory, "McInstall", "runtime");
        try
        {
            Directory.CreateDirectory(toolRoot);
            return toolRoot;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "McInstall", "runtime");
        }
    }

    private static IEnumerable<string> EnumerateKnownJavaExes()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var runtimeRoot = Path.Combine(appData, ".minecraft", "runtime");
        foreach (var exe in SafeEnumerate(runtimeRoot, 6)) yield return exe;

        var roots = new List<string>();
        foreach (var baseDir in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                 })
        {
            if (string.IsNullOrEmpty(baseDir)) continue;
            roots.Add(Path.Combine(baseDir, "Java"));
            roots.Add(Path.Combine(baseDir, "Eclipse Adoptium"));
            roots.Add(Path.Combine(baseDir, "AdoptOpenJDK"));
            roots.Add(Path.Combine(baseDir, "Zulu"));
            roots.Add(Path.Combine(baseDir, "BellSoft"));
            roots.Add(Path.Combine(baseDir, "Amazon Corretto"));
            roots.Add(Path.Combine(baseDir, "Programs", "Eclipse Adoptium"));
            roots.Add(Path.Combine(baseDir, "Programs", "Java"));
            roots.Add(Path.Combine(baseDir, "Microsoft", "jdk"));
        }

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var exe in SafeEnumerate(root, 4)) yield return exe;
        }
    }

    private static IEnumerable<string> SafeEnumerate(string root, int maxDepth)
    {
        if (!Directory.Exists(root)) yield break;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        var seen = 0;

        while (queue.Count > 0 && seen < 400)
        {
            var (dir, depth) = queue.Dequeue();
            string[] files;
            try { files = Directory.GetFiles(dir, "java.exe"); }
            catch { continue; }

            foreach (var file in files)
            {
                seen++;
                yield return file;
            }

            if (depth >= maxDepth) continue;
            string[] subdirs;
            try { subdirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var sub in subdirs) queue.Enqueue((sub, depth + 1));
        }
    }

    private static JavaInfo? Probe(string javaExe)
    {
        if (!File.Exists(javaExe)) return null;
        try
        {
            var info = new ProcessStartInfo(javaExe, "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(info);
            if (process is null) return null;
            var text = process.StandardError.ReadToEnd() + process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(true); } catch { }
                return null;
            }

            var match = Regex.Match(text, "version \"(\\d+)(?:\\.(\\d+))?", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            var major = int.Parse(match.Groups[1].Value);
            if (major == 1 && match.Groups[2].Success) major = int.Parse(match.Groups[2].Value);
            return new JavaInfo(javaExe, major, "local");
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
