using System;
using System.IO;
using System.Linq;
using Floe.App.Services;
using Vortice.Direct3D11;
using Vortice.Mathematics;

namespace Floe.App.Effects.Gpu;

public static partial class GpuOffscreen
{
    /// <summary>
    /// Handles <c>--render-gpu-move=From;To;Move;dir</c>: one look handing over to another through the live layer's
    /// transition pass, as PNG frames at 30 fps (a second of the old look, the 1.6 s move, half a second after).
    /// </summary>
    public static bool TryRenderMove(string[] args)
    {
        const string option = "--render-gpu-move=";
        var argument = args.FirstOrDefault(a => a.StartsWith(option, StringComparison.OrdinalIgnoreCase));
        if (argument is null) return false;

        var parts = argument[option.Length..].Split(';');
        string folder = Path.GetFullPath(parts.Length > 3 && parts[3].Length > 0 ? parts[3] : Path.Combine(Path.GetTempPath(), "floe-move"));
        Directory.CreateDirectory(folder);
        try
        {
            if (parts.Length < 3
                || !Enum.TryParse<VisualizerStyle>(parts[0].Trim(), true, out var from) || !GpuSceneCatalog.Has(from)
                || !Enum.TryParse<VisualizerStyle>(parts[1].Trim(), true, out var to) || !GpuSceneCatalog.Has(to)
                || !Enum.TryParse<SceneTransition>(parts[2].Trim(), true, out var move))
            {
                File.WriteAllText(Path.Combine(folder, "render-error.txt"), "usage: --render-gpu-move=FromLook;ToLook;ZoomIn|ZoomOut|SlideLeft|SlideRight|SlideUp|SlideDown;dir");
                Environment.ExitCode = 2;
                return true;
            }

            const int before = 30, during = 48, after = 15;
            using var scene = new MovePreview(GpuSceneCatalog.Create(from), GpuSceneCatalog.Create(to), move, before, during);
            RenderFrames(scene, 3840, 126, 1.5f, before + during + after, (frame, index) =>
            {
                MockFrame(frame, 6 + (index / 30f));
                frame.DockLeft = .37f;
                frame.DockRight = .63f;
                scene.Index = index;
            }, folder);
            File.WriteAllText(Path.Combine(folder, "render-info.txt"), $"{from} -> {to} by {move}: frames {before}..{before + during - 1} are the move\n");
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(folder, "render-error.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }

        return true;
    }

    // two scenes and the transition pass, drawn as one scene so RenderFrames can take it as it is
    private sealed class MovePreview : IGpuScene
    {
        private readonly IGpuScene _from, _to;
        private readonly SceneTransition _move;
        private readonly int _start, _length;
        private readonly SceneTransitionPass _pass = new();

        public MovePreview(IGpuScene from, IGpuScene to, SceneTransition move, int start, int length)
        {
            (_from, _to, _move, _start, _length) = (from, to, move, start, length);
        }

        public int Index { get; set; }

        public void Initialize(ID3D11Device device)
        {
            _from.Initialize(device);
            _to.Initialize(device);
            _pass.Initialize(device);
        }

        public void Resize(int width, int height)
        {
            _from.Resize(width, height);
            _to.Resize(width, height);
            _pass.Resize(width, height);
        }

        public void Render(ID3D11DeviceContext context, ID3D11RenderTargetView target, GpuFrame frame)
        {
            // both scenes keep drawing all along, as on the live layer during a move
            var clear = new Color4(0, 0, 0, 0);
            context.ClearRenderTargetView(_pass.OldTarget!, clear);
            context.ClearRenderTargetView(_pass.NewTarget!, clear);
            _from.Render(context, _pass.OldTarget!, frame);
            _to.Render(context, _pass.NewTarget!, frame);
            float progress = Math.Clamp((Index - _start + 1) / (float)_length, 0, 1);
            _pass.Apply(context, target, _move, progress);
        }

        public void ReleaseDevice()
        {
            _from.ReleaseDevice();
            _to.ReleaseDevice();
            _pass.ReleaseDevice();
        }

        public void Dispose()
        {
            ReleaseDevice();
            _from.Dispose();
            _to.Dispose();
        }
    }
}
