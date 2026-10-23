using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed record VersionSummary(string Id, string Type, DateTimeOffset ReleaseTime, string Url);

/// <summary>Minecraft 版本 JSON / 资源索引的读取工具。</summary>
public static class McJson
{
    public static JsonNode? Node(JsonNode? root, params string[] path)
    {
        JsonNode? current = root;
        foreach (var key in path)
        {
            if (current is not JsonObject obj) return null;
            current = obj[key];
        }
        return current;
    }

    public static string? Str(JsonNode? root, params string[] path)
        => Node(root, path)?.GetValue<string>();

    public static long? Long(JsonNode? root, params string[] path)
        => Node(root, path)?.GetValue<long>();

    public static DownloadItem? ClientDownload(JsonNode version, string versionId)
    {
        var url = Str(version, "downloads", "client", "url");
        if (string.IsNullOrEmpty(url)) return null;
        return new DownloadItem(
            url,
            $"versions/{versionId}/{versionId}.jar",
            Long(version, "downloads", "client", "size"),
            Str(version, "downloads", "client", "sha1"),
            VerifyExisting: true,
            DisplayName: $"客户端 {versionId}.jar");
    }

    public static DownloadItem? AssetIndexDownload(JsonNode version)
    {
        var url = Str(version, "assetIndex", "url");
        var id = Str(version, "assetIndex", "id");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(id)) return null;
        return new DownloadItem(
            url,
            $"assets/indexes/{id}.json",
            Long(version, "assetIndex", "size"),
            Str(version, "assetIndex", "sha1"),
            VerifyExisting: true,
            DisplayName: $"资源索引 {id}.json");
    }

    public static DownloadItem? LoggingDownload(JsonNode version)
    {
        var url = Str(version, "logging", "client", "file", "url");
        var id = Str(version, "logging", "client", "file", "id");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(id)) return null;
        return new DownloadItem(
            url,
            $"assets/log_configs/{id}",
            Long(version, "logging", "client", "file", "size"),
            Str(version, "logging", "client", "file", "sha1"),
            VerifyExisting: true,
            DisplayName: $"日志配置 {id}");
    }

    /// <summary>枚举需要落盘的库（含 natives 分类器）。</summary>
    public static IEnumerable<DownloadItem> LibraryDownloads(JsonNode version)
    {
        if (version["libraries"] is not JsonArray libraries) yield break;
        var arch = NativeArchToken();

        foreach (var node in libraries)
        {
            if (node is not JsonObject library) continue;
            if (!McRules.Evaluate(library["rules"])) continue;

            var name = (string?)library["name"];
            if (string.IsNullOrEmpty(name)) continue;

            if (library["downloads"] is JsonObject downloads)
            {
                if (downloads["artifact"] is JsonObject artifact)
                {
                    var relative = (string?)artifact["path"] ?? MavenPath(name);
                    var url = (string?)artifact["url"] ?? "https://libraries.minecraft.net/" + relative;
                    yield return new DownloadItem(
                        url,
                        "libraries/" + relative,
                        artifact["size"]?.GetValue<long>(),
                        (string?)artifact["sha1"],
                        VerifyExisting: true,
                        DisplayName: Path.GetFileName(relative));
                }

                if (library["natives"] is JsonObject natives)
                {
                    var key = (string?)natives["windows"];
                    if (!string.IsNullOrEmpty(key))
                    {
                        key = key.Replace("${arch}", arch, StringComparison.Ordinal);
                        if (downloads["classifiers"] is JsonObject classifiers && classifiers[key] is JsonObject classifier)
                        {
                            var relative = (string?)classifier["path"] ?? MavenPath(name, key);
                            var url = (string?)classifier["url"] ?? "https://libraries.minecraft.net/" + relative;
                            yield return new DownloadItem(
                                url,
                                "libraries/" + relative,
                                classifier["size"]?.GetValue<long>(),
                                (string?)classifier["sha1"],
                                VerifyExisting: true,
                                DisplayName: Path.GetFileName(relative));
                        }
                    }
                }
            }
            else if (!string.IsNullOrEmpty(name) && name.Contains(':'))
            {
                // 没有 downloads 段：按 maven 规则推导（Fabric 的 profile 就是这种）
                var relative = MavenPath(name);
                var repository = (string?)library["url"];
                var baseUrl = string.IsNullOrEmpty(repository)
                    ? "https://libraries.minecraft.net/"
                    : repository;
                yield return new DownloadItem(
                    baseUrl.TrimEnd('/') + "/" + relative,
                    "libraries/" + relative,
                    null,
                    null,
                    VerifyExisting: false,
                    DisplayName: Path.GetFileName(relative));
            }
        }
    }

    /// <summary>枚举资源索引里的对象。</summary>
    public static IEnumerable<DownloadItem> AssetDownloads(JsonNode assetIndex, string assetsBase)
    {
        if (assetIndex["objects"] is not JsonObject objects) yield break;

        foreach (var (name, value) in objects)
        {
            if (value is not JsonObject obj) continue;
            var hash = (string?)obj["hash"];
            var size = obj["size"]?.GetValue<long>();
            if (string.IsNullOrEmpty(hash) || hash.Length < 2) continue;

            var prefix = hash[..2];
            yield return new DownloadItem(
                $"{assetsBase.TrimEnd('/')}/{prefix}/{hash}",
                $"assets/objects/{prefix}/{hash}",
                size,
                hash,
                VerifyExisting: false,
                DisplayName: name);
        }
    }

    /// <summary>把 maven 坐标转换成 libraries 下的相对路径。</summary>
    public static string MavenPath(string name, string? classifier = null)
    {
        var extension = "jar";
        var at = name.IndexOf('@');
        if (at >= 0)
        {
            extension = name[(at + 1)..];
            name = name[..at];
        }

        var parts = name.Split(':');
        var group = parts.Length > 0 ? parts[0] : "";
        var artifact = parts.Length > 1 ? parts[1] : "artifact";
        var version = parts.Length > 2 ? parts[2] : "0";
        var existingClassifier = parts.Length > 3 ? parts[3] : null;

        var file = $"{artifact}-{version}";
        if (!string.IsNullOrEmpty(existingClassifier)) file += "-" + existingClassifier;
        if (!string.IsNullOrEmpty(classifier)) file += "-" + classifier;
        file += "." + extension;

        return $"{group.Replace('.', '/')}/{artifact}/{version}/{file}";
    }

    public static string NativeArchToken() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => "32",
        Architecture.Arm64 => "arm64",
        _ => "64",
    };

    public static int JavaMajor(JsonNode version) => (int)(Long(version, "javaVersion", "majorVersion") ?? 8);

    public static VersionSummary Summarize(JsonNode version, string url)
    {
        var id = Str(version, "id") ?? "";
        var type = Str(version, "type") ?? "release";
        DateTimeOffset.TryParse(Str(version, "releaseTime"), out var time);
        return new VersionSummary(id, type, time, url);
    }
}
