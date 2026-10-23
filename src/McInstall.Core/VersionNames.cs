namespace McInstall.Core;

/// <summary>版本命名与去重。</summary>
public static class VersionNames
{
    /// <summary>生成默认版本名：原版用 MC 版本号，带加载器时追加后缀。</summary>
    public static string Suggest(string mcVersion, string? loader, string? loaderVersion)
    {
        if (string.IsNullOrWhiteSpace(loader) || loader.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
            return mcVersion;

        var suffix = loader.ToLowerInvariant() switch
        {
            "fabric" => string.IsNullOrWhiteSpace(loaderVersion) ? "Fabric" : $"Fabric {loaderVersion}",
            "forge" => string.IsNullOrWhiteSpace(loaderVersion) ? "Forge" : $"Forge {loaderVersion}",
            "neoforge" => string.IsNullOrWhiteSpace(loaderVersion) ? "NeoForge" : $"NeoForge {loaderVersion}",
            _ => loader,
        };
        return $"{mcVersion}-{suffix}";
    }

    /// <summary>若名字已被占用，依次尝试 "base (2)"、"base (3)"…</summary>
    public static string MakeUnique(string baseName, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var name = baseName.Trim();
        if (name.Length == 0) name = "Minecraft";
        if (!taken.Contains(name)) return name;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{name} ({i})";
            if (!taken.Contains(candidate)) return candidate;
        }
        return $"{name} ({DateTime.Now:HHmmss})";
    }
}
