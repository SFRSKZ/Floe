using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Floe.App.Effects.MetalRim;
using Floe.App.Services;
using Floe.Core.Models;

namespace Floe.App.Effects.ActiveIcon;

/// <summary>One active-icon design study: a film's look turned into an effect around the dock icon.</summary>
internal sealed record IconLabDesign(string Id, string Film, string Name, string Idea);

/// <summary>
/// The active-icon design lab: twenty studies, each its own small ps_3_0 shader over the icon and its
/// distance field (Lab/*.hlsl). Diagnostic preview only for now: <c>--rendericonlab=folder</c>.
/// </summary>
internal static class IconLab
{
    internal static readonly IconLabDesign[] Designs =
    {
        new("S01", "Blade Runner", "Yağmurda Neon", "İki renkli neon tüp, ışığı yakalayan eğik yağmur, ikonda mercek damlalar, ıslak zeminde yansıma"),
        new("S02", "Tron", "Işık Motoru", "İki ışık motoru ikonun çevresinde kovalaşıyor, arkalarında yükselen ışık duvarı kuyrukta piksele dağılıyor"),
        new("S03", "Interstellar", "Gargantua", "Eğik toplanma diski ikonun önünden geçiyor, arka yüzü bükülüp üstten taşıyor, Doppler parlaklığı"),
        new("S04", "Dune", "Holtzman Kalkanı", "İnce film yanardönerli kalkan kabuğu, darbe dalgaları, ara sıra kırmızı yavaş bıçak delişi"),
        new("S05", "2001: A Space Odyssey", "Yıldız Kapısı", "Slit-scan ışık koridoru, renk çizgileri ikondan dışarı, izleyiciye doğru akıyor"),
        new("S06", "Star Wars", "Hologram Mesajı", "Alttaki yayıcıdan ışık konisi, ikon tarama çizgili, titreşen, arada kayan mavi hologram"),
        new("S07", "Arrival", "Logogram", "Mürekkep halka yazılıyor, kaligrafik kalınlaşmalar ve dallar, sonra dumana dağılıp yeniden yazılıyor"),
        new("S08", "The Matrix", "Derin Kod Yağmuru", "Üç derinlik katmanında düşen glif sütunları, öndeki katman ikonun önünden geçiyor"),
        new("S09", "Iron Man", "JARVIS HUD", "3B eğilip salınan HUD halkaları, tik çizgileri, radar taraması, köşe nişan braketleri"),
        new("S10", "Prometheus", "Yıldız Haritası", "Üç eğik yörüngede gölgeli gezegenler, ikon yıldız gibi ışık veriyor, önden ve arkadan geçiyorlar"),
        new("F01", "Yüzüklerin Efendisi", "Tek Yüzük", "İkonun etrafında yuvarlanan 3B altın halka, ısınınca beliren ateş yazısı ve kıvılcımlar"),
        new("F02", "Doctor Strange", "Sihir Mandalası", "Kıvılcımdan çizgilerle dönen eğik mandala, rün kuşağı, teğet fırlayan kıvılcımlar"),
        new("F03", "Harry Potter", "Patronus", "İkonu saran üç gümüş ışık sarmalı, önden ve arkadan geçiyor, yukarı süzülen kıvılcımlar"),
        new("F04", "Karlar Ülkesi", "Buz Kristali", "Kenardan büyüyen fasetli buz dalları, kırağı tutan ikon kenarı, parlayan uçlar, sonra eriyor"),
        new("F05", "Avatar", "Ruh Ağacı Tohumları", "İkonun çevresinde süzülen biyolüminesan denizanası tohumlar, alt kenarda dalga gibi yanan yosun"),
        new("F06", "Ejderhanı Nasıl Eğitirsin", "Plazma Ateşi", "Keskin bantlı mor-mavi plazma alevleri, ikonu yalayan mavi kenar ışığı, yükselen közler"),
        new("F07", "Thor", "Bifrost", "İkonun altında dönen rünlü mühür diski, ikonu saran prizmatik ışık sütunu ve tayf kıymıkları"),
        new("F08", "Karayip Korsanları", "Lanetli Altın", "Eğik yörüngede takla atan kabartmalı altın sikkeler, arkaya geçince ay ışığında lanetli gümüşe dönüyor"),
        new("F09", "Malefiz", "Diken ve Yeşil Ateş", "Alttan iki yöne büyüyen örgülü dikenli sarmaşıklar, üzerlerinde keskin yeşil alev ve kıvılcım"),
        new("F10", "Peter Pan", "Peri Tozu", "3B yörüngede dolanan altın kuyruklu yıldız, yerçekimiyle dökülen pırıltılar, kenarda yanıp sönen toz"),
    };

    /// <summary>The study shader an aura effect runs on the active icon, or null for the older effects.</summary>
    internal static string? StudyFor(AuraEffect effect) => effect switch
    {
        AuraEffect.Rain => "S01",
        AuraEffect.LightCycle => "S02",
        AuraEffect.Gargantua => "S03",
        AuraEffect.Logogram => "S07",
        AuraEffect.CodeRain => "S08",
        AuraEffect.OneRing => "F01",
        _ => null,
    };

    internal static string Source(string id, bool d3d11)
    {
        var text = new StringBuilder();
        if (d3d11) text.Append("#define LAB_D3D11 1\n");
        text.Append(Read("Floe.IconLab.Lab.Head.hlsl")).Append('\n');
        text.Append(Read($"Floe.IconLab.{id}.hlsl")).Append('\n');
        text.Append(Read("Floe.IconLab.Lab.Foot.hlsl"));
        return text.ToString();
    }

    internal static byte[] Compile(string id, string target) =>
        ShaderCompiler.CompileBytes(Encoding.UTF8.GetBytes(Source(id, target != "ps_3_0")), target);

    private static string Read(string name)
    {
        using var stream = typeof(IconLab).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded {name} was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ---------------------------------------------------------------- preview

    private const double IconSize = 54;   // the dock's IconBox
    private const double Pitch = 62;      // icons sit 8 DIP apart

    /// <summary>
    /// <c>--rendericonlab=folder</c> [<c>--iconlab-only=S01,F03</c>] [<c>--iconlab-seconds=6</c>] [<c>--iconlab-fps=30</c>]:
    /// for every study a frame strip (dock-like row, 2x) and a 4x close-up sheet, plus a sheet of all of them.
    /// </summary>
    internal static bool TryRender(string[] args)
    {
        const string option = "--rendericonlab=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;
        var info = new StringBuilder();
        try
        {
            var folder = Path.GetFullPath(argument[option.Length..]);
            Directory.CreateDirectory(folder);
            string? Value(string name) => args.FirstOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase))?[name.Length..];
            var only = Value("--iconlab-only=")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            double seconds = double.Parse(Value("--iconlab-seconds=") ?? "6", CultureInfo.InvariantCulture);
            int fps = int.Parse(Value("--iconlab-fps=") ?? "30", CultureInfo.InvariantCulture);

            var icons = LoadIcons();
            var sheet = new List<(IconLabDesign Design, BitmapSource Close)>();
            int index = 0;
            foreach (var design in Designs)
            {
                var active = icons[index++ % icons.Count];
                if (only is not null && !only.Contains(design.Id, StringComparer.OrdinalIgnoreCase)) continue;
                try
                {
                    // the dock runs it as ps_3_0: it must fit there first
                    int slots = Compile(design.Id, "ps_3_0").Length;
                    var close = RenderDesign(design, active, icons, Path.Combine(folder, design.Id), seconds, fps);
                    sheet.Add((design, close));
                    info.AppendLine(FormattableString.Invariant($"{design.Id} ok (ps_3_0 {slots} bytes)"));
                }
                catch (Exception ex)
                {
                    info.AppendLine($"{design.Id} FAILED: {ex.Message}");
                    Environment.ExitCode = 1;
                }
            }
            if (sheet.Count > 0) SaveSheet(sheet, Path.Combine(folder, "sheet.png"));
            info.AppendLine("ps_3_0 compiled for the dock; frames run the same HLSL on D3D11 WARP (ps_4_0), real icons and fields, 2x strip / 4x close-up.");
            File.WriteAllText(Path.Combine(folder, "render-info.txt"), info.ToString());
        }
        catch (Exception ex)
        {
            ShaderCompiler.Log($"Icon lab preview failed: {ex}");
            Environment.ExitCode = 1;
        }
        return true;
    }

    private static List<ImageSource> LoadIcons()
    {
        var targets = new[]
        {
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files\Blender Foundation\Blender 4.5\blender.exe",
            @"C:\Program Files\Inkscape\bin\inkscape.exe",
            @"C:\Program Files\Notepad++\notepad++.exe",
            @"C:\Windows\explorer.exe",
            @"C:\Program Files\OrcaSlicer\orca-slicer.exe",
            @"C:\Program Files\7-Zip\7zFM.exe",
        };
        var list = new List<ImageSource>();
        foreach (var target in targets)
        {
            var path = target;
            if (!File.Exists(path) && path.Contains("Blender"))
            {
                var dir = Directory.Exists(@"C:\Program Files\Blender Foundation") ? Directory.GetDirectories(@"C:\Program Files\Blender Foundation").FirstOrDefault() : null;
                path = dir is null ? path : Path.Combine(dir, "blender.exe");
            }
            if (!File.Exists(path)) continue;
            var icon = IconService.GetIcon(new DockApp { Name = Path.GetFileNameWithoutExtension(path), Target = path }, (int)IconSize);
            if (icon is not null) list.Add(icon);
        }
        if (list.Count == 0) list.Add(IconService.GetFallbackIcon());
        return list;
    }

    private static BitmapSource Input(ImageSource icon, int scale)
    {
        var visual = new DrawingVisual();
        double pad = ActiveIconEffect.Pad;
        using (var dc = visual.RenderOpen()) dc.DrawImage(icon, Fit(icon, pad, pad));
        int side = (int)((IconSize + 2 * pad) * scale);
        var input = new RenderTargetBitmap(side, side, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
        input.Render(visual);
        input.Freeze();
        return input;
    }

    // the icon laid out the way the dock's Image (and IconField) shows it: uniform, centred
    private static Rect Fit(ImageSource icon, double x, double y)
    {
        var s = Math.Min(IconSize / Math.Max(1, icon.Width), IconSize / Math.Max(1, icon.Height));
        double w = icon.Width * s, h = icon.Height * s;
        return new Rect(x + (IconSize - w) / 2, y + (IconSize - h) / 2, w, h);
    }

    private static float[] Constants(double time) => new float[]
    {
        (float)time, 0, 0, 0,
        0, 0, 0, 0,
        1f, 0.2f, 0.7f, 1, 0.2f, 0.9f, 1f, 1,
        1, 0, 0, 0,
        (float)(IconSize + 2 * ActiveIconEffect.Pad), (float)IconSize, 0, 0,
    };

    private static BitmapSource RenderDesign(IconLabDesign design, ImageSource active, List<ImageSource> icons, string folder, double seconds, int fps)
    {
        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "*.png")) File.Delete(old);
        var field = (BitmapSource)IconField.For(active, IconSize).ImageSource;
        var pixel = Compile(design.Id, "ps_4_0");
        var left = icons[(Array.IndexOf(Designs, design) + 3) % icons.Count];
        var right = icons[(Array.IndexOf(Designs, design) + 5) % icons.Count];
        double box = IconSize + 2 * ActiveIconEffect.Pad;
        BitmapSource? first = null, last = null;
        using (var strip = new ClickGlassRenderer(Input(active, 2), field, pixel))
        {
            int frames = (int)Math.Round(seconds * fps);
            for (int frame = 0; frame < frames; frame++)
            {
                var surface = strip.Render(Constants(frame / (double)fps));
                if (frame == 0) first = surface;
                if (frame == frames / 2) last = surface;
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    DrawDock(dc, 224, 96);
                    dc.DrawImage(left, Fit(left, 50 - IconSize / 2, 48 - IconSize / 2));
                    dc.DrawImage(right, Fit(right, 174 - IconSize / 2, 48 - IconSize / 2));
                    dc.DrawImage(surface, new Rect(112 - box / 2, 48 - box / 2, box, box));
                }
                Save(visual, 448, 192, 2, Path.Combine(folder, FormattableString.Invariant($"f{frame:000}.png")));
            }
        }
        if (first is not null && last is not null && Same(first, last))
            throw new InvalidOperationException("frames do not move");

        // close-ups at 4x for the details: a frame run at half the strip's rate, and stills at t = 1, 2.5, 4, 5.5 s
        BitmapSource? hero = null;
        using (var close = new ClickGlassRenderer(Input(active, 4), field, pixel))
        {
            int frames = (int)Math.Round(seconds * fps / 2.0);
            for (int frame = 0; frame < frames; frame++)
            {
                var surface = close.Render(Constants(frame * 2.0 / fps));
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    DrawDock(dc, box, box);
                    dc.DrawImage(surface, new Rect(0, 0, box, box));
                }
                Save(visual, (int)(box * 4), (int)(box * 4), 4, Path.Combine(folder, FormattableString.Invariant($"c{frame:000}.png")));
            }
            int n = 0;
            foreach (var t in new[] { 1.0, 2.5, 4.0, 5.5 })
            {
                var surface = close.Render(Constants(t));
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    DrawDock(dc, box, box);
                    dc.DrawImage(surface, new Rect(0, 0, box, box));
                }
                var target = Save(visual, (int)(box * 4), (int)(box * 4), 4, Path.Combine(folder, FormattableString.Invariant($"close-{n++}.png")));
                if (t == 2.5) hero = target;
            }
        }
        return hero!;
    }

    private static void DrawDock(DrawingContext dc, double width, double height)
    {
        var glass = new LinearGradientBrush(Color.FromRgb(34, 37, 46), Color.FromRgb(16, 18, 24), 90);
        dc.DrawRectangle(glass, null, new Rect(0, 0, width, height));
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new Rect(0, 0, width, 0.5));
    }

    private static bool Same(BitmapSource a, BitmapSource b)
    {
        var x = new byte[a.PixelWidth * a.PixelHeight * 4];
        var y = new byte[x.Length];
        a.CopyPixels(x, a.PixelWidth * 4, 0);
        b.CopyPixels(y, b.PixelWidth * 4, 0);
        return x.AsSpan().SequenceEqual(y);
    }

    private static RenderTargetBitmap Save(Visual visual, int width, int height, int scale, string path)
    {
        var target = new RenderTargetBitmap(width, height, 96.0 * scale, 96.0 * scale, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var output = File.Create(path);
        encoder.Save(output);
        return target;
    }

    private static void SaveSheet(List<(IconLabDesign Design, BitmapSource Close)> items, string path)
    {
        const int tile = 312, label = 54, columns = 5;
        int rows = (items.Count + columns - 1) / columns;
        var visual = new DrawingVisual();
        var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 11, 14)), null, new Rect(0, 0, columns * tile, rows * (tile + label)));
            for (int i = 0; i < items.Count; i++)
            {
                double x = (i % columns) * tile, y = (i / columns) * (tile + label);
                dc.DrawImage(items[i].Close, new Rect(x, y, tile, tile));
                var title = new FormattedText($"{items[i].Design.Id}  {items[i].Design.Name}", CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, face, 17, Brushes.White, 1.0);
                var film = new FormattedText(items[i].Design.Film, CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, face, 13, new SolidColorBrush(Color.FromRgb(150, 156, 170)), 1.0);
                dc.DrawText(title, new Point(x + 10, y + tile + 6));
                dc.DrawText(film, new Point(x + 10, y + tile + 29));
            }
        }
        Save(visual, columns * tile, rows * (tile + label), 1, path);
    }
}
