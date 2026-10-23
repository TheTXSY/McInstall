using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace McInstall.App;

/// <summary>把 Windows 标题栏染成和界面一致的深/浅色（Win10 20H1+ / Win11）。</summary>
internal static class WindowTheme
{
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmCaptionColor = 35;
    private const int DwmTextColor = 36;

    public static void Apply(Window window, bool dark)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        var enabled = dark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref enabled, sizeof(int));

        if (Environment.OSVersion.Version.Build < 22000) return;

        // Win11：自定义边框时也保留系统圆角
        var corner = 2;
        DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));

        // COLORREF = 0x00BBGGRR
        var caption = dark ? 0x00170D08 : 0x00FBF8F7;
        var text = dark ? 0x00FAEFE9 : 0x002A170F;
        DwmSetWindowAttribute(handle, DwmCaptionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(handle, DwmTextColor, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
