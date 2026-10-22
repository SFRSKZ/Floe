using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Floe.App.Effects.Chromatic;

/// <summary>
/// Drives the chromatic faces a window shows. Only visible, active faces are ticked, and
/// the timer never runs once the window closes: the material is decorative, keep it cheap.
/// </summary>
internal sealed class ChromaticAnimator : IDisposable
{
    /// <summary>The reference material's speed; .7 keeps the reflections slow.</summary>
    private const double Speed = 0.7;

    private readonly List<Face> _faces = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private double _time;
    private double _previous;

    private sealed class Face
    {
        public required Border Surface { get; init; }
        public required ChromaticEffect Effect { get; init; }
        public required Func<bool> Active { get; init; }
        public double Hover;
        public double Target;
        public Size Size;
    }

    internal ChromaticAnimator()
    {
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Gives <paramref name="surface"/> a chromatic face; hover follows the owning control.</summary>
    internal void Add(Border surface, UIElement owner, Func<bool> active)
    {
        var effect = new ChromaticEffect();
        if (surface.RenderSize.Width > 0 && surface.RenderSize.Height > 0)
        {
            effect.Size = surface.RenderSize;
        }

        surface.Effect = effect;
        surface.SizeChanged += (_, e) => effect.Size = e.NewSize;
        var face = new Face { Surface = surface, Effect = effect, Active = active, Size = surface.RenderSize };
        _faces.Add(face);
        owner.MouseEnter += (_, _) => face.Target = 1;
        owner.MouseLeave += (_, _) => face.Target = 0;
    }

    internal void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _clock.Restart();
        _previous = 0;
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        _clock.Stop();
        _faces.Clear();
    }

    private void Tick()
    {
        var now = _clock.Elapsed.TotalSeconds;
        var delta = Math.Min(Math.Max(0, now - _previous), 0.1);
        _previous = now;
        _time += delta * Speed;
        var ease = 1 - Math.Exp(-delta * 14);

        foreach (var face in _faces)
        {
            if (!face.Surface.IsVisible || !face.Active())
            {
                if (face.Hover > 0.001)
                {
                    face.Hover = 0;
                    face.Effect.Hover = 0;
                }
                continue;
            }

            face.Hover += (face.Target - face.Hover) * ease;
            if (Math.Abs(face.Target - face.Hover) < 0.002)
            {
                face.Hover = face.Target;
            }

            var size = face.Surface.RenderSize;
            if (size.Width > 0 && size.Height > 0 && size != face.Size)
            {
                face.Size = size;
                face.Effect.Size = size;
            }

            face.Effect.Hover = face.Hover;
            face.Effect.Time = _time;
        }
    }
}
