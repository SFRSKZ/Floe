using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Floe.App.Effects.MetalRim;

namespace Floe.App.Effects.Chromatic;

/// <summary>
/// Drives the chromatic faces and metal rims a window shows. Only visible, active surfaces
/// are ticked, and the timer never runs once the window closes: the material is decorative,
/// keep it cheap.
/// </summary>
internal sealed class ChromaticAnimator : IDisposable
{
    /// <summary>The reference material's speed; .7 keeps the reflections slow.</summary>
    private const double Speed = 0.7;

    /// <summary>Selected options and the close button get the full ring; the rest stay quiet.</summary>
    private const double RimStrength = 1.0;
    private const double RimGlow = 0.45;
    private const double RimQuietStrength = 0.4;
    private const double RimQuietGlow = 0.16;

    private readonly List<Face> _faces = new();
    private readonly List<Rim> _rims = new();
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

    private sealed class Rim
    {
        public required Border Surface { get; init; }
        public required MetalRimEffect Effect { get; init; }
        public required Func<bool> Strong { get; init; }
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

    /// <summary>
    /// Puts the chromatic metal rim on <paramref name="surface"/>: a transparent overlay the
    /// same size as the control, its glow bleeding into the effect's padding. The overlay must
    /// not be clipped by its host. The ring fades between the quiet and full strength as
    /// <paramref name="strong"/> changes.
    /// </summary>
    internal bool AddRim(Border surface, Func<bool> strong, double cornerRadius = 9)
    {
        var effect = MetalRimEffect.ApplyPreset("chromatic", true);
        if (!effect.IsAvailable)
        {
            return false;
        }

        effect.Thickness = 1.8;
        effect.CornerRadius = cornerRadius;
        var full = strong();
        effect.Strength = full ? RimStrength : RimQuietStrength;
        effect.GlowGain = full ? RimGlow : RimQuietGlow;
        if (surface.RenderSize.Width > 0 && surface.RenderSize.Height > 0)
        {
            effect.Size = surface.RenderSize;
        }

        surface.Effect = effect;
        surface.SizeChanged += (_, e) => effect.Size = e.NewSize;
        _rims.Add(new Rim { Surface = surface, Effect = effect, Strong = strong, Size = surface.RenderSize });
        return true;
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
        _rims.Clear();
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

        foreach (var rim in _rims)
        {
            if (!rim.Surface.IsVisible)
            {
                continue;
            }

            var size = rim.Surface.RenderSize;
            if (size.Width > 0 && size.Height > 0 && size != rim.Size)
            {
                rim.Size = size;
                rim.Effect.Size = size;
            }

            var strong = rim.Strong();
            var strength = strong ? RimStrength : RimQuietStrength;
            var glow = strong ? RimGlow : RimQuietGlow;
            rim.Effect.Strength += (strength - rim.Effect.Strength) * ease;
            rim.Effect.GlowGain += (glow - rim.Effect.GlowGain) * ease;
            if (Math.Abs(strength - rim.Effect.Strength) < 0.002)
            {
                rim.Effect.Strength = strength;
            }
            if (Math.Abs(glow - rim.Effect.GlowGain) < 0.002)
            {
                rim.Effect.GlowGain = glow;
            }

            rim.Effect.Time = _time;
        }
    }
}
