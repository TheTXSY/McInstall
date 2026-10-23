using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed record MergedVersion(
    JsonObject Json,
    string RootId,
    IReadOnlyList<string> Chain,
    IReadOnlyList<string> Errors);

/// <summary>处理 inheritsFrom：把子版本与父版本合并成一份「完整」JSON（PCL 内部也是这么干的）。</summary>
public static class VersionChain
{
    public static MergedVersion Load(string gameDirectory, string versionId)
    {
        var chain = new List<string>();
        var errors = new List<string>();
        var merged = Merge(gameDirectory, versionId, chain, errors);
        return new MergedVersion(
            merged ?? new JsonObject(),
            chain.Count > 0 ? chain[^1] : versionId,
            chain,
            errors);
    }

    private static JsonObject? Merge(string gameDirectory, string versionId, List<string> chain, List<string> errors)
    {
        var jsonPath = PclPaths.VersionJsonPath(gameDirectory, versionId);
        if (!File.Exists(jsonPath))
        {
            errors.Add($"缺少版本 JSON：versions/{versionId}/{versionId}.json");
            return null;
        }

        JsonObject? child;
        try
        {
            child = JsonNode.Parse(File.ReadAllText(jsonPath)) as JsonObject;
        }
        catch (Exception ex)
        {
            errors.Add($"版本 JSON 无法解析（{versionId}）：{ex.Message}");
            return null;
        }

        if (child is null)
        {
            errors.Add($"版本 JSON 为空：{versionId}");
            return null;
        }

        chain.Add(versionId);

        var parentId = (string?)child["inheritsFrom"];
        if (string.IsNullOrEmpty(parentId)) return child;

        var parent = Merge(gameDirectory, parentId, chain, errors);
        if (parent is null)
        {
            errors.Add($"前置版本 {parentId} 缺失（{versionId} 需要它才能启动）");
            return child;
        }

        var merged = (JsonObject)child.DeepClone();

        var libraries = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { child["libraries"], parent["libraries"] })
        {
            if (source is not JsonArray array) continue;
            foreach (var item in array)
            {
                var name = (string?)item?["name"];
                if (!string.IsNullOrEmpty(name) && !seen.Add(name)) continue;
                libraries.Add(item?.DeepClone());
            }
        }
        if (libraries.Count > 0) merged["libraries"] = libraries;

        merged["assetIndex"] ??= parent["assetIndex"]?.DeepClone();
        merged["downloads"] ??= parent["downloads"]?.DeepClone();
        merged["mainClass"] ??= parent["mainClass"]?.DeepClone();
        merged["assets"] ??= parent["assets"]?.DeepClone();
        merged["javaVersion"] ??= parent["javaVersion"]?.DeepClone();
        merged["logging"] ??= parent["logging"]?.DeepClone();

        return merged;
    }
}
