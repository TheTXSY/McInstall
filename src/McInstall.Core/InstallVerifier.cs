using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed record VerifyReport(
    int MissingLibraries,
    int MissingAssets,
    int TotalLibraries,
    int TotalAssets,
    IReadOnlyList<string> Problems);

public static class InstallVerifier
{
    public static async Task<VerifyReport> VerifyAsync(
        string gameDirectory,
        string versionId,
        bool fullCheck,
        CancellationToken ct = default)
    {
        var problems = new List<string>();
        var chain = VersionChain.Load(gameDirectory, versionId);
        problems.AddRange(chain.Errors);

        var version = chain.Json;
        if (version.Count == 0)
            return new VerifyReport(0, 0, 0, 0, problems);

        if (string.IsNullOrEmpty(McJson.Str(version, "mainClass")))
            problems.Add("版本 JSON 缺少 mainClass（PCL 会拒绝识别）");

        // 客户端 jar：最底层的原版版本必须有
        var clientSize = McJson.Long(version, "downloads", "client", "size");
        if (clientSize is not null)
        {
            var jar = PclPaths.VersionJarPath(gameDirectory, chain.RootId);
            if (!File.Exists(jar))
                problems.Add($"缺少客户端 jar：versions/{chain.RootId}/{chain.RootId}.jar");
            else if (new FileInfo(jar).Length != clientSize.Value)
                problems.Add($"客户端 jar 大小不符：versions/{chain.RootId}/{chain.RootId}.jar");
        }

        var missingLibraries = 0;
        var libraries = McJson.LibraryDownloads(version)
            .DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var library in libraries)
        {
            ct.ThrowIfCancellationRequested();
            var path = Path.Combine(gameDirectory, library.TargetPath);
            if (!File.Exists(path))
            {
                missingLibraries++;
                if (problems.Count < 40) problems.Add($"缺少库：{library.TargetPath}");
                continue;
            }

            if (fullCheck && library.Sha1 is not null)
            {
                var actual = await Sha1Async(path, ct).ConfigureAwait(false);
                if (!actual.Equals(library.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    missingLibraries++;
                    if (problems.Count < 40) problems.Add($"库校验失败：{library.TargetPath}");
                }
            }
        }

        var missingAssets = 0;
        var totalAssets = 0;
        var assetIndexId = McJson.Str(version, "assetIndex", "id") ?? McJson.Str(version, "assets");
        if (!string.IsNullOrEmpty(assetIndexId))
        {
            var index = Path.Combine(gameDirectory, "assets", "indexes", assetIndexId + ".json");
            if (!File.Exists(index))
            {
                problems.Add($"缺少资源索引：assets/indexes/{assetIndexId}.json");
            }
            else
            {
                var indexNode = JsonNode.Parse(await File.ReadAllTextAsync(index, ct).ConfigureAwait(false));
                if (indexNode?["objects"] is JsonObject objects)
                {
                    totalAssets = objects.Count;
                    foreach (var (name, value) in objects)
                    {
                        var hash = (string?)value?["hash"];
                        if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;
                        var path = Path.Combine(gameDirectory, "assets", "objects", hash[..2], hash);
                        if (!File.Exists(path))
                        {
                            missingAssets++;
                            if (problems.Count < 40) problems.Add($"缺少资源：{name}");
                            continue;
                        }

                        if (fullCheck && value?["size"] is JsonValue sizeNode)
                        {
                            var size = sizeNode.GetValue<long>();
                            if (new FileInfo(path).Length != size)
                            {
                                missingAssets++;
                                if (problems.Count < 40) problems.Add($"资源大小不符：{name}");
                            }
                        }
                    }
                }
            }
        }

        return new VerifyReport(missingLibraries, missingAssets, libraries.Count, totalAssets, problems);
    }

    private static async Task<string> Sha1Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await System.Security.Cryptography.SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
