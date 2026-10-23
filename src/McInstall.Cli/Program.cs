using System.Diagnostics;
using McInstall.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var argv = args;
if (argv.Length == 0 || argv[0] is "-h" or "--help" or "help")
{
    PrintHelp();
    return 0;
}

var command = argv[0];
var options = ParseOptions(argv.Skip(1).ToArray());

try
{
    switch (command)
    {
        case "dirs":
            return ShowDirs(options);
        case "list":
            return await ListAsync(options);
        case "install":
            return await InstallAsync(options);
        case "verify":
            return await VerifyAsync(options);
        case "repair":
            return await RepairAsync(options);
        default:
            Console.Error.WriteLine($"未知命令：{command}");
            PrintHelp();
            return 2;
    }
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("已取消。");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine("失败：" + ex.Message);
    if (options.ContainsKey("verbose")) Console.Error.WriteLine(ex);
    return 1;
}

static int ShowDirs(Dictionary<string, string?> options)
{
    var pclDir = ResolvePclDir(options.GetValueOrDefault("pcl"));
    var configured = PclPaths.ReadConfiguredGameDir(pclDir);
    var gameDir = PclPaths.ResolveGameDir(pclDir, options.GetValueOrDefault("game"));
    Console.WriteLine($"PCL 目录    : {pclDir}{(File.Exists(Path.Combine(pclDir, "PCL2.exe")) ? "（已找到 PCL2.exe）" : "（未找到 PCL2.exe）")}");
    Console.WriteLine($"配置的游戏目录: {configured ?? "(未配置)"}");
    Console.WriteLine($"实际游戏目录  : {gameDir}{(Directory.Exists(gameDir) ? "" : "（不存在，安装时创建）")}");
    Console.WriteLine($"已安装版本    : {string.Join(", ", PclPaths.EnumerateInstalledVersions(gameDir))}");
    return 0;
}

static async Task<int> ListAsync(Dictionary<string, string?> options)
{
    using var http = HttpClientFactory.Create();
    var service = new VersionService(http);
    var versions = await service.ListVersionsAsync(CancellationToken.None);
    var releases = versions.Where(v => v.Type == "release").ToList();
    var snapshots = versions.Where(v => v.Type == "snapshot").ToList();

    Console.WriteLine($"共 {versions.Count} 个版本：正式版 {releases.Count}，快照 {snapshots.Count}");
    Console.WriteLine("最近正式版：" + string.Join(", ", releases.Take(8).Select(v => v.Id)));
    Console.WriteLine("最近快照  ：" + string.Join(", ", snapshots.Take(5).Select(v => v.Id)));
    return 0;
}

static async Task<int> InstallAsync(Dictionary<string, string?> options)
{
    var version = options.GetValueOrDefault("mc") ?? options.GetValueOrDefault("version");
    if (string.IsNullOrEmpty(version))
    {
        Console.Error.WriteLine("需要 --mc <版本号>（例如 --mc 1.20.1，或 --mc latest）");
        return 2;
    }

    var pclDir = ResolvePclDir(options.GetValueOrDefault("pcl"));
    var gameDir = PclPaths.ResolveGameDir(pclDir, options.GetValueOrDefault("game"));
    var threads = int.TryParse(options.GetValueOrDefault("threads"), out var t) ? t : 16;

    Console.WriteLine($"游戏目录：{gameDir}");
    Console.WriteLine($"目标版本：{version}    线程：{threads}");

    using var http = HttpClientFactory.Create();
    var installer = new MinecraftInstaller(http);
    var reporter = new ConsoleReporter();
    var installOptions = new InstallOptions
    {
        Threads = threads,
        FullVerify = options.ContainsKey("full"),
        Loader = options.GetValueOrDefault("loader"),
        LoaderVersion = options.GetValueOrDefault("loader-version"),
        OptifineVersion = options.GetValueOrDefault("optifine"),
        VersionName = options.GetValueOrDefault("name"),
    };

    var started = Stopwatch.StartNew();
    var result = await installer.InstallAsync(gameDir, version, installOptions, reporter, CancellationToken.None);
    reporter.Stop();

    Console.WriteLine();
    Console.WriteLine($"完成：{result.VersionId}");
    Console.WriteLine($"目录：{result.VersionDirectory}");
    Console.WriteLine($"库 {result.Libraries} 个，资源 {result.Assets} 个，用时 {result.Elapsed.TotalSeconds:0.0}s");
    Console.WriteLine("下一步：打开 PCL，版本列表里应该能看到它；如果没刷新，重启 PCL。");
    return 0;
}

static async Task<int> RepairAsync(Dictionary<string, string?> options)
{
    var version = options.GetValueOrDefault("mc") ?? options.GetValueOrDefault("version");
    if (string.IsNullOrEmpty(version))
    {
        Console.Error.WriteLine("需要 --mc <版本号>");
        return 2;
    }

    var pclDir = ResolvePclDir(options.GetValueOrDefault("pcl"));
    var gameDir = PclPaths.ResolveGameDir(pclDir, options.GetValueOrDefault("game"));
    Console.WriteLine($"游戏目录：{gameDir}");

    using var http = HttpClientFactory.Create();
    var reporter = new ConsoleReporter();
    var service = new RepairService(http);
    var report = await service.RepairAsync(gameDir, version, 16, options.ContainsKey("full"), reporter, CancellationToken.None);
    reporter.Stop();

    Console.WriteLine($"修复前：库缺 {report.MissingLibrariesBefore}，资源缺 {report.MissingAssetsBefore}");
    Console.WriteLine($"修复后：库缺 {report.MissingLibrariesAfter}，资源缺 {report.MissingAssetsAfter}");
    foreach (var problem in report.RemainingProblems.Take(10)) Console.WriteLine("  - " + problem);
    return report.Success ? 0 : 1;
}

static async Task<int> VerifyAsync(Dictionary<string, string?> options)
{
    var version = options.GetValueOrDefault("mc") ?? options.GetValueOrDefault("version");
    if (string.IsNullOrEmpty(version))
    {
        Console.Error.WriteLine("需要 --mc <版本号>");
        return 2;
    }

    var pclDir = ResolvePclDir(options.GetValueOrDefault("pcl"));
    var gameDir = PclPaths.ResolveGameDir(pclDir, options.GetValueOrDefault("game"));
    var report = await InstallVerifier.VerifyAsync(gameDir, version, options.ContainsKey("full"));

    Console.WriteLine($"版本 {version}：库 {report.TotalLibraries}（缺 {report.MissingLibraries}），资源 {report.TotalAssets}（缺 {report.MissingAssets}）");
    foreach (var problem in report.Problems) Console.WriteLine("  - " + problem);
    return report.Problems.Count == 0 ? 0 : 1;
}

static string ResolvePclDir(string? explicitDirectory)
{
    if (!string.IsNullOrWhiteSpace(explicitDirectory)) return Path.GetFullPath(explicitDirectory);
    return PclPaths.FindPclDirectory(Directory.GetCurrentDirectory())
        ?? PclPaths.FindPclDirectory(AppContext.BaseDirectory)
        ?? Directory.GetCurrentDirectory();
}

static Dictionary<string, string?> ParseOptions(string[] arguments)
{
    var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < arguments.Length; i++)
    {
        var arg = arguments[i];
        if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
        var key = arg[2..];
        string? value = null;
        var eq = key.IndexOf('=');
        if (eq >= 0)
        {
            value = key[(eq + 1)..];
            key = key[..eq];
        }
        else if (i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = arguments[++i];
        }
        result[key] = value;
    }
    return result;
}

static void PrintHelp()
{
    Console.WriteLine("""
    McInstall.Cli — Minecraft 版本安装器（测试/命令行版）

    用法：
      McInstall.Cli dirs                        显示 PCL 目录 / 游戏目录探测结果
      McInstall.Cli list                        列出可安装版本
      McInstall.Cli install --mc <id> [选项]     安装原版
      McInstall.Cli verify --mc <id> [--full]   校验已安装版本

    选项：
      --game <目录>        指定游戏目录（默认按 PCL 配置探测）
      --pcl <目录>         指定 PCL 目录（默认从当前目录向上找 PCL2.exe）
      --threads <N>        下载并发（默认 16）
      --loader <名字>      fabric / forge / neoforge
      --loader-version <v> 指定加载器版本（默认自动选）
      --optifine [版本]    配合 Forge 安装 OptiFine（作为 mod，默认 latest）
      --full               全量 SHA1 校验
      --name <名字>        自定义版本名
      --verbose            出错时打印堆栈
    """);
}

sealed class ConsoleReporter : IInstallReporter
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private string _phase = "";
    private ProgressSnapshot? _last;
    private long _lastPrintTicks;
    private readonly bool _redirected = Console.IsOutputRedirected;

    public void Log(string message)
    {
        ClearLine();
        Console.WriteLine($"[{_clock.Elapsed:mm\\:ss}] {message}");
    }

    public void Phase(string phase)
    {
        _phase = phase;
        ClearLine();
        Console.WriteLine($"[{_clock.Elapsed:mm\\:ss}] == {phase} ==");
    }

    public void Progress(ProgressSnapshot snapshot)
    {
        _last = snapshot;
        var now = Stopwatch.GetTimestamp();
        var minInterval = _redirected ? Stopwatch.Frequency * 2 : Stopwatch.Frequency / 5;
        if (now - _lastPrintTicks < minInterval) return;
        _lastPrintTicks = now;

        var line = $"{snapshot.Phase} {snapshot.Fraction * 100,5:0.0}%  " +
                   $"{snapshot.DoneFiles}/{snapshot.TotalFiles} 文件  " +
                   $"{MinecraftInstaller.FormatBytes((long)snapshot.BytesPerSecond)}/s  " +
                   (snapshot.Eta is { } eta ? $"剩余 {eta:mm\\:ss}  " : "") +
                   Truncate(snapshot.CurrentFile, 48);

        if (_redirected) Console.WriteLine(line);
        else
        {
            Console.Write('\r');
            Console.Write(line.PadRight(Math.Min(Console.WindowWidth - 1, 160)));
        }
    }

    public void Stop()
    {
        if (!_redirected) { Console.Write('\r'); Console.Write(new string(' ', Math.Min(Console.WindowWidth - 1, 160))); Console.Write('\r'); }
        if (_last is not null && _redirected) { }
    }

    private void ClearLine()
    {
        if (_redirected) return;
        Console.Write('\r');
        Console.Write(new string(' ', Math.Min(Console.WindowWidth - 1, 160)));
        Console.Write('\r');
    }

    private static string Truncate(string? text, int length)
        => string.IsNullOrEmpty(text) ? "" : (text.Length <= length ? text : "…" + text[^length..]);
}
