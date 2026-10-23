using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace McInstall.Core;

/// <summary>一个待下载文件。TargetPath 相对于游戏目录。</summary>
public sealed record DownloadItem(
    string Url,
    string TargetPath,
    long? Size = null,
    string? Sha1 = null,
    bool VerifyExisting = true,
    string? DisplayName = null)
{
    public string Name => DisplayName ?? TargetPath.Replace('\\', '/');
}

public sealed class DownloadFailure
{
    public required DownloadItem Item { get; init; }
    public required Exception Error { get; init; }
}

public sealed class DownloadFailedException : Exception
{
    public IReadOnlyList<DownloadFailure> Failures { get; }

    public DownloadFailedException(IReadOnlyList<DownloadFailure> failures)
        : base($"有 {failures.Count} 个文件下载失败（第一个：{failures[0].Item.Name} - {failures[0].Error.Message}）")
        => Failures = failures;
}

public sealed class DownloadEngine
{
    private readonly HttpClient _http;
    private readonly string _rootDirectory;
    private readonly int _concurrency;
    private readonly IUrlRewriter _rewriter;

    public DownloadEngine(HttpClient http, string rootDirectory, int concurrency, IUrlRewriter? rewriter = null)
    {
        _http = http;
        _rootDirectory = rootDirectory;
        _concurrency = Math.Clamp(concurrency, 1, 64);
        _rewriter = rewriter ?? NullUrlRewriter.Instance;
    }

    public async Task RunAsync(IReadOnlyList<DownloadItem> items, ProgressTracker tracker, CancellationToken ct)
    {
        if (items.Count == 0) return;

        var failures = new ConcurrentBag<DownloadFailure>();
        using var gate = new SemaphoreSlim(_concurrency);

        var tasks = items.Select(item => Task.Run(async () =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await DownloadOneAsync(item, tracker, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new DownloadFailure { Item = item, Error = ex });
            }
            finally
            {
                gate.Release();
            }
        }, ct)).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (failures.Count > 0)
            throw new DownloadFailedException(failures.ToArray());
    }

    private async Task DownloadOneAsync(DownloadItem item, ProgressTracker tracker, CancellationToken ct)
    {
        var targetPath = Path.Combine(_rootDirectory, item.TargetPath.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        if (File.Exists(targetPath))
        {
            var existing = new FileInfo(targetPath);
            var ok = item.Size is long size
                ? existing.Length == size && (!item.VerifyExisting || item.Sha1 is null || await Sha1MatchesAsync(targetPath, item.Sha1, ct).ConfigureAwait(false))
                : item.Sha1 is not null && await Sha1MatchesAsync(targetPath, item.Sha1, ct).ConfigureAwait(false);

            if (ok)
            {
                tracker.AddBytes(existing.Length);
                tracker.FileDone();
                return;
            }

            TryDelete(targetPath);
        }

        var partPath = targetPath + ".part";
        long counted = 0;
        if (File.Exists(partPath))
        {
            var partLength = new FileInfo(partPath).Length;
            if (item.Size is long expected && partLength > expected) TryDelete(partPath);
            else if (partLength > 0)
            {
                counted = partLength;
                tracker.AddBytes(counted);
            }
        }

        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await TransferAsync(item, partPath, tracker, counted, ct).ConfigureAwait(false);
                counted = -1; // 已计入
                lastError = null;
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                lastError = ex;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden)
            {
                lastError = ex;
                break; // 404/403 重试无意义
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (File.Exists(partPath))
            {
                counted = new FileInfo(partPath).Length;
            }
            else if (counted > 0)
            {
                tracker.AddBytes(-counted);
                counted = 0;
            }

            if (attempt < 2)
                await Task.Delay(TimeSpan.FromMilliseconds(600 * (attempt + 1)), ct).ConfigureAwait(false);
        }

        if (lastError is not null) throw lastError;

        // 长度/SHA1 终检
        var partInfo = new FileInfo(partPath);
        if (item.Size is long finalSize && partInfo.Length != finalSize)
            throw new IOException($"{item.Name} 大小不符：期望 {finalSize}，实际 {partInfo.Length}");

        if (item.Sha1 is not null && !await Sha1MatchesAsync(partPath, item.Sha1, ct).ConfigureAwait(false))
        {
            TryDelete(partPath);
            throw new IOException($"{item.Name} SHA1 校验失败");
        }

        File.Move(partPath, targetPath, overwrite: true);
        tracker.FileDone();
    }

    private async Task TransferAsync(DownloadItem item, string partPath, ProgressTracker tracker, long alreadyCounted, CancellationToken ct)
    {
        var start = File.Exists(partPath) ? new FileInfo(partPath).Length : 0;
        if (item.Size is long size && start > size)
        {
            TryDelete(partPath);
            start = 0;
        }

        if (item.Size is long expected && start == expected)
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, _rewriter.Rewrite(item.Url));
        request.Headers.UserAgent.ParseAdd("McInstall/0.1");
        if (start > 0)
            request.Headers.Range = new RangeHeaderValue(start, null);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && start > 0)
        {
            TryDelete(partPath);
            tracker.AddBytes(-start);
            throw new IOException("服务器拒绝续传，重新开始");
        }

        response.EnsureSuccessStatusCode();

        var append = response.StatusCode == HttpStatusCode.PartialContent && start > 0;
        if (!append && start > 0)
        {
            // 服务器忽略了 Range，从头来
            tracker.AddBytes(-start);
            start = 0;
        }

        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        await using var destination = new FileStream(
            partPath,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        tracker.BeginFile(item.Name);

        var buffer = new byte[1 << 16];
        long written = append ? start : 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (read <= 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
            written += read;
            tracker.AddBytes(read);
        }

        await destination.FlushAsync(timeout.Token).ConfigureAwait(false);
    }

    private static async Task<bool> Sha1MatchesAsync(string path, string expected, CancellationToken ct)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA1.HashDataAsync(stream, ct).ConfigureAwait(false);
            return Convert.ToHexString(hash).Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
    }
}
