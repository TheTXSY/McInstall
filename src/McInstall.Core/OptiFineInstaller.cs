using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace McInstall.Core;

/// <summary>OptiFine：官方只提供 GUI 安装器，这里按「Forge + OptiFine mod」的方式安装（1.14+ 主流做法）。</summary>
public sealed class OptiFineInstaller
{
    private readonly HttpClient _http;
    private readonly Endpoints _endpoints;
    private readonly IUrlRewriter _rewriter;

    public OptiFineInstaller(HttpClient http, Endpoints? endpoints = null, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _endpoints = endpoints ?? Endpoints.Default;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    /// <summary>从 optifine.net/downloads 页面抓取该游戏版本可用的 jar 列表（新的在前）。</summary>
    public async Task<IReadOnlyList<string>> ListVersionsAsync(string mcVersion, CancellationToken ct)
    {
        var html = await GetStringAsync($"{_endpoints.OptifineBase.TrimEnd('/')}/downloads", null, ct)
            .ConfigureAwait(false);

        var pattern = @"adloadx\?f=((?:preview_)?OptiFine_" + Regex.Escape(mcVersion) + @"_[^""'&]+?\.jar)";
        var names = Regex.Matches(html, pattern)
            .Select(m => Uri.UnescapeDataString(m.Groups[1].Value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 页面顺序通常是从新到旧；再按文件名倒序稳定一下
        names.Sort(StringComparer.OrdinalIgnoreCase);
        names.Reverse();
        return names;
    }

    public async Task<string?> EnsureDownloadedAsync(
        string gameDirectory,
        string mcVersion,
        string? requested,
        IInstallReporter reporter,
        ProgressTracker tracker,
        CancellationToken ct)
    {
        var versions = await ListVersionsAsync(mcVersion, ct).ConfigureAwait(false);
        if (versions.Count == 0)
        {
            reporter.Log($"OptiFine：没有找到支持 {mcVersion} 的版本");
            return null;
        }

        string fileName;
        if (string.IsNullOrWhiteSpace(requested) ||
            requested.Equals("latest", StringComparison.OrdinalIgnoreCase))
        {
            fileName = versions[0];
            reporter.Log($"OptiFine：自动选择 {fileName}");
        }
        else
        {
            fileName = versions.FirstOrDefault(v => v.Equals(requested, StringComparison.OrdinalIgnoreCase))
                       ?? versions.FirstOrDefault(v => v.Contains(requested, StringComparison.OrdinalIgnoreCase))
                       ?? versions[0];
            reporter.Log($"OptiFine：{fileName}");
        }

        var relative = $"McInstall/cache/optifine/{fileName}";
        var target = Path.Combine(gameDirectory, relative);
        if (File.Exists(target) && new FileInfo(target).Length > 0)
            return target;

        var adUrl = $"{_endpoints.OptifineBase.TrimEnd('/')}/adloadx?f={Uri.EscapeDataString(fileName)}";
        var page = await GetStringAsync(adUrl, $"{_endpoints.OptifineBase.TrimEnd('/')}/downloads", ct)
            .ConfigureAwait(false);

        var match = Regex.Match(page, @"downloadx\?f=([^""'&]+)&x=([^""'&]+)");
        if (!match.Success)
            throw new InvalidOperationException("OptiFine 下载页没有找到可用的下载链接（可能需要手动下载）");

        var tokenUrl = $"{_endpoints.OptifineBase.TrimEnd('/')}/downloadx?f={match.Groups[1].Value}&x={match.Groups[2].Value}";
        reporter.Log("OptiFine：开始下载…");
        tracker.SetPhase("下载 OptiFine");
        tracker.SetTotals(1, 12_000_000);

        var engine = new DownloadEngine(_http, gameDirectory, 1, _rewriter);
        await engine.RunAsync(new[]
        {
            new DownloadItem(tokenUrl, relative, null, null, VerifyExisting: false, DisplayName: fileName),
        }, tracker, ct).ConfigureAwait(false);

        if (!IsZip(target))
            throw new InvalidDataException("下载到的 OptiFine 不是有效的 jar（可能被官网拦截，需要手动下载）");

        return target;
    }

    public async Task InstallAsModAsync(
        string gameDirectory,
        string mcVersion,
        string forgeVersionId,
        string? requested,
        IInstallReporter reporter,
        ProgressTracker tracker,
        CancellationToken ct)
    {
        var jar = await EnsureDownloadedAsync(gameDirectory, mcVersion, requested, reporter, tracker, ct)
            .ConfigureAwait(false);
        if (jar is null) return;

        var fileName = Path.GetFileName(jar);
        // PCL 的版本隔离可能开也可能关：两个 mods 目录都放一份，哪个生效都不会漏
        var modsDirs = new[]
        {
            Path.Combine(gameDirectory, "mods"),
            Path.Combine(PclPaths.VersionDir(gameDirectory, forgeVersionId), "mods"),
        };

        foreach (var dir in modsDirs)
        {
            Directory.CreateDirectory(dir);
            var destination = Path.Combine(dir, fileName);
            if (!File.Exists(destination))
                File.Copy(jar, destination);
        }

        reporter.Log($"OptiFine 已放入 mods：{fileName}（共 {modsDirs.Length} 个位置）");
    }

    private static bool IsZip(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            return archive.Entries.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<string> GetStringAsync(string url, string? referer, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _rewriter.Rewrite(url));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) McInstall/0.1");
        if (!string.IsNullOrEmpty(referer)) request.Headers.Referrer = new Uri(referer);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }
}
