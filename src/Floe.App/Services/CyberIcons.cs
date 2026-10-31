using System.Windows;
using System.Windows.Media;

namespace Floe.App.Services;

/// <summary>
/// The line icons of the cyber themes, drawn in one colour instead of the apps' own artwork. An app is matched by name;
/// one with no icon of its own gets its first letter in a square. Outlines from Tabler Icons (MIT, tabler.io/icons).
/// </summary>
public static class CyberIcons
{
    private const double Box = 24;
    private const double Stroke = 1.8;

    private static readonly (string Match, string Icon)[] Apps =
    {
        ("chrome", "brand-chrome"),
        ("steam", "brand-steam"),
        ("spotify", "brand-spotify"),
        ("chatgpt", "brand-openai"),
        ("codex", "brand-openai"),
        ("claude", "floe-claude"),
        ("opencode", "floe-opencode"),
        ("terminal", "terminal-2"),
        ("powershell", "terminal-2"),
        ("code", "brand-vscode"),
        ("edge", "brand-edge"),
        ("firefox", "brand-firefox"),
        ("discord", "brand-discord"),
        ("youtube", "brand-youtube"),
        ("whatsapp", "brand-whatsapp"),
        ("telegram", "brand-telegram"),
        ("github", "brand-github"),
        ("netflix", "brand-netflix"),
        ("explorer", "folder"),
        ("gezgin", "folder"),
        ("bambu", "floe-bambu"),
        ("orca", "floe-orca"),
        ("blender", "cube"),
        ("fusion", "cube"),
        ("freecad", "cube"),
        ("photo", "photo"),
        ("foto", "photo"),
        ("paint", "brush"),
        ("music", "music"),
        ("vlc", "movie"),
        ("mail", "mail"),
        ("outlook", "mail"),
        ("hesap", "calculator"),
        ("calculator", "calculator"),
        ("note", "notes"),
        ("ayarlar", "settings"),
        ("settings", "settings"),
        ("camera", "camera"),
        ("kamera", "camera"),
        ("teams", "message-circle"),
        ("zoom", "video"),
    };

    private const string Cloud = "M6 14h12l3 -3v-2l-3 -3h-2l-3 -3h-3l-3 3v2h-1l-3 3v1z";

    private static readonly Dictionary<string, string[]> Paths = new()
    {
        // the cyber themes' own hard edged media keys and weather
        ["floe-play"] = new[] { "M7 4v16l13 -8z" },
        ["floe-pause"] = new[] { "M7 4h3v16h-3z", "M14 4h3v16h-3z" },
        ["floe-previous"] = new[] { "M19 5v14l-10 -7z", "M6 5v14" },
        ["floe-next"] = new[] { "M5 5v14l10 -7z", "M18 5v14" },
        ["wx-sunny"] = new[] { "M9 7h6l2 2v6l-2 2h-6l-2 -2v-6z", "M12 2v2", "M12 20v2", "M2 12h2", "M20 12h2", "M5 5l1.5 1.5", "M19 5l-1.5 1.5", "M5 19l1.5 -1.5", "M19 19l-1.5 -1.5" },
        ["wx-partly"] = new[] { "M8 21h9l4 -4v-2l-2 -2h-2l-3 -3h-2l-3 3v1l-3 3v1z", "M4 10v-3l2 -2h3l2 2", "M7.5 1v1.5", "M1 7h1.5", "M2.5 2.5l1 1", "M12.5 2.5l-1 1" },
        ["wx-cloudy"] = new[] { Cloud.Replace("M6 14", "M6 17") },
        ["wx-overcast"] = new[] { Cloud, "M5 18h14", "M8 21h8" },
        ["wx-fog"] = new[] { "M6 6h12", "M3 11h18", "M6 16h12", "M9 21h6" },
        ["wx-light_rain"] = new[] { Cloud, "M9 17l-1 4", "M15 17l-1 4" },
        ["wx-rain"] = new[] { Cloud, "M7 17l-1 4", "M12 17l-1 4", "M17 17l-1 4" },
        ["wx-heavy_rain"] = new[] { Cloud, "M6 17l-1 4", "M10 17l-1 4", "M14 17l-1 4", "M18 17l-1 4" },
        ["wx-sleet"] = new[] { Cloud, "M8 17l-1 4", "M14.5 17.5l3 3", "M17.5 17.5l-3 3" },
        ["wx-light_snow"] = new[] { Cloud, "M12 17v4", "M10 19h4" },
        ["wx-snow"] = new[] { Cloud, "M8 17v4", "M6 19h4", "M16 17v4", "M14 19h4" },
        ["wx-heavy_snow"] = new[] { Cloud, "M6 17v4", "M4 19h4", "M12 17v4", "M10 19h4", "M18 17v4", "M16 19h4" },
        ["wx-thunderstorm"] = new[] { Cloud, "M13 13l-3 4h4l-3 5" },

        // drawn after the apps' own logos on the same grid: Tabler has none for these
        ["floe-bambu"] = new[] { "M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1 -3 3h-10a3 3 0 0 1 -3 -3v-10a3 3 0 0 1 3 -3z", "M12 4v16", "M4 14.5l8 -2.5", "M12 9.5l8 -2.5" },
        ["floe-orca"] = new[] { "M2 14.5c1.5 -3.5 5 -6 9.5 -6.2l2 -4.3l2 4.8c2.6 .8 4.4 2.4 5.5 4.2l1 -1.5v5.5l-3.5 -1.5c-1.8 2.5 -5.5 4 -9.5 3.5c-3.2 -.4 -5.8 -2 -7 -4.5z", "M4 16c3 -.4 5.5 .6 7.5 2.5", "M7 11.8a1.6 .9 -15 1 0 3.1 -.8a1.6 .9 -15 1 0 -3.1 .8" },
        ["floe-claude"] = new[] { "M14.0 12.3L21.7 13.4", "M13.6 13.2L18.8 17.3", "M12.7 13.9L15.6 20.9", "M11.7 14.0L10.9 20.1", "M10.8 13.6L6.0 19.7", "M10.1 12.7L3.8 15.3", "M10.0 11.7L2.7 10.7", "M10.4 10.8L5.4 6.8", "M11.3 10.1L8.3 2.9", "M12.3 10.0L13.2 3.5", "M13.2 10.4L17.9 4.4", "M13.9 11.3L19.8 8.9" },
        ["floe-opencode"] = new[] { "M5 3h14v18h-14z", "M9 8h6v9h-6z" },
        ["brand-chrome"] = new[] { "M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0", "M12 12m-3 0a3 3 0 1 0 6 0a3 3 0 1 0 -6 0", "M12 9h8.4", "M14.598 13.5l-4.2 7.275", "M9.402 13.5l-4.2 -7.275" },
        ["brand-steam"] = new[] { "M15.5 9.5a1 1 0 1 0 2 0a1 1 0 1 0 -2 0", "M16.5 5a4.5 4.5 0 1 1 -.653 8.953l-4.347 3.009l0 .038a3 3 0 0 1 -2.824 3l-.176 0a3 3 0 0 1 -2.94 -2.402l-2.56 -1.098v-3.5l3.51 1.755a2.989 2.989 0 0 1 2.834 -.635l2.727 -3.818a4.5 4.5 0 0 1 4.429 -5.302z" },
        ["brand-spotify"] = new[] { "M12 12m-9 0a9 9 0 1 0 18 0a9 9 0 1 0 -18 0", "M8 11.973c2.5 -1.473 5.5 -.973 7.5 .527", "M9 15c1.5 -1 4 -1 5 .5", "M7 9c2 -1 6 -2 10 .5" },
        ["brand-openai"] = new[] { "M11.217 19.384a3.501 3.501 0 0 0 6.783 -1.217v-5.167l-6 -3.35", "M5.214 15.014a3.501 3.501 0 0 0 4.446 5.266l4.34 -2.534v-6.946", "M6 7.63c-1.391 -.236 -2.787 .395 -3.534 1.689a3.474 3.474 0 0 0 1.271 4.745l4.263 2.514l6 -3.348", "M12.783 4.616a3.501 3.501 0 0 0 -6.783 1.217v5.067l6 3.45", "M18.786 8.986a3.501 3.501 0 0 0 -4.446 -5.266l-4.34 2.534v6.946", "M18 16.302c1.391 .236 2.787 -.395 3.534 -1.689a3.474 3.474 0 0 0 -1.271 -4.745l-4.308 -2.514l-5.955 3.42" },
        ["brand-edge"] = new[] { "M20.978 11.372a9 9 0 1 0 -1.593 5.773", "M20.978 11.372c.21 2.993 -5.034 2.413 -6.913 1.486c1.392 -1.6 .402 -4.038 -2.274 -3.851c-1.745 .122 -2.927 1.157 -2.784 3.202c.28 3.99 4.444 6.205 10.36 4.79", "M3.022 12.628c-.283 -4.043 8.717 -7.228 11.248 -2.688", "M12.628 20.978c-2.993 .21 -5.162 -4.725 -3.567 -9.748" },
        ["brand-firefox"] = new[] { "M4.028 7.82a9 9 0 1 0 12.823 -3.4c-1.636 -1.02 -3.064 -1.02 -4.851 -1.02h-1.647", "M4.914 9.485c-1.756 -1.569 -.805 -5.38 .109 -6.17c.086 .896 .585 1.208 1.111 1.685c.88 -.275 1.313 -.282 1.867 0c.82 -.91 1.694 -2.354 2.628 -2.093c-1.082 1.741 -.07 3.733 1.371 4.173c-.17 .975 -1.484 1.913 -2.76 2.686c-1.296 .938 -.722 1.85 0 2.234c.949 .506 3.611 -1 4.545 .354c-1.698 .102 -1.536 3.107 -3.983 2.727c2.523 .957 4.345 .462 5.458 -.34c1.965 -1.52 2.879 -3.542 2.879 -5.557c-.014 -1.398 .194 -2.695 -1.26 -4.75" },
        ["brand-windows"] = new[] { "M17.8 20l-12 -1.5c-1 -.1 -1.8 -.9 -1.8 -1.9v-9.2c0 -1 .8 -1.8 1.8 -1.9l12 -1.5c1.2 -.1 2.2 .8 2.2 1.9v12.1c0 1.2 -1.1 2.1 -2.2 1.9z", "M12 5l0 14", "M4 12l16 0" },
        ["brand-vscode"] = new[] { "M16 3v18l4 -2.5v-13z", "M9.165 13.903l-4.165 3.597l-2 -1l4.333 -4.5m1.735 -1.802l6.932 -7.198v5l-4.795 4.141", "M16 16.5l-11 -10l-2 1l13 13.5" },
        ["brand-discord"] = new[] { "M8 12a1 1 0 1 0 2 0a1 1 0 0 0 -2 0", "M14 12a1 1 0 1 0 2 0a1 1 0 0 0 -2 0", "M15.5 17c0 1 1.5 3 2 3c1.5 0 2.833 -1.667 3.5 -3c.667 -1.667 .5 -5.833 -1.5 -11.5c-1.457 -1.015 -3 -1.34 -4.5 -1.5l-.972 1.923a11.913 11.913 0 0 0 -4.053 0l-.975 -1.923c-1.5 .16 -3.043 .485 -4.5 1.5c-2 5.667 -2.167 9.833 -1.5 11.5c.667 1.333 2 3 3.5 3c.5 0 2 -2 2 -3", "M7 16.5c3.5 1 6.5 1 10 0" },
        ["brand-youtube"] = new[] { "M2 8a4 4 0 0 1 4 -4h12a4 4 0 0 1 4 4v8a4 4 0 0 1 -4 4h-12a4 4 0 0 1 -4 -4v-8z", "M10 9l5 3l-5 3z" },
        ["brand-whatsapp"] = new[] { "M3 21l1.65 -3.8a9 9 0 1 1 3.4 2.9l-5.05 .9", "M9 10a.5 .5 0 0 0 1 0v-1a.5 .5 0 0 0 -1 0v1a5 5 0 0 0 5 5h1a.5 .5 0 0 0 0 -1h-1a.5 .5 0 0 0 0 1" },
        ["brand-telegram"] = new[] { "M15 10l-4 4l6 6l4 -16l-18 7l4 2l2 6l3 -4" },
        ["brand-github"] = new[] { "M9 19c-4.3 1.4 -4.3 -2.5 -6 -3m12 5v-3.5c0 -1 .1 -1.4 -.5 -2c2.8 -.3 5.5 -1.4 5.5 -6a4.6 4.6 0 0 0 -1.3 -3.2a4.2 4.2 0 0 0 -.1 -3.2s-1.1 -.3 -3.5 1.3a12.3 12.3 0 0 0 -6.2 0c-2.4 -1.6 -3.5 -1.3 -3.5 -1.3a4.2 4.2 0 0 0 -.1 3.2a4.6 4.6 0 0 0 -1.3 3.2c0 4.6 2.7 5.7 5.5 6c-.6 .6 -.6 1.2 -.5 2v3.5" },
        ["brand-apple"] = new[] { "M8.286 7.008c-3.216 0 -4.286 3.23 -4.286 5.92c0 3.229 2.143 8.072 4.286 8.072c1.165 -.05 1.799 -.538 3.214 -.538c1.406 0 1.607 .538 3.214 .538s4.286 -3.229 4.286 -5.381c-.03 -.011 -2.649 -.434 -2.679 -3.23c-.02 -2.335 2.589 -3.179 2.679 -3.228c-1.096 -1.606 -3.162 -2.113 -3.75 -2.153c-1.535 -.12 -3.032 1.077 -3.75 1.077c-.729 0 -2.036 -1.077 -3.214 -1.077z", "M12 4a2 2 0 0 0 2 -2a2 2 0 0 0 -2 2" },
        ["brand-netflix"] = new[] { "M9 3l10 18h-4l-10 -18z", "M5 3v18h4v-10.5", "M19 21v-18h-4v10.5" },
        ["brand-adobe"] = new[] { "M12.893 4.514l7.977 14a.993 .993 0 0 1 -.394 1.365a1.04 1.04 0 0 1 -.5 .127h-3.476l-4.5 -8l-2.5 4h1.5l2 4h-8.977c-.565 0 -1.023 -.45 -1.023 -1c0 -.171 .045 -.34 .13 -.49l7.977 -13.993a1.034 1.034 0 0 1 1.786 0z" },
        ["sparkles"] = new[] { "M16 18a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2zm0 -12a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2zm-7 12a6 6 0 0 1 6 -6a6 6 0 0 1 -6 -6a6 6 0 0 1 -6 6a6 6 0 0 1 6 6z" },
        ["terminal-2"] = new[] { "M8 9l3 3l-3 3", "M13 15l3 0", "M3 4m0 2a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2z" },
        ["cube"] = new[] { "M21 16.008v-8.018a1.98 1.98 0 0 0 -1 -1.717l-7 -4.008a2.016 2.016 0 0 0 -2 0l-7 4.008c-.619 .355 -1 1.01 -1 1.718v8.018c0 .709 .381 1.363 1 1.717l7 4.008a2.016 2.016 0 0 0 2 0l7 -4.008c.619 -.355 1 -1.01 1 -1.718z", "M12 22v-10", "M12 12l8.73 -5.04", "M3.27 6.96l8.73 5.04" },
        ["box"] = new[] { "M12 3l8 4.5l0 9l-8 4.5l-8 -4.5l0 -9l8 -4.5", "M12 12l8 -4.5", "M12 12l0 9", "M12 12l-8 -4.5" },
        ["folder"] = new[] { "M5 4h4l3 3h7a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-11a2 2 0 0 1 2 -2" },
        ["photo"] = new[] { "M15 8h.01", "M3 6a3 3 0 0 1 3 -3h12a3 3 0 0 1 3 3v12a3 3 0 0 1 -3 3h-12a3 3 0 0 1 -3 -3v-12z", "M3 16l5 -5c.928 -.893 2.072 -.893 3 0l5 5", "M14 14l1 -1c.928 -.893 2.072 -.893 3 0l3 3" },
        ["music"] = new[] { "M3 17a3 3 0 1 0 6 0a3 3 0 0 0 -6 0", "M13 17a3 3 0 1 0 6 0a3 3 0 0 0 -6 0", "M9 17v-13h10v13", "M9 8h10" },
        ["movie"] = new[] { "M4 4m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v12a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z", "M8 4l0 16", "M16 4l0 16", "M4 8l4 0", "M4 16l4 0", "M4 12l16 0", "M16 8l4 0", "M16 16l4 0" },
        ["mail"] = new[] { "M3 7a2 2 0 0 1 2 -2h14a2 2 0 0 1 2 2v10a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-10z", "M3 7l9 6l9 -6" },
        ["calculator"] = new[] { "M4 3m0 2a2 2 0 0 1 2 -2h12a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-12a2 2 0 0 1 -2 -2z", "M8 7m0 1a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1v1a1 1 0 0 1 -1 1h-6a1 1 0 0 1 -1 -1z", "M8 14l0 .01", "M12 14l0 .01", "M16 14l0 .01", "M8 17l0 .01", "M12 17l0 .01", "M16 17l0 .01" },
        ["notes"] = new[] { "M5 3m0 2a2 2 0 0 1 2 -2h10a2 2 0 0 1 2 2v14a2 2 0 0 1 -2 2h-10a2 2 0 0 1 -2 -2z", "M9 7l6 0", "M9 11l6 0", "M9 15l4 0" },
        ["settings"] = new[] { "M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065z", "M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" },
        ["device-gamepad-2"] = new[] { "M12 5h3.5a5 5 0 0 1 0 10h-5.5l-4.015 4.227a2.3 2.3 0 0 1 -3.923 -2.035l1.634 -8.173a5 5 0 0 1 4.904 -4.019h3.4z", "M14 15l4.07 4.284a2.3 2.3 0 0 0 3.925 -2.023l-1.6 -8.232", "M8 9v2", "M7 10h2", "M14 10h2" },
        ["printer"] = new[] { "M17 17h2a2 2 0 0 0 2 -2v-4a2 2 0 0 0 -2 -2h-14a2 2 0 0 0 -2 2v4a2 2 0 0 0 2 2h2", "M17 9v-4a2 2 0 0 0 -2 -2h-6a2 2 0 0 0 -2 2v4", "M7 13m0 2a2 2 0 0 1 2 -2h6a2 2 0 0 1 2 2v4a2 2 0 0 1 -2 2h-6a2 2 0 0 1 -2 -2z" },
        ["brush"] = new[] { "M3 21v-4a4 4 0 1 1 4 4h-4", "M21 3a16 16 0 0 0 -12.8 10.2", "M21 3a16 16 0 0 1 -10.2 12.8", "M10.6 9a9 9 0 0 1 4.4 4.4" },
        ["camera"] = new[] { "M5 7h1a2 2 0 0 0 2 -2a1 1 0 0 1 1 -1h6a1 1 0 0 1 1 1a2 2 0 0 0 2 2h1a2 2 0 0 1 2 2v9a2 2 0 0 1 -2 2h-14a2 2 0 0 1 -2 -2v-9a2 2 0 0 1 2 -2", "M9 13a3 3 0 1 0 6 0a3 3 0 0 0 -6 0" },
        ["message-circle"] = new[] { "M3 20l1.3 -3.9c-2.324 -3.437 -1.426 -7.872 2.1 -10.374c3.526 -2.501 8.59 -2.296 11.845 .48c3.255 2.777 3.695 7.266 1.029 10.501c-2.666 3.235 -7.615 4.215 -11.574 2.293l-4.7 1" },
        ["world"] = new[] { "M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0", "M3.6 9h16.8", "M3.6 15h16.8", "M11.5 3a17 17 0 0 0 0 18", "M12.5 3a17 17 0 0 1 0 18" },
        ["video"] = new[] { "M15 10l4.553 -2.276a1 1 0 0 1 1.447 .894v6.764a1 1 0 0 1 -1.447 .894l-4.553 -2.276v-4z", "M3 6m0 2a2 2 0 0 1 2 -2h8a2 2 0 0 1 2 2v8a2 2 0 0 1 -2 2h-8a2 2 0 0 1 -2 -2z" },
        ["hexagon"] = new[] { "M19.875 6.27a2.225 2.225 0 0 1 1.125 1.948v7.284c0 .809 -.443 1.555 -1.158 1.948l-6.75 4.27a2.269 2.269 0 0 1 -2.184 0l-6.75 -4.27a2.225 2.225 0 0 1 -1.158 -1.948v-7.285c0 -.809 .443 -1.554 1.158 -1.947l6.75 -3.98a2.33 2.33 0 0 1 2.25 0l6.75 3.98h-.033z" },
        ["adjustments-horizontal"] = new[] { "M14 6m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0", "M4 6l8 0", "M16 6l4 0", "M8 12m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0", "M4 12l2 0", "M10 12l10 0", "M17 18m-2 0a2 2 0 1 0 4 0a2 2 0 1 0 -4 0", "M4 18l11 0", "M19 18l1 0" },
        ["chevron-up"] = new[] { "M6 15l6 -6l6 6" },
    };

    /// <summary>The icon for an app; the drawing follows <paramref name="brush"/> as it changes.</summary>
    public static ImageSource ForApp(string name, Brush brush)
    {
        var lower = name.ToLowerInvariant();
        foreach (var (match, icon) in Apps)
        {
            if (lower.Contains(match)) return Draw(icon, brush);
        }

        return Letter(name, brush);
    }

    /// <summary>The app's line icon, or nothing when none matches.</summary>
    public static ImageSource? Known(string name, Brush brush)
    {
        var lower = name.ToLowerInvariant();
        foreach (var (match, icon) in Apps)
        {
            if (lower.Contains(match)) return Draw(icon, brush);
        }

        return null;
    }

    /// <summary>One of the icons by its Tabler name.</summary>
    public static ImageSource Draw(string icon, Brush brush)
    {
        var group = Canvas(brush);
        foreach (var path in Paths[icon]) group.Children.Add(Line(Geometry.Parse(path), brush));
        return Finish(group);
    }

    /// <summary>The weather by the condition <see cref="WeatherService"/> gives.</summary>
    public static ImageSource Weather(string condition, Brush brush) => condition switch
    {
        "mostly_sunny" or "partly_cloudy" => Draw("wx-partly", brush),
        _ => Draw(Paths.ContainsKey($"wx-{condition}") ? $"wx-{condition}" : "wx-cloudy", brush),
    };

    private static ImageSource Letter(string name, Brush brush)
    {
        var group = Canvas(brush);
        group.Children.Add(Line(new RectangleGeometry(new Rect(3, 3, 18, 18)), brush));
        var initial = new FormattedText(name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?", System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, new Typeface("Consolas"), 13, brush, 1);
        group.Children.Add(new GeometryDrawing(brush, null, initial.BuildGeometry(new Point(12 - (initial.Width / 2), 12 - (initial.Height / 2)))));
        return Finish(group);
    }

    // an empty box first, so every icon keeps the same 24 unit frame whatever its own bounds
    private static DrawingGroup Canvas(Brush brush)
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, Box, Box))));
        return group;
    }

    private static GeometryDrawing Line(Geometry geometry, Brush brush) =>
        new(null, new Pen(brush, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, geometry);

    private static ImageSource Finish(DrawingGroup group) => new DrawingImage(group);
}
