using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace McInstall.Core;

/// <summary>定位 PCL 目录、读取 PCL 配置、推导游戏目录。</summary>
public static class PclPaths
{
    /// <summary>在常见位置（桌面、下载、文档、Program Files…）找 PCL2.exe，最多往下看一层目录。</summary>
    public static string? ProbeForPclDirectory()
    {
        var candidates = new List<string>();
        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path)) candidates.Add(path);
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Add(Path.Combine(desktop, "PCL2"));
        Add(desktop);
        Add(Path.Combine(documents, "PCL2"));
        Add(documents);
        Add(Path.Combine(userProfile, "Downloads", "PCL2"));
        Add(Path.Combine(userProfile, "Downloads"));
        Add(Path.Combine(localAppData, "Programs", "PCL2"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PCL2"));
        Add(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(candidate)) continue;
            if (File.Exists(Path.Combine(candidate, "PCL2.exe"))) return candidate;

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(candidate))
                {
                    if (File.Exists(Path.Combine(sub, "PCL2.exe"))) return sub;
                }
            }
            catch
            {
                // 没权限就算了
            }
        }
        return null;
    }

    /// <summary>从给定目录向上找 PCL2.exe。</summary>
    public static string? FindPclDirectory(string? startDirectory = null)
    {
        var dir = new DirectoryInfo(startDirectory ?? AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PCL2.exe")))
                return dir.FullName;
        }
        return null;
    }

    /// <summary>解析 PCL/Setup.ini 里的 LaunchFolderSelect（$ 代表 PCL 目录）。</summary>
    public static string? ReadConfiguredGameDir(string pclDirectory)
    {
        var ini = Path.Combine(pclDirectory, "PCL", "Setup.ini");
        if (!File.Exists(ini)) return null;

        foreach (var raw in File.ReadLines(ini))
        {
            var line = raw.Trim();
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            if (!line[..idx].Trim().Equals("LaunchFolderSelect", StringComparison.OrdinalIgnoreCase)) continue;

            var value = line[(idx + 1)..].Trim();
            if (value.Length == 0) continue;
            // PCL 把 PCL 目录记作 "$"，且 "$" 自带结尾斜杠（例如 "$.minecraft\"）
            var replacement = pclDirectory.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            value = value.Replace("$", replacement, StringComparison.Ordinal);
            value = value.TrimEnd('\\', '/');
            if (value.Length == 0) continue;
            return Path.GetFullPath(value);
        }
        return null;
    }

    /// <summary>按 PCL 自己的候选顺序推导游戏目录。</summary>
    public static string ResolveGameDir(string pclDirectory, string? overrideDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
            return Path.GetFullPath(overrideDirectory);

        var configured = ReadConfiguredGameDir(pclDirectory);
        if (configured is not null && Directory.Exists(configured))
            return configured;

        var local = Path.Combine(pclDirectory, ".minecraft");
        if (Directory.Exists(local) && Directory.Exists(Path.Combine(local, "versions")))
            return local;
        if (Directory.Exists(local))
            return local;

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        if (Directory.Exists(roaming))
            return roaming;

        return local; // 不存在也没关系，安装时会创建
    }

    public static string VersionsDir(string gameDir) => Path.Combine(gameDir, "versions");
    public static string LibrariesDir(string gameDir) => Path.Combine(gameDir, "libraries");
    public static string AssetsDir(string gameDir) => Path.Combine(gameDir, "assets");
    public static string VersionDir(string gameDir, string id) => Path.Combine(VersionsDir(gameDir), id);
    public static string VersionJsonPath(string gameDir, string id) => Path.Combine(VersionDir(gameDir, id), id + ".json");
    public static string VersionJarPath(string gameDir, string id) => Path.Combine(VersionDir(gameDir, id), id + ".jar");

    /// <summary>枚举已安装版本（有 json 的目录）。</summary>
    public static IEnumerable<string> EnumerateInstalledVersions(string gameDir)
    {
        var versions = VersionsDir(gameDir);
        if (!Directory.Exists(versions)) yield break;

        foreach (var dir in Directory.EnumerateDirectories(versions))
        {
            var name = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, name + ".json")))
                yield return name;
        }
    }

    public static string DescribeOS()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
         : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "linux";
}

/// <summary>规则求值（Mojang 的 rules 数组）。</summary>
public static class McRules
{
    public sealed record Features(bool IsDemoUser = false, bool HasCustomResolution = false, bool HasQuickPlaysSupport = false);

    public static bool Evaluate(JsonNode? rules, Features? features = null)
    {
        features ??= new Features();
        if (rules is not JsonArray array) return true;

        var allowed = false;
        foreach (var rule in array.OfType<JsonObject>())
        {
            var action = (string?)rule["action"];
            var applies = Applies(rule, features);
            if (!applies) continue;
            allowed = action is "allow";
        }
        return allowed;
    }

    private static bool Applies(JsonObject rule, Features features)
    {
        if (rule["os"] is JsonObject os)
        {
            var name = (string?)os["name"];
            if (name is not null && !name.Equals(PclPaths.DescribeOS(), StringComparison.OrdinalIgnoreCase))
                return false;

            var arch = (string?)os["arch"];
            if (arch is not null && !ArchMatches(arch)) return false;

            if (os["versionRange"] is JsonObject range && !VersionInRange(range)) return false;
        }

        if (rule["features"] is JsonObject feat)
        {
            foreach (var (key, value) in feat)
            {
                var expected = value?.GetValue<bool>() ?? false;
                var actual = key switch
                {
                    "is_demo_user" => features.IsDemoUser,
                    "has_custom_resolution" => features.HasCustomResolution,
                    "has_quick_plays_support" => features.HasQuickPlaysSupport,
                    _ => false,
                };
                if (actual != expected) return false;
            }
        }

        return true;
    }

    private static bool ArchMatches(string required)
    {
        var arch = RuntimeInformation.ProcessArchitecture;
        return required.ToLowerInvariant() switch
        {
            "x86" => arch == Architecture.X86,
            "x86_64" or "x64" => arch == Architecture.X64,
            "arm64" => arch == Architecture.Arm64,
            _ => true,
        };
    }

    private static bool VersionInRange(JsonObject range)
    {
        var current = Environment.OSVersion.Version;
        if (range["min"] is JsonValue minNode && Version.TryParse(minNode.ToString(), out var min) && current < min)
            return false;
        if (range["max"] is JsonValue maxNode && Version.TryParse(maxNode.ToString(), out var max) && current > max)
            return false;
        return true;
    }
}
