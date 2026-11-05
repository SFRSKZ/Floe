using System.Globalization;
using Floe.App.Effects.Gpu;
using Floe.App.Services;
using System.Reflection;

namespace SceneLab;

/// <summary>Command line for the lab; see <see cref="PrintHelp"/>.</summary>
internal sealed class Options
{
    public string Scene { get; private set; } = "CyberSignal";
    public string Out { get; private set; } = "out";
    public float From { get; private set; }
    public float To { get; private set; } = -1;
    public int Fps { get; private set; } = 30;
    public int Width { get; private set; } = 3840;
    public int Height { get; private set; } = 126;
    public float PixelsPerDip { get; private set; } = 1.5f;
    public string? Wav { get; private set; }
    public bool Mp4 { get; private set; }
    public bool Sheet { get; private set; } = true;
    public bool Beat { get; private set; } = true;
    public bool KeepFrames { get; private set; }
    public bool DockPreview { get; private set; } = true;
    public bool HoldFade { get; private set; } = true;
    public bool List { get; private set; }
    public bool DumpAudio { get; private set; }
    public bool Help { get; private set; }
    public string Accent { get; private set; } = "FCEE0A";
    public List<float> Shots { get; } = new() { 2f, 6f, 10f, 15.5f };

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{arg} needs a value");
            double Number() => double.Parse(Next(), CultureInfo.InvariantCulture);

            switch (arg.ToLowerInvariant())
            {
                case "--scene": options.Scene = Next(); break;
                case "--out": options.Out = Next(); break;
                case "--from": options.From = (float)Number(); break;
                case "--to": options.To = (float)Number(); break;
                case "--fps": options.Fps = (int)Number(); break;
                case "--width": options.Width = (int)Number(); break;
                case "--height": options.Height = (int)Number(); break;
                case "--ppd": options.PixelsPerDip = (float)Number(); break;
                case "--accent": options.Accent = Next(); break;
                case "--wav": options.Wav = Next(); break;
                case "--shots": options.Shots.Clear(); foreach (var part in Next().Split(',')) options.Shots.Add(float.Parse(part, CultureInfo.InvariantCulture)); break;
                case "--mp4": options.Mp4 = true; break;
                case "--keep-frames": options.KeepFrames = true; options.Mp4 = true; break;
                case "--sheet": options.Sheet = true; break;
                case "--no-sheet": options.Sheet = false; break;
                case "--no-dock": options.DockPreview = false; break;
                case "--beat": options.Beat = true; break;
                case "--no-beat": options.Beat = false; break;
                case "--fade-real": options.HoldFade = false; break;
                case "--list": options.List = true; break;
                case "--dump-audio": options.DumpAudio = true; break;
                case "--help": case "-h": case "/?": options.Help = true; break;
                default: throw new ArgumentException($"unknown option: {arg}");
            }
        }

        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("""
            Floe SceneLab — renders a visualizer scene offscreen at the dock strip's real geometry.

              SceneLab --list
              SceneLab --scene <name> --out <dir> [options]

            options
              --from <s>        start of the timeline          (default 0)
              --to <s>          end of the timeline            (default the track's length)
              --fps <n>         frames per second              (default 30)
              --width / --height / --ppd   strip geometry      (default 3840x126 at 1.5 px/dip)
              --wav <file>      drive from real music instead of the mock track
              --accent <RRGGBB> strip accent                   (default the Cyberpunk dock's yellow)
              --shots t1,t2,..  evidence frames                (default 2,6,10,15.5)
              --beat / --no-beat  also save the hardest beat frame (default on)
              --mp4             encode the timeline to mp4 (needs ffmpeg)
              --keep-frames     keep the PNG sequence next to the mp4
              --fade-real       let the mock track fade the strip out over its silence
              --no-sheet        skip the contact sheet
              --no-dock         skip the "with the dock over the middle" sheet
              --list            list the scenes this build can draw

            output
              <dir>/shots/*.png   the named evidence frames
              <dir>/sheet.png     all shots, labelled, stacked
              <dir>/<scene>.mp4   the timeline (with --mp4)
              <dir>/report.txt    geometry, audio at each shot, motion in the drop against the silence
            """);
    }
}
