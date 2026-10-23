using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace McInstall.App;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>浅色 / 深色主题：默认跟随 Windows，也可以手动固定。</summary>
public static class ThemeManager
{
    private static ResourceDictionary? _current;
    private static DispatcherTimer? _watcher;

    public static AppTheme Mode { get; private set; } = AppTheme.System;

    public static bool IsDark { get; private set; }

    public static event Action? Changed;

    public static void Initialize(AppTheme mode)
    {
        Mode = mode;
        Apply(Resolve(mode), notify: false);

        if (_watcher is not null) return;
        _watcher = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _watcher.Tick += (_, _) =>
        {
            if (Mode != AppTheme.System) return;
            var dark = SystemPrefersDark();
            if (dark != IsDark) Apply(dark, notify: true);
        };
        _watcher.Start();
    }

    public static void SetMode(AppTheme mode)
    {
        Mode = mode;
        Apply(Resolve(mode), notify: true);
    }

    public static AppTheme Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System,
    };

    public static string ToConfig(AppTheme mode) => mode switch
    {
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => "system",
    };

    public static bool SystemPrefersDark()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool Resolve(AppTheme mode)
        => mode == AppTheme.Dark || (mode == AppTheme.System && SystemPrefersDark());

    private static void Apply(bool dark, bool notify)
    {
        IsDark = dark;
        var app = Application.Current;
        if (app is null) return;

        var assemblyName = typeof(ThemeManager).Assembly.GetName().Name;
        var dictionary = new ResourceDictionary
        {
            Source = new Uri(
                $"pack://application:,,,/{assemblyName};component/Themes/{(dark ? "Dark" : "Light")}.xaml",
                UriKind.Absolute),
        };

        if (_current is not null) app.Resources.MergedDictionaries.Remove(_current);
        app.Resources.MergedDictionaries.Insert(0, dictionary);
        _current = dictionary;

        if (notify) Changed?.Invoke();
    }
}
