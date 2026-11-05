using System.IO;
using System.Diagnostics;
using System.Numerics;
using System.Windows.Media.Imaging;
using Floe.App.Effects.Gpu;
using Floe.App.Services;
using SceneLab.Audio;
using SceneLab.Render;

namespace SceneLab;

/// <summary>
/// Floe SceneLab — the design harness for Floe's sound-driven strip visualizers.
/// <para>
/// It renders any <see cref="IGpuScene"/> offscreen at the strip's real geometry (3840x126 at 1.5 px/dip),
/// drives it from the mock song or a WAV file analysed by the dock's own spectrum code, and writes evidence:
/// frames at named times, the beat frame, a contact sheet, an mp4 and a report that measures how hard the
/// picture answers the sound.
/// </para>
/// </summary>
internal static class Program
{
    private const int DefaultWidth = 3840;
    private const int DefaultHeight = 126;
    private const float DefaultPixelsPerDip = 1.5f;

    [STAThread]
    private static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Help || args.Length == 0)
        {
            Options.PrintHelp();
            return options.Help ? 0 : 1;
        }

        if (options.List)
        {
            foreach (var (name, description) in SceneRegistry.All())
            {
                Console.WriteLine($"{name,-24} {description}");
            }

            return 0;
        }

        try
        {
            return Run(options);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[scenelab] {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }

    private static int Run(Options options)
    {
        var scene = SceneRegistry.Create(options.Scene, out string sceneDescription);
        ITrack track = options.Wav is not null
            ? new WavTrack(WavData.Read(options.Wav)) { Source = options.Wav }
            : new MockTrack { HoldFadeThroughSilence = options.HoldFade };

        string outDir = Path.GetFullPath(options.Out);
        Directory.CreateDirectory(outDir);
        var framesDir = Path.Combine(outDir, "frames");
        var shotsDir = Path.Combine(outDir, "shots");
        if (Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
        if (Directory.Exists(shotsDir)) Directory.Delete(shotsDir, true);
        if (options.Mp4) Directory.CreateDirectory(framesDir);
        Directory.CreateDirectory(shotsDir);

        float from = Math.Max(0, options.From);
        float to = options.To > from ? options.To : Math.Max(from + 1, track.Length);
        int fps = Math.Max(1, options.Fps);
        int frameCount = Math.Max(1, (int)MathF.Round((to - from) * fps));

        // pass one: the audio only, to find the hardest beat and the silence window long before any GPU work
        var shotTimes = new List<(string Label, float Time)>();
        foreach (var time in options.Shots)
        {
            if (time >= from && time <= to) shotTimes.Add(($"t{time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}", time));
        }

        if (options.Beat)
        {
            var scout = options.Wav is not null ? new WavTrack(WavData.Read(options.Wav)) : (ITrack)new MockTrack();
            float best = -1, bestTime = from;
            var probe = new GpuFrame { Dt = 1f / fps };
            for (int i = 0; i < frameCount; i++)
            {
                float t = from + (i / (float)fps);
                scout.Fill(probe, t);
                float strength = (probe.Audio.Kick * 1.2f) + (probe.Audio.Snare * 0.7f) + (probe.Audio.Bass * 0.4f);
                if (strength > best) { best = strength; bestTime = t; }
            }

            shotTimes.Add(("beat", bestTime));
        }

        shotTimes.Sort((a, b) => a.Time.CompareTo(b.Time));

        using var renderer = new SceneRenderer(options.Width, options.Height);
        var frame = new GpuFrame();
        scene.Initialize(renderer.Device);
        scene.Resize(options.Width, options.Height);

        var palette = new Vector3[GpuFrame.PaletteSize];
        byte[]? previous = null;
        var luminance = new List<double>(frameCount);
        var motion = new List<double>(frameCount);
        var midTone = new List<double>(frameCount);
        var shadowTone = new List<double>(frameCount);
        var times = new List<float>(frameCount);
        var shotFrames = new Dictionary<string, byte[]>();
        var shotAudio = new Dictionary<string, string>();
        var watch = Stopwatch.StartNew();

        for (int i = 0; i < frameCount; i++)
        {
            float t = from + (i / (float)fps);
            frame.Dt = 1f / fps;
            frame.Time = t;
            frame.Width = options.Width;
            frame.Height = options.Height;
            frame.PixelsPerDip = options.PixelsPerDip;
            frame.Performance = Floe.App.Services.Performance.Level;
            frame.Wave = false;
            frame.SourceX = 0.5f;
            // the strip's middle is the dock: 46% of the width while no media plays, 58% with the media panel open
            frame.DockLeft = 0.5f - 0.23f;
            frame.DockRight = 0.5f + 0.23f;
            frame.Fade = 1;

            FillPalette(palette, t, options.Accent);
            for (int p = 0; p < GpuFrame.PaletteSize; p++) frame.Palette[p] = palette[p];

            track.Fill(frame, t);
            if (options.DumpAudio && i % 10 == 0) Console.WriteLine($"  audio t={t:0.00} {AudioLine(frame.Audio)}");

            var pixels = renderer.Draw(scene, frame);
            Frames.Composite(pixels);
            var (luma, change) = Frames.Measure(pixels, previous);
            var (shadow, mid, highlight) = Frames.Tone(pixels);
            _ = highlight;
            luminance.Add(luma);
            motion.Add(change);
            midTone.Add(mid);
            shadowTone.Add(shadow);
            times.Add(t);
            previous = pixels;

            foreach (var (label, time) in shotTimes)
            {
                if (!shotFrames.ContainsKey(label) && t >= time)
                {
                    shotFrames[label] = pixels;
                    // the analysis the scene actually drew with, not a re-run after the render
                    shotAudio[label] = AudioLine(frame.Audio);
                }
            }

            if (options.Mp4)
            {
                Frames.SavePng(pixels, options.Width, options.Height, Path.Combine(framesDir, $"f{i:D5}.png"));
            }
        }

        watch.Stop();
        scene.ReleaseDevice();
        scene.Dispose();

        // the shots on disk, plus a contact sheet of them
        var sheet = new List<(string, BitmapSource)>();
        var dockSheet = new List<(string, BitmapSource)>();
        foreach (var (label, time) in shotTimes)
        {
            if (!shotFrames.TryGetValue(label, out var pixels)) continue;
            Frames.SavePng(pixels, options.Width, options.Height, Path.Combine(shotsDir, $"shot-{label}.png"));
            string described = $"{options.Scene}  {label}  ({(shotAudio.TryGetValue(label, out var audio) ? audio : track.Describe(time))})";
            sheet.Add((described, Frames.ToBitmap(pixels, options.Width, options.Height)));
            if (options.DockPreview)
            {
                var withDock = (byte[])pixels.Clone();
                DockPreview.Draw(withDock, options.Width, options.Height, 0.27f, 0.73f, filled: true);
                dockSheet.Add(($"with the dock over the middle 46%  |  {described}", Frames.ToBitmap(withDock, options.Width, options.Height)));
            }
        }

        if (options.Sheet && sheet.Count > 0)
        {
            Frames.SaveContactSheet(sheet, Path.Combine(outDir, "sheet.png"));
            if (dockSheet.Count > 0) Frames.SaveContactSheet(dockSheet, Path.Combine(outDir, "sheet-with-dock.png"));
        }

        string mp4 = "";
        if (options.Mp4)
        {
            string output = Path.Combine(outDir, $"{options.Scene}.mp4");
            bool ok = Frames.EncodeMp4(framesDir, fps, output, out string log);
            if (ok) mp4 = output;
            else File.WriteAllText(Path.Combine(outDir, "ffmpeg-log.txt"), log);
            if (!options.KeepFrames && Directory.Exists(framesDir)) Directory.Delete(framesDir, true);
        }

        // the report: what was drawn, what the sound did, and how hard the picture answers it
        var report = new System.Text.StringBuilder();
        report.AppendLine($"scene        {options.Scene}  ({sceneDescription})");
        report.AppendLine($"track        {(options.Wav is not null ? options.Wav : track.Name)}  length {track.Length:0.0}s");
        report.AppendLine($"geometry     {options.Width}x{options.Height} at {options.PixelsPerDip} px/dip; dock gap {frame.DockLeft:0.00}..{frame.DockRight:0.00}"
            + " (46% of width, 58% with media open)");
        report.AppendLine($"device       {renderer.Info}");
        report.AppendLine($"frames       {frameCount} at {fps} fps from t={from:0.00}s to t={to:0.00}s");
        report.AppendLine($"render       {watch.Elapsed.TotalSeconds:0.00}s total, {watch.Elapsed.TotalMilliseconds / frameCount:0.0} ms/frame");
        report.AppendLine();
        report.AppendLine("shots:");
        foreach (var (label, time) in shotTimes)
        {
            report.AppendLine($"  {label,-8} t={time,6:0.00}  {(shotAudio.TryGetValue(label, out var audio) ? audio : track.Describe(time))}");
        }

        // motion under the drop against motion in the silence: a still picture reads as a static illustration
        double dropFrom = Math.Min(8.0, from + ((to - from) * 0.5));
        double dropTo = Math.Min(12.0, to);
        double silenceFrom = Math.Max(Math.Max(track.SilenceFrom, to - 0.8), dropTo + 0.1);
        double silenceTo = Math.Max(silenceFrom, to - 0.02);
        double DropMotion() => Window(dropFrom, dropTo, 0.5);
        double SilenceMotion() => Window(silenceFrom, silenceTo, 0.4);
        double Window(double a, double b, double guard)
        {
            double sum = 0;
            int count = 0;
            for (int i = 0; i < times.Count; i++)
            {
                if (times[i] < a || times[i] > b) continue;
                sum += motion[i];
                count++;
            }

            return count == 0 ? 0 : sum / count;
        }

        double drop = DropMotion(), silence = SilenceMotion();
        report.AppendLine();
        report.AppendLine($"motion       drop {drop:0.0000}  silence {silence:0.0000}  ratio {(silence > 1e-6 ? drop / silence : 0):0.0}x");
        report.AppendLine($"luma         min {luminance.Min():0.000}  max {luminance.Max():0.000}  mean {luminance.Average():0.000}");

        // the professional bar: a graded frame keeps its mid tones and does not read as flat pixel art
        double midShare = midTone.Average(), shadowShare = shadowTone.Average();
        string verdict = midShare < 0.20 ? "FLAT pixel-art tone: raise the mid tones (grade, fog, soft light)"
            : midShare < 0.35 ? "acceptable but dark: more graded mid tones would read as film"
            : "cinematic tone spread";
        report.AppendLine($"tone         shadow {shadowShare:0.00}  mid {midShare:0.00}  -> {verdict}");
        report.AppendLine($"silence hold the last {Math.Min(0.4, to - from):0.00}s moves {silence:0.0000} of a frame (should be near 0)");
        if (mp4.Length > 0) report.AppendLine($"mp4          {mp4}");
        File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
        Console.Write(report.ToString());
        return 0;
    }

    // The analyser values the scene actually drew with, so a shot's label cannot disagree with its pixels.
    private static string AudioLine(Floe.Core.Interop.SpectrumFrame a) =>
        $"level {a.Level:0.00} bass {a.Bass:0.00} mid {a.Mid:0.00} treble {a.Treble:0.00} "
        + $"kick {a.Kick:0.00} snare {a.Snare:0.00} hat {a.Hat:0.00}";

    // The dock's palette: the user's accent rotated out to the ends of the strip. The lab uses the Cyberpunk
    // dock's own accent by default so the look is judged in the colours it will ship in.
    private static void FillPalette(Vector3[] palette, float time, string accent)
    {
        var (r, g, b) = Hex(accent);
        for (int i = 0; i < palette.Length; i++)
        {
            double t = i / (double)(palette.Length - 1);
            var (hueR, hueG, hueB) = RotateHue(r, g, b, 50 * t);
            palette[i] = new Vector3((float)Math.Clamp(hueR, 0, 1), (float)Math.Clamp(hueG, 0, 1), (float)Math.Clamp(hueB, 0, 1));
        }
    }

    private static (double R, double G, double B) Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return (Convert.ToInt32(hex[..2], 16) / 255.0, Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0, Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0);
    }

    private static (double R, double G, double B) RotateHue(double r, double g, double b, double degrees)
    {
        double a = degrees * Math.PI / 180, c = Math.Cos(a), s = Math.Sin(a), k = 1 / 3.0, q = Math.Sqrt(k);
        double m0 = c + ((1 - c) * k), m1 = (k * (1 - c)) - (q * s), m2 = (k * (1 - c)) + (q * s);
        return ((r * m0) + (g * m1) + (b * m2), (r * m2) + (g * m0) + (b * m1), (r * m1) + (g * m2) + (b * m0));
    }
}
