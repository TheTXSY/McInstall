using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace McInstall.Core;

public enum ModLoaderKind
{
    Forge,
    NeoForge,
}

/// <summary>Forge / NeoForge：下载官方 installer，用 Java 跑 processors，产物直接落进 .minecraft。</summary>
public sealed class ForgeInstaller
{
    private readonly HttpClient _http;
    private readonly Endpoints _endpoints;
    private readonly IUrlRewriter _rewriter;
    private readonly ModLoaderKind _kind;

    public ForgeInstaller(HttpClient http, ModLoaderKind kind, Endpoints? endpoints = null, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _kind = kind;
        _endpoints = endpoints ?? Endpoints.Default;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    public string DisplayName => _kind == ModLoaderKind.Forge ? "Forge" : "NeoForge";

    public async Task<IReadOnlyList<string>> ListVersionsAsync(string mcVersion, CancellationToken ct)
    {
        if (_kind == ModLoaderKind.Forge)
        {
            var url = $"{_endpoints.ForgeMaven.TrimEnd('/')}/net/minecraftforge/forge/maven-metadata.xml";
            var text = await GetTextAsync(url, ct).ConfigureAwait(false);
            var doc = XDocument.Parse(text);
            var prefix = mcVersion + "-";
            return doc.Descendants("version")
                .Select(v => v.Value)
                .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                .Select(v => v[prefix.Length..])
                .Distinct()
                .ToList();
        }

        var apiUrl = $"{_endpoints.NeoForgeMaven.TrimEnd('/')}/../api/maven/versions/releases/net/neoforged/neoforge";
        var legacyPrefix = IsNeoLegacy(mcVersion) ? mcVersion + "-" : null;
        if (legacyPrefix is not null)
            apiUrl = $"{_endpoints.NeoForgeMaven.TrimEnd('/')}/../api/maven/versions/releases/net/neoforged/forge";

        var json = JsonNode.Parse(await GetTextAsync(apiUrl, ct).ConfigureAwait(false));
        var prefixNeo = legacyPrefix ?? NeoVersionPrefix(mcVersion);
        if (json is JsonObject obj && obj["versions"] is JsonArray versions)
        {
            return versions
                .Select(v => v?.GetValue<string>() ?? "")
                .Where(v => v.StartsWith(prefixNeo, StringComparison.Ordinal))
                .Select(v => legacyPrefix is null ? v : v[prefixNeo.Length..])
                .Distinct()
                .ToList();
        }
        return Array.Empty<string>();
    }

    public async Task<string> PickVersionAsync(string mcVersion, string? requested, CancellationToken ct)
    {
        if (_kind == ModLoaderKind.Forge)
        {
            var promos = await GetForgePromotionsAsync(ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(requested))
            {
                var wanted = promos.TryGetValue($"{mcVersion}-{requested}", out var exact) ? exact : requested;
                return wanted;
            }
            if (promos.TryGetValue($"{mcVersion}-latest", out var latest)) return latest;
            var versions = await ListVersionsAsync(mcVersion, ct).ConfigureAwait(false);
            if (versions.Count == 0) throw new InvalidOperationException($"Forge 没有支持 {mcVersion} 的版本");
            return versions.OrderByDescending(v => v, McVersionComparer.Instance).First();
        }

        var neos = await ListVersionsAsync(mcVersion, ct).ConfigureAwait(false);
        if (neos.Count == 0) throw new InvalidOperationException($"NeoForge 没有支持 {mcVersion} 的版本");
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = neos.FirstOrDefault(v => v.Equals(requested, StringComparison.OrdinalIgnoreCase));
            return match ?? throw new InvalidOperationException($"NeoForge 没有 {requested} 这个版本");
        }
        return neos.OrderByDescending(v => v, McVersionComparer.Instance).First();
    }

    public async Task<string> InstallAsync(
        string gameDirectory,
        string mcVersion,
        int requiredJava,
        string? requestedVersion,
        InstallOptions options,
        JavaLocator javaLocator,
        IInstallReporter reporter,
        ProgressTracker tracker,
        CancellationToken ct)
    {
        reporter.Phase($"{DisplayName} 安装");
        var version = await PickVersionAsync(mcVersion, requestedVersion, ct).ConfigureAwait(false);
        reporter.Log($"{DisplayName} 版本：{version}");

        var (installerUrl, installerName) = BuildInstallerUrl(mcVersion, version);
        var cacheRelative = $"McInstall/cache/{installerName}";
        var cachePath = Path.Combine(gameDirectory, cacheRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        if (!File.Exists(cachePath))
        {
            tracker.SetPhase($"下载 {DisplayName} 安装器");
            tracker.SetTotals(1, 12_000_000);
            var engine = new DownloadEngine(_http, gameDirectory, 2, _rewriter);
            await engine.RunAsync(new[]
            {
                new DownloadItem(installerUrl, cacheRelative, null, null, VerifyExisting: false, DisplayName: installerName),
            }, tracker, ct).ConfigureAwait(false);
        }

        var expectedVersion = ReadInstallerTargetVersion(cachePath);
        reporter.Log($"安装器目标版本：{expectedVersion ?? "(未知，安装后扫描)"}");

        if (!string.IsNullOrEmpty(expectedVersion) &&
            File.Exists(PclPaths.VersionJsonPath(gameDirectory, expectedVersion)))
        {
            reporter.Log($"{DisplayName} 已经安装过（{expectedVersion}），跳过安装器");
            return expectedVersion;
        }

        JavaInfo java;
        if (!string.IsNullOrWhiteSpace(options.JavaPath) && File.Exists(options.JavaPath) &&
            javaLocator.TryProbe(options.JavaPath) is { } customJava)
        {
            java = customJava;
            reporter.Log($"使用指定的 Java {customJava.Major}：{customJava.Path}");
        }
        else
        {
            java = await javaLocator.EnsureAsync(requiredJava, JavaLocator.DefaultRuntimeRoot(), reporter, tracker, ct)
                .ConfigureAwait(false);
        }

        MinecraftInstaller.EnsureLauncherProfiles(gameDirectory);

        // Forge 安装器会在 java.io.tmpdir 下建临时目录；显式指到可写目录，避免系统 TEMP 权限问题
        var tempDirectory = Path.Combine(gameDirectory, "McInstall", "tmp");
        Directory.CreateDirectory(tempDirectory);

        var arguments = new[] { $"-Djava.io.tmpdir={tempDirectory}", "-jar", cachePath, "--installClient" };
        var exitCode = await RunProcessAsync(java.Path, arguments, gameDirectory, tempDirectory, reporter, ct).ConfigureAwait(false);
        if (exitCode != 0)
        {
            reporter.Log($"安装器退出码 {exitCode}，尝试带目录参数重试");
            var retryArgs = new[] { $"-Djava.io.tmpdir={tempDirectory}", "-jar", cachePath, "--installClient", gameDirectory };
            exitCode = await RunProcessAsync(java.Path, retryArgs, gameDirectory, tempDirectory, reporter, ct).ConfigureAwait(false);
        }

        if (exitCode != 0)
            throw new InvalidOperationException($"{DisplayName} 安装器执行失败（退出码 {exitCode}），详见日志");

        var installedId = expectedVersion;
        if (string.IsNullOrEmpty(installedId) || !File.Exists(PclPaths.VersionJsonPath(gameDirectory, installedId)))
            installedId = FindInstalledVersion(gameDirectory, mcVersion)
                          ?? throw new InvalidOperationException($"{DisplayName} 安装器没有产出可识别的版本");

        reporter.Log($"{DisplayName} 安装完成：{installedId}");
        return installedId;
    }

    private (string Url, string FileName) BuildInstallerUrl(string mcVersion, string version)
    {
        if (_kind == ModLoaderKind.Forge)
        {
            var file = $"forge-{mcVersion}-{version}-installer.jar";
            return ($"{_endpoints.ForgeMaven.TrimEnd('/')}/net/minecraftforge/forge/{mcVersion}-{version}/{file}", file);
        }

        var neoFile = $"neoforge-{version}-installer.jar";
        if (IsNeoLegacy(mcVersion))
        {
            var legacyFile = $"forge-{mcVersion}-{version}-installer.jar";
            return ($"{_endpoints.NeoForgeMaven.TrimEnd('/')}/net/neoforged/forge/{mcVersion}-{version}/{legacyFile}", legacyFile);
        }
        return ($"{_endpoints.NeoForgeMaven.TrimEnd('/')}/net/neoforged/neoforge/{version}/{neoFile}", neoFile);
    }

    private static string? ReadInstallerTargetVersion(string installerPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(installerPath);
            var entry = archive.Entries.FirstOrDefault(e => e.FullName == "install_profile.json");
            if (entry is null) return null;
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var json = JsonNode.Parse(reader.ReadToEnd());
            return McJson.Str(json, "version");
        }
        catch
        {
            return null;
        }
    }

    private static string? FindInstalledVersion(string gameDirectory, string mcVersion)
    {
        var candidates = new List<(string Id, DateTime Time)>();
        foreach (var id in PclPaths.EnumerateInstalledVersions(gameDirectory))
        {
            if (!id.Contains("forge", StringComparison.OrdinalIgnoreCase)) continue;
            if (!id.Contains(mcVersion, StringComparison.Ordinal)) continue;
            var jsonPath = PclPaths.VersionJsonPath(gameDirectory, id);
            candidates.Add((id, File.GetLastWriteTimeUtc(jsonPath)));
        }
        return candidates.OrderByDescending(c => c.Time).Select(c => c.Id).FirstOrDefault();
    }

    private async Task<Dictionary<string, string>> GetForgePromotionsAsync(CancellationToken ct)
    {
        var text = await GetTextAsync(_endpoints.ForgePromotions, ct).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (JsonNode.Parse(text) is JsonObject root && root["promos"] is JsonObject promos)
        {
            foreach (var (key, value) in promos)
            {
                if (value is not null) result[key] = value.GetValue<string>();
            }
        }
        return result;
    }

    private static string NeoVersionPrefix(string mcVersion)
    {
        // 1.20.2 → 20.2.   1.21.1 → 21.1.   26.2 → 26.2.
        if (mcVersion.StartsWith("1.", StringComparison.Ordinal))
            return mcVersion[2..] + ".";
        return mcVersion + ".";
    }

    private static bool IsNeoLegacy(string mcVersion)
        => mcVersion.StartsWith("1.20.1", StringComparison.Ordinal);

    private async Task<int> RunProcessAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string tempDirectory,
        IInstallReporter reporter,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["TEMP"] = tempDirectory;
        startInfo.Environment["TMP"] = tempDirectory;
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => LogLine(reporter, "OUT", e.Data);
        process.ErrorDataReceived += (_, e) => LogLine(reporter, "ERR", e.Data);

        reporter.Log($"执行：{Path.GetFileName(executable)} {string.Join(' ', arguments)}");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw;
        }

        return process.ExitCode;
    }

    private static void LogLine(IInstallReporter reporter, string channel, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        var text = line.Trim();
        if (text.Length < 3) return;

        // Forge 安装器输出极其啰嗦（每个类一行 Patching），过滤掉噪音
        if (text.StartsWith("Patching ", StringComparison.Ordinal)) return;
        if (text.StartsWith("Considering ", StringComparison.Ordinal)) return;
        if (text.StartsWith("Injecting ", StringComparison.Ordinal)) return;
        if (text.Contains("Download completed", StringComparison.Ordinal)) return;
        if (text.StartsWith("Downloading library from", StringComparison.Ordinal)) return;
        if (text.StartsWith("File exists", StringComparison.Ordinal)) return;
        if (text.StartsWith("Extraction completed", StringComparison.Ordinal)) return;

        reporter.Log($"[{channel}] {text}");
    }

    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(_rewriter.Rewrite(url), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
