using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JunkCleaner.Ui;

/// <summary>Включает тёмную заголовочную полосу (DWM), чтобы она совпадала с нашей темой.</summary>
public static class MysticTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attr, ref int attrValue, int cbAttribute);

    public static void ApplyWhenReady(Window window)
    {
        if (window.IsLoaded)
            ApplyCore(window);
        else
            window.Loaded += OnLoaded;

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            window.Loaded -= OnLoaded;
            ApplyCore(window);
        }
    }

    private static void ApplyCore(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int useDark = 1;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref useDark, sizeof(int)) != 0)
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeLegacy, ref useDark, sizeof(int));
    }
}
