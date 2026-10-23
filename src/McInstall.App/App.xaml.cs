using System.IO;
using System.Windows;
using McInstall.Core;

namespace McInstall.App;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 先决定主题，再创建窗口，避免启动时闪一下默认颜色
        var config = McInstallConfig.Load();
        ThemeManager.Initialize(ThemeManager.Parse(config.Theme));

        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppContext.BaseDirectory, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {args.Exception}\r\n\r\n");
            }
            catch
            {
                // 忽略日志写入失败
            }

            MessageBox.Show(args.Exception.ToString(), "出现未处理的错误", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
