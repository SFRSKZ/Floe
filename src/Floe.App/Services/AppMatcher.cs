using Floe.App.ViewModels;
using Floe.Core.Interop;
using Floe.Core.Services;

namespace Floe.App.Services;

/// <summary>Which dock item a window belongs to; the rules live in <see cref="AppIdentity"/>.</summary>
public static class AppMatcher
{
    public static bool Matches(DockItemViewModel item, string process, string path, string appId)
        => AppIdentity.Matches(item.ProcessName, item.App.Target, item.ExePath,
            new WindowFacts(process ?? string.Empty, path ?? string.Empty, appId ?? string.Empty));

    public static bool Matches(DockItemViewModel item, AppWindow window)
        => Matches(item, window.ProcessName, window.ProcessPath, window.AppId);
}
