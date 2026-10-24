using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// The Tron bulge, made of cubes: under the hovered icon the flat glass grows a mound of whole cubes that never
/// sits still. Every column flows upward, a new cube rising out of the glass at its foot while the one on top breaks
/// off and scatters, fading as it flies. When the mound sinks, the cubes it loses scatter too.
/// The dock feeds it the columns once per frame; it keeps no clock of its own.
/// </summary>
public sealed class TronCubes : FrameworkElement
{
    /// <summary>Edge of one cube of the mound, in DIP: a column's width and a step's height.</summary>
    public const double Cube = 6;

    private const double Face = Cube - 1.5;
    private const int MaxParticles = 320;
    private const double FadeSeconds = .85;
    // cubes a second each column carries up and throws off its top
    private const double Flow = 2.2;
    private const double RiseMin = 25;
    private const double RiseMax = 85;
    private const double Spread = 70;
    private const double Gravity = 60;

    /// <summary>One column of the mound, in this element's coordinates: its foot (Top), and its height.</summary>
    public readonly record struct Column(double Left, double Top, double Width, double Height, double Fraction);

    private struct Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Age;
    }

    private readonly List<Column> _columns = new();
    private readonly List<double> _phases = new();
    private readonly List<double> _heights = new();
    private readonly List<Particle> _particles = new();
    private readonly Random _random = new(20261024);
    private Brush _face = Brushes.Black;
    private Pen _edge = new(Brushes.Black, 1);
    private Pen _heat = new(Brushes.Black, 1);
    private Color _line;
    private Color _hot;
    private bool _tinted;
    private bool _flowing;

    public TronCubes()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>True while the mound flows or a scattered cube is still in the air, so the dock keeps its frames coming.</summary>
    public bool Busy => _flowing || _particles.Count > 0;

    /// <summary>
    /// One frame: <paramref name="columns"/> stand on the glass's top edge (Top) and want Height / Cube plus
    /// Fraction cubes of mound; <paramref name="dt"/> drives the flow and the scatter.
    /// </summary>
    public void Frame(IReadOnlyList<Column> columns, double hoverX, double dt)
    {
        _columns.Clear();
        _flowing = false;
        if (!GlassSettings.Tron || Visibility != Visibility.Visible)
        {
            if (_particles.Count > 0 || _heights.Count > 0) Clear();
            return;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            _columns.Add(column);
            var cubes = (column.Height / Cube) + column.Fraction;
            if (i >= _phases.Count)
            {
                // columns start out of step, so the mound shimmers instead of pumping as one
                _phases.Add(_random.NextDouble());
                _heights.Add(cubes);
            }

            var middle = column.Left + (column.Width / 2);
            if (cubes >= 1)
            {
                _flowing = true;
                var phase = _phases[i] + (dt * Flow * (.8 + (.4 * Math.Abs(Math.Sin(i * 1.7)))));
                if (phase >= 1)
                {
                    // the top cube breaks off and flies, and the stack moves up a place
                    phase -= 1;
                    Scatter(new Point(middle, column.Top - (Math.Floor(cubes) * Cube)), hoverX);
                }

                _phases[i] = phase;
            }

            // a sinking mound throws off the cubes it no longer holds
            var lost = (int)Math.Floor(_heights[i]) - (int)Math.Floor(cubes);
            for (var k = 0; k < Math.Min(lost, 6); k++)
            {
                Scatter(new Point(middle + ((_random.NextDouble() - .5) * Cube), column.Top - ((Math.Floor(cubes) + k + .5) * Cube)), hoverX);
            }

            _heights[i] = cubes;
        }

        if (_phases.Count > columns.Count)
        {
            _phases.RemoveRange(columns.Count, _phases.Count - columns.Count);
            _heights.RemoveRange(columns.Count, _heights.Count - columns.Count);
        }

        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= FadeSeconds)
            {
                _particles.RemoveAt(i);
                continue;
            }

            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
            p.Vy += Gravity * dt;
            _particles[i] = p;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_columns.Count == 0 && _particles.Count == 0) return;
        EnsureBrushes();
        for (var i = 0; i < _columns.Count; i++)
        {
            var column = _columns[i];
            var cubes = (column.Height / Cube) + column.Fraction;
            if (cubes < 1) continue;
            var phase = i < _phases.Count ? _phases[i] : 0;
            var x = column.Left + ((column.Width - Face) / 2);
            var whole = (int)Math.Floor(cubes);

            // the stack rides up by its phase: the bottom cube is still coming out of the glass, the top one
            // is about to leave
            for (var k = 0; k < whole; k++)
            {
                var y = column.Top - ((k + phase + 1) * Cube) + ((Cube - Face) / 2);
                var opacity = k == 0 ? phase : 1;
                Draw(dc, new Rect(x, y, Face, Face), opacity);
            }
        }

        for (var i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            Draw(dc, new Rect(p.X, p.Y, Face, Face), 1 - (p.Age / FadeSeconds));
        }
    }

    // a cube breaks loose: out to the side away from the hover and up, then falling a little as it fades
    private void Scatter(Point at, double hoverX)
    {
        if (_particles.Count >= MaxParticles) _particles.RemoveAt(0);
        var away = at.X < hoverX ? -1 : at.X > hoverX ? 1 : _random.Next(2) == 0 ? -1 : 1;
        _particles.Add(new Particle
        {
            X = at.X - (Face / 2),
            Y = at.Y - (Face / 2),
            Vx = away * (.25 + (.75 * _random.NextDouble())) * Spread,
            Vy = -(RiseMin + (_random.NextDouble() * (RiseMax - RiseMin))),
        });
    }

    private void Draw(DrawingContext dc, Rect face, double opacity)
    {
        if (opacity <= .01) return;
        dc.PushOpacity(Math.Min(1, opacity));
        dc.DrawRectangle(_face, _edge, face);
        dc.DrawLine(_heat, new Point(face.Left, face.Top + .5), new Point(face.Right, face.Top + .5));
        dc.Pop();
    }

    private void EnsureBrushes()
    {
        var line = ThemePalette.LineColor;
        var hot = ThemePalette.HotColor;
        if (_tinted && _line == line && _hot == hot) return;
        _tinted = true;
        _line = line;
        _hot = hot;
        var face = new SolidColorBrush(Color.FromArgb(0xD9, 0, 0, 0));
        face.Freeze();
        _face = face;
        var edge = new SolidColorBrush(line);
        edge.Freeze();
        _edge = new Pen(edge, 1);
        _edge.Freeze();
        var heat = new SolidColorBrush(hot);
        heat.Freeze();
        _heat = new Pen(heat, 1) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        _heat.Freeze();
    }

    private void Clear()
    {
        _particles.Clear();
        _phases.Clear();
        _heights.Clear();
        _columns.Clear();
        InvalidateVisual();
    }
}
