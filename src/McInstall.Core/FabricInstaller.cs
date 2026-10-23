using System.Text.Json;
using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed record FabricLoaderVersion(string Version, string Maven, bool Stable);

/// <summary>Fabric：官方 meta 直接给完整 profile JSON，改写 id/inheritsFrom 后落盘。</summary>
public sealed class FabricInstaller
{
    private readonly HttpClient _http;
    private readonly Endpoints _endpoints;
    private readonly IUrlRewriter _rewriter;

    public FabricInstaller(HttpClient http, Endpoints? endpoints = null, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _endpoints = endpoints ?? Endpoints.Default;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    public async Task<IReadOnlyList<FabricLoaderVersion>> ListLoadersAsync(string mcVersion, CancellationToken ct)
    {
        var url = $"{_endpoints.FabricMeta.TrimEnd('/')}/v2/versions/loader/{Uri.EscapeDataString(mcVersion)}";
        var text = await GetTextAsync(url, ct).ConfigureAwait(false);
        var list = new List<FabricLoaderVersion>();
        if (JsonNode.Parse(text) is not JsonArray array) return list;

        foreach (var item in array)
        {
            var loader = item?["loader"];
            var version = (string?)loader?["version"];
            if (string.IsNullOrEmpty(version)) continue;
            list.Add(new FabricLoaderVersion(
                version,
                (string?)loader?["maven"] ?? $"net.fabricmc:fabric-loader:{version}",
                loader?["stable"]?.GetValue<bool>() ?? false));
        }
        return list;
    }

    public async Task<string> InstallAsync(
        string gameDirectory,
        string mcVersion,
        string? loaderVersion,
        InstallOptions options,
        IInstallReporter reporter,
        ProgressTracker tracker,
        CancellationToken ct)
    {
        reporter.Phase("Fabric 加载器");
        var loaders = await ListLoadersAsync(mcVersion, ct).ConfigureAwait(false);
        if (loaders.Count == 0)
            throw new InvalidOperationException($"Fabric 没有支持 {mcVersion} 的加载器版本");

        FabricLoaderVersion chosen;
        if (string.IsNullOrWhiteSpace(loaderVersion))
        {
            chosen = loaders.FirstOrDefault(l => l.Stable) ?? loaders[0];
            reporter.Log($"Fabric loader：自动选择稳定版 {chosen.Version}");
        }
        else
        {
            chosen = loaders.FirstOrDefault(l => l.Version.Equals(loaderVersion, StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException($"Fabric 没有 {loaderVersion} 这个版本");
            reporter.Log($"Fabric loader：{chosen.Version}");
        }

        var profileUrl = $"{_endpoints.FabricMeta.TrimEnd('/')}/v2/versions/loader/{Uri.EscapeDataString(mcVersion)}/{Uri.EscapeDataString(chosen.Version)}/profile/json";
        var raw = await GetTextAsync(profileUrl, ct).ConfigureAwait(false);
        if (JsonNode.Parse(raw) is not JsonObject profile)
            throw new InvalidDataException("Fabric profile 解析失败");

        var versionId = options.VersionName ?? $"{mcVersion}-Fabric {chosen.Version}";
        profile["id"] = versionId;
        profile["inheritsFrom"] = mcVersion;

        var intermediary = FindIntermediary(profile);
        if (intermediary is not null && intermediary.Equals("0.0.0", StringComparison.Ordinal))
            reporter.Log("警告：该 Fabric 版本缺少 intermediary（0.0.0），可能无法启动");

        var versionDir = PclPaths.VersionDir(gameDirectory, versionId);
        Directory.CreateDirectory(versionDir);
        var jsonPath = Path.Combine(versionDir, versionId + ".json");
        var text = profile.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temp = jsonPath + ".tmp";
        await File.WriteAllTextAsync(temp, text, ct).ConfigureAwait(false);
        File.Move(temp, jsonPath, overwrite: true);
        reporter.Log($"已写入 Fabric 版本 JSON：{versionId}");

        var items = McJson.LibraryDownloads(profile).ToList();
        if (items.Count > 0)
        {
            tracker.SetPhase("Fabric 依赖库");
            tracker.SetTotals(items.Count, items.Sum(i => i.Size ?? 0));
            var engine = new DownloadEngine(_http, gameDirectory, options.Threads, _rewriter);
            await engine.RunAsync(items, tracker, ct).ConfigureAwait(false);
        }

        // 校验：库文件必须存在且非空
        var missing = items
            .Select(i => Path.Combine(gameDirectory, i.TargetPath))
            .Where(p => !File.Exists(p) || new FileInfo(p).Length == 0)
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException($"Fabric 依赖库不完整（缺 {missing.Count} 个）");

        reporter.Log($"Fabric 安装完成：{versionId}");
        return versionId;
    }

    private static string? FindIntermediary(JsonObject profile)
    {
        if (profile["libraries"] is not JsonArray libraries) return null;
        foreach (var node in libraries)
        {
            var name = (string?)node?["name"];
            if (name is null || !name.StartsWith("net.fabricmc:intermediary:", StringComparison.Ordinal)) continue;
            var parts = name.Split(':');
            return parts.Length > 2 ? parts[2] : null;
        }
        return null;
    }

    private async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(_rewriter.Rewrite(url), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
