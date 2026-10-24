using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Floe.App.Services;

namespace Floe.App.Windows;

/// <summary>
/// The loose cubes of a Tron bulge: the next cube of a column floats down onto its step while a cube the
/// step loses drifts up and fades. The dock feeds it the stepped columns once per frame; it keeps no clock.
/// </summary>
public sealed class TronCubes : FrameworkElement
{
    /// <summary>Edge of one cube of the stepped bulge, in DIP: the step height and the column width.</summary>
    public const double Cube = 7;

    private const double Face = Cube - 2;
    private const int MaxParticles = 80;
    private const double FadeSeconds = .6;
    private const double RiseMin = 40;
    private const double RiseMax = 90;
    private const double Drift = 30;

    /// <summary>One column of the stepped top edge, in this element's coordinates.</summary>
    public readonly record struct Column(double Left, double Top, double Width, double Height, double Fraction);

    private readonly struct Loose
    {
        public Loose(double x, double y, double opacity)
        {
            X = x;
            Y = y;
            Opacity = opacity;
        }

        public double X { get; }

        public double Y { get; }

        public double Opacity { get; }
    }

    private struct Particle
    {
        public double X;
        public double Y;
        public double Vx;
        public double Vy;
        public double Age;
    }

    private readonly List<Loose> _assembling = new();
    private readonly List<Particle> _particles = new();
    private readonly List<double> _heights = new();
    private readonly List<double> _lefts = new();
    private readonly Random _random = new(20261024);
    private Brush _face = Brushes.Black;
    private Pen _edge = new(Brushes.Black, 1);
    private Pen _heat = new(Brushes.Black, 1);
    private Color _line;
    private Color _hot;
    private bool _tinted;

    public TronCubes()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>True while a scattered cube is still in the air, so the dock keeps its frames coming.</summary>
    public bool Busy => _particles.Count > 0;

    /// <summary>Draws one frame of the stepped columns; <paramref name="dt"/> only drives the scatter.</summary>
    public void Frame(IReadOnlyList<Column> columns, double hoverX, double dt)
    {
        _assembling.Clear();
        if (!GlassSettings.Tron || Visibility != Visibility.Visible || columns.Count == 0)
        {
            if (_particles.Count > 0 || _heights.Count > 0) Clear();
            return;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var column = columns[i];
            var fraction = column.Fraction;

            // the next cube floats down onto the step, offset by what is still missing
            if (fraction > .02)
            {
                _assembling.Add(new Loose(
                    column.Left + (column.Width / 2) - (Face / 2),
                    column.Top - Face - ((1 - fraction) * Cube * 1.6),
                    fraction));
            }

            // a lost cube leaves from where its step used to be; a column that moved is a fresh one
            var last = i < _heights.Count ? _heights[i] : column.Height;
            var held = i < _lefts.Count && Math.Abs(_lefts[i] - column.Left) < Cube;
            if (held && last > column.Height + .01)
            {
                var middle = column.Left + (column.Width / 2);
                var lost = Math.Min(MaxParticles, (int)Math.Round((last - column.Height) / Cube));
                for (var k = 0; k < lost; k++) Scatter(new Point(middle, column.Top + (k * Cube) + (Cube / 2)), hoverX);
            }

            if (i < _heights.Count)
            {
                _heights[i] = column.Height;
                _lefts[i] = column.Left;
            }
            else
            {
                _heights.Add(column.Height);
                _lefts.Add(column.Left);
            }
        }

        if (_heights.Count > columns.Count)
        {
            _heights.RemoveRange(columns.Count, _heights.Count - columns.Count);
            _lefts.RemoveRange(columns.Count, _lefts.Count - columns.Count);
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
            _particles[i] = p;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_assembling.Count == 0 && _particles.Count == 0) return;
        EnsureBrushes();
        foreach (var cube in _assembling)
        {
            Draw(dc, new Rect(cube.X, cube.Y, Face, Face), cube.Opacity);
        }

        for (var i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            Draw(dc, new Rect(p.X, p.Y, Face, Face), 1 - (p.Age / FadeSeconds));
        }
    }

    private void Scatter(Point at, double hoverX)
    {
        if (_particles.Count >= MaxParticles) _particles.RemoveAt(0);
        // away from the hover, never straight up
        var away = at.X < hoverX ? -1 : at.X > hoverX ? 1 : _random.Next(2) == 0 ? -1 : 1;
        _particles.Add(new Particle
        {
            X = at.X - (Face / 2),
            Y = at.Y - (Face / 2),
            Vx = away * _random.NextDouble() * Drift,
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
        _heights.Clear();
        _lefts.Clear();
        InvalidateVisual();
    }
}
