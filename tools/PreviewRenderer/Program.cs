using System.Collections;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McInstall.App;

namespace PreviewRenderer;

/// <summary>
/// 不显示窗口、不触发 Loaded/网络，直接把 MainWindow / RenameWindow 渲染成 PNG。
/// 只用于设计检查，不参与发布。
/// </summary>
internal static class Program
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int Width = 1280;
    private const int Height = 880;

    [STAThread]
    private static void Main(string[] args)
    {
        var output = args.Length > 0 ? args[0] : Path.Combine("tools", "ui-shots", "preview");
        Directory.CreateDirectory(output);

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        ThemeManager.Initialize(AppTheme.Dark);

        var window = new MainWindow();
        var content = (FrameworkElement)window.Content;

        Seed(window);
        RenderStates(window, content, output);

        RenderTheme(window, content, output, "dark");
        RenderRenameDialog(output, "dark");

        ThemeManager.SetMode(AppTheme.Light);
        Pump(window);
        RenderTheme(window, content, output, "light");
        RenderRenameDialog(output, "light");

        Console.WriteLine($"preview written to {Path.GetFullPath(output)}");
        app.Shutdown();
    }

    private static void Seed(MainWindow window)
    {
        SetField(window, "_gameDir", @"C:\Users\dsctx\Desktop\PCL2\.minecraft");
        SetField(window, "_pclDir", @"C:\Users\dsctx\Desktop\PCL2");
        Invoke(window, "UpdateHeader");

        var view = (IList)GetField(window, "_view")!;
        var itemType = typeof(MainWindow).GetNestedType("VersionListItem", BindingFlags.NonPublic)!;
        foreach (var (id, date) in new[]
                 {
                     ("1.21.4", "2024-12-03"), ("1.21.3", "2024-10-23"), ("1.21.1", "2024-08-08"),
                     ("1.20.6", "2024-04-29"), ("1.20.1", "2023-06-12"), ("1.19.4", "2023-03-14"),
                     ("1.18.2", "2022-02-28"), ("1.16.5", "2021-01-14"),
                 })
        {
            view.Add(Activator.CreateInstance(itemType, id, date, "release"));
        }

        var list = (System.Windows.Controls.ListBox)window.FindName("VersionList");
        list.SelectedIndex = 0;
        Pump(window);

        var loaderVersions = (System.Windows.Controls.ComboBox)window.FindName("LoaderVersionBox");
        loaderVersions.ItemsSource = new[] { "自动（推荐）", "0.16.9", "0.16.8" };
        loaderVersions.SelectedIndex = 0;

        var gameDir = (System.Windows.Controls.TextBox)window.FindName("GameDirBox");
        gameDir.Text = @"C:\Users\dsctx\Desktop\PCL2\.minecraft";

        var threads = (System.Windows.Controls.Slider)window.FindName("ThreadsSlider");
        threads.Value = 12;
        ((System.Windows.Controls.TextBlock)window.FindName("ThreadsLabel")).Text = "12";

        var name = (System.Windows.Controls.TextBox)window.FindName("NameBox");
        name.Text = "1.21.4";

        ((System.Windows.Controls.TextBlock)window.FindName("TargetSummaryText")).Text =
            "1.21.4 · 原版 · 安装到 .minecraft";
        ((System.Windows.Controls.TextBlock)window.FindName("PercentText")).Text = "42%";
        ((System.Windows.Controls.TextBlock)window.FindName("DownloadSpeedText")).Text = "12.4 MB/s";
        ((System.Windows.Controls.TextBlock)window.FindName("EtaText")).Text = "剩余 00:38";
        ((System.Windows.Controls.TextBlock)window.FindName("CurrentFileText")).Text =
            "assets/objects/8e/8e9c1a0e6f1f0e1f4b8c1d2a4e6f8a0c1d3e5f7a";
        ((System.Windows.Controls.TextBlock)window.FindName("PhaseText")).Text = "资源文件";
        ((System.Windows.Controls.TextBlock)window.FindName("DetailText")).Text =
            "1832/3598 文件 · 421.6 MB/621.3 MB · 12.4 MB/s";
        ((System.Windows.Controls.ProgressBar)window.FindName("ProgressBar")).Value = 0.42;

        ((System.Windows.Controls.TextBlock)window.FindName("SummaryText")).Text =
            "Minecraft：1.21.4（正式版）\n加载器：原版\nOptiFine：不安装\n线程：12    全量校验：关\nJava：自动探测\n安装到：C:\\Users\\dsctx\\Desktop\\PCL2\\.minecraft";

        ((System.Windows.Controls.ComboBox)window.FindName("ThemeBox")).SelectedIndex = 2;

        Invoke(window, "UpdateFooter");
    }

    private static void RenderStates(MainWindow window, FrameworkElement content, string output)
    {
        var loading = (FrameworkElement)window.FindName("LoadingPanel");
        var empty = (FrameworkElement)window.FindName("EmptyPanel");
        var list = (FrameworkElement)window.FindName("VersionList");
        var count = (System.Windows.Controls.TextBlock)window.FindName("CountText");
        var retry = (FrameworkElement)window.FindName("RetryButton");
        var emptyText = (System.Windows.Controls.TextBlock)window.FindName("EmptyText");

        loading.Visibility = Visibility.Visible;
        empty.Visibility = Visibility.Collapsed;
        list.Visibility = Visibility.Collapsed;
        count.Text = "…";
        Render(content, Path.Combine(output, "dark-loading.png"));

        loading.Visibility = Visibility.Collapsed;
        list.Visibility = Visibility.Visible;
        empty.Visibility = Visibility.Visible;
        retry.Visibility = Visibility.Visible;
        count.Text = "—";
        emptyText.Text = "版本清单获取失败，检查网络后重试";
        Render(content, Path.Combine(output, "dark-error.png"));

        empty.Visibility = Visibility.Collapsed;
        retry.Visibility = Visibility.Collapsed;
        count.Text = "8 个";
        emptyText.Text = "没有匹配的版本";
    }

    private static void RenderTheme(MainWindow window, FrameworkElement content, string output, string theme)
    {
        for (var step = 1; step <= 5; step++)
        {
            SelectStep(window, step);
            Render(content, Path.Combine(output, $"{theme}-{step:00}.png"));
        }

        var overlay = (FrameworkElement)window.FindName("SettingsOverlay");
        overlay.Visibility = Visibility.Visible;
        Render(content, Path.Combine(output, $"{theme}-settings.png"));
        overlay.Visibility = Visibility.Collapsed;
    }

    private static void SelectStep(MainWindow window, int step)
    {
        SetField(window, "_step", step);
        SetField(window, "_maxStep", 5);
        for (var i = 1; i <= 5; i++)
        {
            var panel = (UIElement)window.FindName($"Step{i}Panel");
            panel.Visibility = i == step ? Visibility.Visible : Visibility.Collapsed;
        }

        Invoke(window, "UpdateStepChrome");
        Invoke(window, "UpdateFooter");
        Pump(window);
    }

    private static void RenderRenameDialog(string output, string theme)
    {
        var dialog = new RenameWindow("1.21.4", new[] { "1.21.4", "1.20.1" });
        var content = (FrameworkElement)dialog.Content;
        content.Measure(new Size(480, double.PositiveInfinity));
        var height = Math.Max(200, content.DesiredSize.Height);
        content.Arrange(new Rect(0, 0, 480, height));
        content.UpdateLayout();
        Render(content, Path.Combine(output, $"{theme}-rename.png"), 480, (int)Math.Ceiling(height));
        dialog.Close();
    }

    private static void Render(FrameworkElement content, string path, int width = Width, int height = Height)
    {
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine(path);
    }

    private static void Pump(DispatcherObject target)
    {
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        target.Dispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static object? GetField(object target, string name)
        => target.GetType().GetField(name, Private)!.GetValue(target);

    private static void SetField(object target, string name, object value)
        => target.GetType().GetField(name, Private)!.SetValue(target, value);

    private static object? Invoke(object target, string name, params object?[] args)
        => target.GetType().GetMethod(name, Private)!.Invoke(target, args);
}
