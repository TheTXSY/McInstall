namespace McInstall.Core;

/// <summary>下载源配置。镜像默认关闭（BMCLAPI 2026-10 实测已不可用）。</summary>
public sealed class Endpoints
{
    public string VersionManifest { get; init; } = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public string AssetsBase { get; init; } = "https://resources.download.minecraft.net";
    public string FabricMeta { get; init; } = "https://meta.fabricmc.net";
    public string ForgeMaven { get; init; } = "https://maven.minecraftforge.net";
    public string ForgePromotions { get; init; } = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
    public string NeoForgeMaven { get; init; } = "https://maven.neoforged.net/releases";
    public string OptifineBase { get; init; } = "https://optifine.net";
    public string MojangRuntimeManifest { get; init; } =
        "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";

    public static Endpoints Default { get; } = new();
}

/// <summary>可选的 URL 重写（镜像/代理）。默认不动。</summary>
public interface IUrlRewriter
{
    string Rewrite(string url);
}

public sealed class NullUrlRewriter : IUrlRewriter
{
    public static NullUrlRewriter Instance { get; } = new();
    public string Rewrite(string url) => url;
}

/// <summary>简单的 host 前缀镜像（例如把官方 host 换成镜像 host）。</summary>
public sealed class HostMirrorUrlRewriter : IUrlRewriter
{
    private readonly IReadOnlyDictionary<string, string> _map;

    public HostMirrorUrlRewriter(IReadOnlyDictionary<string, string> map) => _map = map;

    public string Rewrite(string url)
    {
        foreach (var (from, to) in _map)
        {
            if (url.StartsWith(from, StringComparison.OrdinalIgnoreCase))
                return to + url[from.Length..];
        }
        return url;
    }
}
