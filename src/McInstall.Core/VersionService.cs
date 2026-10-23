using System.Net;
using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed record ResolvedVersion(string Id, JsonNode Json, string RawJson, string Url);

public sealed class VersionService
{
    private readonly HttpClient _http;
    private readonly Endpoints _endpoints;
    private readonly IUrlRewriter _rewriter;
    private JsonNode? _manifest;
    private DateTimeOffset _manifestTime;

    public VersionService(HttpClient http, Endpoints? endpoints = null, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _endpoints = endpoints ?? Endpoints.Default;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    public async Task<JsonNode> GetManifestAsync(CancellationToken ct, bool refresh = false)
    {
        if (!refresh && _manifest is not null && DateTimeOffset.UtcNow - _manifestTime < TimeSpan.FromMinutes(10))
            return _manifest;

        var text = await GetTextAsync(_endpoints.VersionManifest, ct).ConfigureAwait(false);
        var manifest = JsonNode.Parse(text) ?? throw new InvalidDataException("版本清单解析失败");
        _manifest = manifest;
        _manifestTime = DateTimeOffset.UtcNow;
        return manifest;
    }

    public async Task<IReadOnlyList<VersionSummary>> ListVersionsAsync(CancellationToken ct)
    {
        var manifest = await GetManifestAsync(ct).ConfigureAwait(false);
        var list = new List<VersionSummary>();
        if (manifest["versions"] is not JsonArray versions) return list;

        foreach (var node in versions)
        {
            if (node is not JsonObject version) continue;
            var id = (string?)version["id"];
            var url = (string?)version["url"];
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(url)) continue;
            DateTimeOffset.TryParse((string?)version["releaseTime"], out var time);
            list.Add(new VersionSummary(id, (string?)version["type"] ?? "release", time, url));
        }
        return list;
    }

    public async Task<ResolvedVersion> ResolveAsync(string idOrAlias, CancellationToken ct)
    {
        var manifest = await GetManifestAsync(ct).ConfigureAwait(false);

        var id = idOrAlias.Trim();
        if (id.Equals("latest", StringComparison.OrdinalIgnoreCase) ||
            id.Equals("latest-release", StringComparison.OrdinalIgnoreCase))
            id = (string?)manifest["latest"]?["release"] ?? id;
        else if (id.Equals("latest-snapshot", StringComparison.OrdinalIgnoreCase))
            id = (string?)manifest["latest"]?["snapshot"] ?? id;

        if (manifest["versions"] is not JsonArray versions)
            throw new InvalidDataException("版本清单里没有 versions 数组");

        JsonObject? entry = null;
        foreach (var node in versions)
        {
            if (node is JsonObject candidate &&
                string.Equals((string?)candidate["id"], id, StringComparison.OrdinalIgnoreCase))
            {
                entry = candidate;
                break;
            }
        }

        if (entry is null)
            throw new InvalidOperationException($"版本清单里找不到「{idOrAlias}」");

        var url = (string?)entry["url"] ?? throw new InvalidDataException("版本条目缺少 url");
        var raw = await GetTextAsync(url, ct).ConfigureAwait(false);
        var json = JsonNode.Parse(raw) ?? throw new InvalidDataException("版本 JSON 解析失败");
        var actualId = (string?)json["id"] ?? id;
        return new ResolvedVersion(actualId, json, raw, url);
    }

    public async Task<string> GetTextAsync(string url, CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var response = await _http.GetAsync(_rewriter.Rewrite(url), ct).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < 2)
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), ct).ConfigureAwait(false);
            }
        }
        throw new IOException($"请求失败：{url}", last);
    }
}

public static class HttpClientFactory
{
    public static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = 64,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("McInstall/0.1 (Minecraft installer)");
        return client;
    }
}
