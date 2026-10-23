using System.Text.Json;
using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed class InstallOptions
{
    public int Threads { get; init; } = 16;
    public bool FullVerify { get; init; }
    public bool UseMirror { get; init; }
    public string? JavaPath { get; init; }
    public string? Loader { get; init; }
    public string? LoaderVersion { get; init; }
    public string? OptifineVersion { get; init; }
    public string? VersionName { get; init; }
}

public sealed record InstallResult(
    string VersionId,
    string VersionDirectory,
    int Libraries,
    int Assets,
    long DownloadedBytes,
    TimeSpan Elapsed);

public sealed class MinecraftInstaller
{
    private readonly HttpClient _http;
    private readonly Endpoints _endpoints;
    private readonly VersionService _versions;
    private readonly IUrlRewriter _rewriter;

    public MinecraftInstaller(HttpClient http, Endpoints? endpoints = null, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _endpoints = endpoints ?? Endpoints.Default;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
        _versions = new VersionService(http, _endpoints, _rewriter);
    }

    public async Task<InstallResult> InstallAsync(
        string gameDirectory,
        string versionRequest,
        InstallOptions options,
        IInstallReporter reporter,
        CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        EnsureWritable(gameDirectory);

        reporter.Phase("解析版本");
        var resolved = await _versions.ResolveAsync(versionRequest, ct).ConfigureAwait(false);
        var versionId = options.VersionName ?? resolved.Id;
        reporter.Log($"目标版本：{versionId}（{McJson.Str(resolved.Json, "type") ?? "?"}）");

        EnsureLauncherProfiles(gameDirectory);

        var versionDir = PclPaths.VersionDir(gameDirectory, versionId);
        Directory.CreateDirectory(versionDir);

        // 1) 版本 JSON 落盘（原样保存，不裁剪字段）
        var jsonPath = PclPaths.VersionJsonPath(gameDirectory, versionId);
        var jsonText = resolved.RawJson;
        var parsed = JsonNode.Parse(jsonText);
        if (parsed is JsonObject obj && (string?)obj["id"] != versionId)
        {
            obj["id"] = versionId;
            jsonText = obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        WriteAllTextAtomic(jsonPath, jsonText);
        reporter.Log($"已写入版本 JSON：{jsonPath}");

        // 2) 第一梯队：客户端、资源索引、日志配置、依赖库
        var tracker = new ProgressTracker();
        using var timer = new Timer(_ => reporter.Progress(tracker.Snapshot()), null, 200, 250);

        var primary = new List<DownloadItem>();
        var client = McJson.ClientDownload(resolved.Json, versionId);
        if (client is not null) primary.Add(client);
        else reporter.Log("警告：该版本没有 downloads.client（可能是远古版本，需要另行处理）");

        var assetIndex = McJson.AssetIndexDownload(resolved.Json);
        if (assetIndex is not null) primary.Add(assetIndex);

        var logging = McJson.LoggingDownload(resolved.Json);
        if (logging is not null) primary.Add(logging);

        var libraries = McJson.LibraryDownloads(resolved.Json)
            .DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        primary.AddRange(libraries);

        reporter.Phase("客户端与依赖库");
        tracker.SetTotals(primary.Count, primary.Sum(i => i.Size ?? 0));
        reporter.Log($"开始下载：{primary.Count} 个文件（{FormatBytes(primary.Sum(i => i.Size ?? 0))}）");

        var engine = new DownloadEngine(_http, gameDirectory, options.Threads, _rewriter);
        await engine.RunAsync(primary, tracker, ct).ConfigureAwait(false);

        // 3) 第二梯队：资源文件
        var assetIndexPath = Path.Combine(gameDirectory, "assets", "indexes", McJson.Str(resolved.Json, "assetIndex", "id") + ".json");
        var assets = new List<DownloadItem>();
        if (File.Exists(assetIndexPath))
        {
            var indexNode = JsonNode.Parse(await File.ReadAllTextAsync(assetIndexPath, ct).ConfigureAwait(false));
            if (indexNode is not null)
                assets.AddRange(McJson.AssetDownloads(indexNode, _endpoints.AssetsBase));
        }
        else if (McJson.Str(resolved.Json, "assetIndex", "url") is { Length: > 0 } indexUrl)
        {
            reporter.Log("资源索引下载失败，尝试重新获取");
            var text = await _versions.GetTextAsync(indexUrl, ct).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(assetIndexPath)!);
            await File.WriteAllTextAsync(assetIndexPath, text, ct).ConfigureAwait(false);
            assets.AddRange(McJson.AssetDownloads(JsonNode.Parse(text)!, _endpoints.AssetsBase));
        }

        if (assets.Count > 0)
        {
            // 资源对象按 hash 去重：同一个 hash 可能被多个资源名引用
            assets = assets
                .DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            reporter.Phase("资源文件");
            tracker.AddTotals(assets.Count, assets.Sum(i => i.Size ?? 0));
            reporter.Log($"开始下载资源：{assets.Count} 个对象（{FormatBytes(assets.Sum(i => i.Size ?? 0))}）");
            await engine.RunAsync(assets, tracker, ct).ConfigureAwait(false);
        }

        // 4) 校验
        reporter.Phase("校验安装");
        var report = await InstallVerifier.VerifyAsync(gameDirectory, versionId, options.FullVerify, ct).ConfigureAwait(false);
        if (report.Problems.Count > 0)
        {
            foreach (var problem in report.Problems.Take(20))
                reporter.Log("校验问题：" + problem);
            throw new InvalidOperationException($"安装校验未通过：{report.Problems.Count} 个问题");
        }

        var finalId = versionId;
        if (!string.IsNullOrWhiteSpace(options.Loader) &&
            !options.Loader.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
        {
            finalId = options.Loader.ToLowerInvariant() switch
            {
                "fabric" => await new FabricInstaller(_http, _endpoints, _rewriter)
                    .InstallAsync(gameDirectory, versionId, options.LoaderVersion, options, reporter, tracker, ct)
                    .ConfigureAwait(false),
                "forge" => await new ForgeInstaller(_http, ModLoaderKind.Forge, _endpoints, _rewriter)
                    .InstallAsync(gameDirectory, versionId, McJson.JavaMajor(resolved.Json), options.LoaderVersion,
                        options, new JavaLocator(_http, _rewriter), reporter, tracker, ct)
                    .ConfigureAwait(false),
                "neoforge" => await new ForgeInstaller(_http, ModLoaderKind.NeoForge, _endpoints, _rewriter)
                    .InstallAsync(gameDirectory, versionId, McJson.JavaMajor(resolved.Json), options.LoaderVersion,
                        options, new JavaLocator(_http, _rewriter), reporter, tracker, ct)
                    .ConfigureAwait(false),
                _ => throw new NotSupportedException($"暂不支持加载器：{options.Loader}"),
            };
        }

        if (!string.IsNullOrWhiteSpace(options.OptifineVersion))
        {
            if (!string.Equals(options.Loader, "forge", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("OptiFine 目前只支持「Forge + OptiFine mod」的方式，请先选择 Forge 加载器");

            await new OptiFineInstaller(_http, _endpoints, _rewriter)
                .InstallAsModAsync(gameDirectory, versionId, finalId, options.OptifineVersion, reporter, tracker, ct)
                .ConfigureAwait(false);
        }

        reporter.Phase("完成");
        reporter.Log($"完成：{finalId}，库 {libraries.Count} 个，资源 {assets.Count} 个");

        return new InstallResult(
            finalId,
            PclPaths.VersionDir(gameDirectory, finalId),
            libraries.Count,
            assets.Count,
            tracker.Snapshot().DoneBytes,
            DateTimeOffset.UtcNow - started);
    }

    private static void EnsureWritable(string gameDirectory)
    {
        try
        {
            Directory.CreateDirectory(gameDirectory);
            var probe = Path.Combine(gameDirectory, ".mcinstall_write_test");
            File.WriteAllText(probe, "test");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new UnauthorizedAccessException(
                "没有权限写入游戏目录：\n" + gameDirectory + "\n\n" +
                "常见原因：安全软件（卡巴斯基 / 360 / 火绒 / Defender 受控文件夹访问）拦截了未签名的程序。\n" +
                "可以试试：\n" +
                "1) 在杀软里把 McInstall.exe 设为「受信任」或加入排除项；\n" +
                "2) 右键「以管理员身份运行」；\n" +
                "3) 点程序里的「更改目录」，换一个不受保护的目录。", ex);
        }
    }

    public static void EnsureLauncherProfiles(string gameDirectory)    {
        var path = Path.Combine(gameDirectory, "launcher_profiles.json");
        if (File.Exists(path)) return;

        const string minimal = """
        {
          "profiles": {},
          "selectedProfile": "",
          "clientToken": "00000000000000000000000000000000",
          "authenticationDatabase": {}
        }
        """;
        File.WriteAllText(path, minimal);
    }

    private static void WriteAllTextAtomic(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
        if (bytes < 1024L * 1024 * 1024) return (bytes / 1024.0 / 1024).ToString("0.0") + " MB";
        return (bytes / 1024.0 / 1024 / 1024).ToString("0.00") + " GB";
    }
}
