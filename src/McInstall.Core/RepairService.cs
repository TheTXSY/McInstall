using System.Text.Json.Nodes;

namespace McInstall.Core;

public sealed record RepairReport(
    int MissingLibrariesBefore,
    int MissingAssetsBefore,
    int MissingLibrariesAfter,
    int MissingAssetsAfter,
    IReadOnlyList<string> RemainingProblems)
{
    public bool Success => MissingLibrariesAfter == 0 && MissingAssetsAfter == 0;
}

/// <summary>校验并补齐任意已安装版本（包括 PCL 自己装的版本）。</summary>
public sealed class RepairService
{
    private readonly HttpClient _http;
    private readonly Endpoints _endpoints;
    private readonly IUrlRewriter _rewriter;

    public RepairService(HttpClient http, Endpoints? endpoints = null, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _endpoints = endpoints ?? Endpoints.Default;
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    public async Task<RepairReport> RepairAsync(
        string gameDirectory,
        string versionId,
        int threads,
        bool fullVerify,
        IInstallReporter reporter,
        CancellationToken ct)
    {
        reporter.Phase("检查文件");
        var before = await InstallVerifier.VerifyAsync(gameDirectory, versionId, false, ct).ConfigureAwait(false);
        reporter.Log($"修复前：库缺 {before.MissingLibraries}，资源缺 {before.MissingAssets}");

        if (before.MissingLibraries == 0 && before.MissingAssets == 0 && before.Problems.Count == 0)
        {
            reporter.Phase("完成");
            reporter.Log("文件完整，不需要修复");
            return new RepairReport(0, 0, 0, 0, Array.Empty<string>());
        }

        var chain = VersionChain.Load(gameDirectory, versionId);
        if (chain.Errors.Count > 0)
        {
            reporter.Log("版本链有问题：" + string.Join("；", chain.Errors));
            if (chain.Json.Count == 0)
                return new RepairReport(before.MissingLibraries, before.MissingAssets,
                    before.MissingLibraries, before.MissingAssets, chain.Errors);
        }

        var tracker = new ProgressTracker();
        using var timer = new Timer(_ => reporter.Progress(tracker.Snapshot()), null, 200, 250);
        var engine = new DownloadEngine(_http, gameDirectory, threads, _rewriter);

        // 1) 客户端 jar / 日志配置 / 全部库
        var items = new List<DownloadItem>();
        if (McJson.ClientDownload(chain.Json, chain.RootId) is { } client) items.Add(client);
        if (McJson.LoggingDownload(chain.Json) is { } logging) items.Add(logging);
        items.AddRange(McJson.LibraryDownloads(chain.Json)
            .DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase));

        reporter.Phase("补齐依赖库");
        tracker.SetTotals(items.Count, items.Sum(i => i.Size ?? 0));
        await engine.RunAsync(items, tracker, ct).ConfigureAwait(false);

        // 2) 资源索引与资源对象
        var indexId = McJson.Str(chain.Json, "assetIndex", "id");
        if (!string.IsNullOrEmpty(indexId))
        {
            var indexPath = Path.Combine(gameDirectory, "assets", "indexes", indexId + ".json");
            if (!File.Exists(indexPath) && McJson.AssetIndexDownload(chain.Json) is { } indexItem)
            {
                tracker.SetPhase("补齐资源索引");
                tracker.SetTotals(1, indexItem.Size ?? 0);
                await engine.RunAsync(new[] { indexItem }, tracker, ct).ConfigureAwait(false);
            }

            if (File.Exists(indexPath))
            {
                var indexNode = JsonNode.Parse(await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false));
                if (indexNode is not null)
                {
                    var assets = McJson.AssetDownloads(indexNode, _endpoints.AssetsBase)
                        .DistinctBy(i => i.TargetPath, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (assets.Count > 0)
                    {
                        reporter.Phase("补齐资源文件");
                        tracker.SetTotals(assets.Count, assets.Sum(i => i.Size ?? 0));
                        await engine.RunAsync(assets, tracker, ct).ConfigureAwait(false);
                    }
                }
            }
        }

        reporter.Phase("复查");
        var after = await InstallVerifier.VerifyAsync(gameDirectory, versionId, fullVerify, ct).ConfigureAwait(false);
        reporter.Log($"修复后：库缺 {after.MissingLibraries}，资源缺 {after.MissingAssets}");

        return new RepairReport(
            before.MissingLibraries,
            before.MissingAssets,
            after.MissingLibraries,
            after.MissingAssets,
            after.Problems);
    }
}
