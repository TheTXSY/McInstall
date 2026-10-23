using System.Text.Json;

namespace McInstall.Core;

/// <summary>工具自己的配置，存在 exe 同目录，不影响 PCL。</summary>
public sealed class McInstallConfig
{
    public string? PclDirectory { get; set; }
    public string? GameDirectory { get; set; }
    public int Threads { get; set; } = 16;
    public bool FullVerify { get; set; }
    public string? JavaPath { get; set; }

    /// <summary>system / light / dark；默认跟随系统。</summary>
    public string? Theme { get; set; }

    public static string ConfigPath => Path.Combine(AppContext.BaseDirectory, "McInstall.json");

    public static McInstallConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return new McInstallConfig();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<McInstallConfig>(json) ?? new McInstallConfig();
        }
        catch
        {
            return new McInstallConfig();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
            // 配置写不进去不影响主流程
        }
    }
}
