using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Floe.Core.Interop;
using Floe.Core.Models;

namespace Floe.App.Windows;

internal static class GlassHost
{
    public static DwmResult Apply(Window window, BackdropType type, bool dark, bool round = true)
    {
        var hwnd = new WindowInteropHelper(window).Handle;

        if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = Colors.Transparent;
        }

        WindowStyling.MakeNonActivatingToolWindow(hwnd);

        if (type == BackdropType.None)
        {
            DwmEffects.DisableBackdrop(hwnd);
            return default;
        }

        return DwmEffects.Apply(hwnd, type, dark, round);
    }
}
