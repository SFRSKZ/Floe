using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Floe.App.Effects.Cyber;
using Floe.App.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // The icon is a wall of cubes: the click punches into it, the cubes near the hit are knocked loose and
    // tumble out, the ones at the rim only give a little, and then they all fly back and lock into the icon.
    // Each cube carries its own piece of the icon on its face; edges are hard, nothing is blurred.

    private const int CubeGrid = 6;
    // the physics runs in .95 s of its own time, played out over 1.8 s: the burst reads like the clip's slow motion
    private const double CubeSeconds = .95;
    private const double CubePlay = 1.8;

    private CyberFxLayer.Run? _cubeRun;
    private UIElement? _cubeHidden;

    private void PlayClickCubes(IconSlot slot)
    {
        StopClickCubes();
        if (slot.Button.Content is not UIElement content || slot.Item.Icon is not { } source) return;
        FrameworkElement face = content is Grid { Children.Count: > 0 } grid && grid.Children[0] is Image image ? image : (FrameworkElement)content;
        if (face.ActualWidth <= 0 || !Root.IsAncestorOf(face)) return;

        // the fx layer is turned with the dock; the icon is turned back upright, so the cubes take its picture as shown
        if (!_upright.Matrix.IsIdentity && source is BitmapSource bitmap)
        {
            var m = _upright.Matrix;
            source = new TransformedBitmap(bitmap, new MatrixTransform(m.M11, m.M12, m.M21, m.M22, 0, 0));
        }

        var burst = CubeBurst.Create(source, face.ActualWidth, VisualTreeHelper.GetDpi(this).DpiScaleX, GlassSettings.Cyber);
        if (burst is null) return;

        content.Opacity = 0;
        _cubeHidden = content;
        _cubeRun = _fx.Play(CubePlay / AuraSettings.ClickSpeed, (dc, p) =>
        {
            if (!Root.IsAncestorOf(face)) return;
            burst.Draw(dc, face.TransformToAncestor(Root).TransformBounds(new Rect(face.RenderSize)), p);
        }, done: StopClickCubes);
    }

    private void StopClickCubes()
    {
        if (_cubeRun is { } run)
        {
            _cubeRun = null;
            _fx.Cancel(run);
        }

        if (_cubeHidden is not null) _cubeHidden.Opacity = 1;
        _cubeHidden = null;
    }

    private sealed class CubeBurst
    {
        private const double Gravity = 1100;
        // share of the run the cubes are loose before they head home, and when the last one has locked in
        private const double Out = .46, Home = .9;

        private static readonly Point3D[] Corners =
        {
            new(-.5, -.5, -.5), new(.5, -.5, -.5), new(.5, .5, -.5), new(-.5, .5, -.5),
            new(-.5, -.5, .5), new(.5, -.5, .5), new(.5, .5, .5), new(-.5, .5, .5),
        };

        // front, back, right, left, bottom, top; corners run so the first three span the face's own square
        private static readonly int[][] Faces =
        {
            new[] { 4, 5, 6, 7 }, new[] { 5, 4, 7, 6 }, new[] { 6, 5, 1, 2 },
            new[] { 0, 4, 7, 3 }, new[] { 7, 6, 2, 3 }, new[] { 0, 1, 5, 4 },
        };

        private static readonly Vector3D[] Normals =
        {
            new(0, 0, 1), new(0, 0, -1), new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0),
        };

        private static readonly Pen Edge = Frozen(new Pen(Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0x06, 0x07, 0x0C))), .8));
        private static readonly Brush Ink = Frozen(new SolidColorBrush(Color.FromRgb(0x08, 0x09, 0x0E)));
        private static readonly Brush[] Dark = Enumerable.Range(0, 9).Select(i => Frozen(new SolidColorBrush(Color.FromArgb((byte)(i * 22), 0, 0, 0)))).ToArray();

        private readonly int _count;
        private readonly BitmapSource?[] _piece;
        private readonly Brush?[] _top, _side;
        private readonly Brush _hole;
        private readonly double[] _delay, _back, _spin, _size, _force;
        private readonly Vector[] _home, _velocity;
        private readonly double[] _rise;
        private readonly Vector3D[] _axis;
        private readonly Point[] _p = new Point[8];
        private readonly Point[] _q = new Point[4];
        private readonly List<(double Depth, int Index, Matrix3D Turn, Point Center, double Size)> _order = new();

        private CubeBurst(int count, Brush hole)
        {
            _count = count;
            _piece = new BitmapSource?[count];
            _top = new Brush?[count];
            _side = new Brush?[count];
            _hole = hole;
            _delay = new double[count];
            _back = new double[count];
            _spin = new double[count];
            _size = new double[count];
            _force = new double[count];
            _home = new Vector[count];
            _velocity = new Vector[count];
            _rise = new double[count];
            _axis = new Vector3D[count];
        }

        /// <summary>The icon cut into cubes, ready to burst; null when there is nothing to cut.</summary>
        public static CubeBurst? Create(ImageSource icon, double size, double dpi, bool cyber)
        {
            int cellPx = Math.Max(2, (int)Math.Round(size * dpi / CubeGrid));
            int px = cellPx * CubeGrid;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(icon, new Rect(0, 0, px, px));
            var sheet = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
            sheet.Render(visual);
            sheet.Freeze();
            var pixels = new byte[px * px * 4];
            sheet.CopyPixels(pixels, px * 4, 0);

            // a cyber hole glows red inside, like the wall in the clip; the glass dock's goes dark
            var burst = new CubeBurst(CubeGrid * CubeGrid, Frozen(new SolidColorBrush(cyber ? Color.FromRgb(0x8C, 0x06, 0x1E) : Color.FromRgb(0x14, 0x16, 0x1E))));
            var seed = Random.Shared.Next(1000);
            double R(int i, int k) => CyberFxLayer.Random((i * 31.7) + (k * 7.3) + seed);
            // the hit lands near the middle, never twice in the same spot
            var hit = new Vector(.5 + ((R(0, 90) - .5) * .25), .5 + ((R(0, 91) - .5) * .25));
            bool any = false;
            for (int i = 0; i < burst._count; i++)
            {
                int c = i % CubeGrid, r = i / CubeGrid;
                double a = 0, rs = 0, gs = 0, bs = 0;
                for (int y = r * cellPx; y < (r + 1) * cellPx; y++)
                {
                    for (int x = c * cellPx; x < (c + 1) * cellPx; x++)
                    {
                        int o = ((y * px) + x) * 4;
                        bs += pixels[o];
                        gs += pixels[o + 1];
                        rs += pixels[o + 2];
                        a += pixels[o + 3];
                    }
                }

                int n = cellPx * cellPx;
                double alpha = a / n / 255;
                if (alpha < .3) continue; // the icon's empty corners stay empty
                any = true;
                // premultiplied sums over the cell's coverage give the cell's own colour
                double k = 1 / Math.Max(1, a / 255);
                var colour = Color.FromRgb((byte)Math.Min(255, rs * k), (byte)Math.Min(255, gs * k), (byte)Math.Min(255, bs * k));
                burst._piece[i] = Frozen(new CroppedBitmap(sheet, new Int32Rect(c * cellPx, r * cellPx, cellPx, cellPx)));
                burst._top[i] = Frozen(new SolidColorBrush(Scale(colour, 1.25, alpha)));
                burst._side[i] = Frozen(new SolidColorBrush(Scale(colour, .55, alpha)));

                var home = new Vector((c + .5) / CubeGrid, (r + .5) / CubeGrid);
                burst._home[i] = home;
                var away = home - hit;
                double d = Math.Min(1, away.Length / .46);
                if (away.Length < 1e-3) away = new Vector(R(i, 1) - .5, -.5);
                away.Normalize();
                // close to the hit the cubes are thrown hard; at the rim they stay in the wall and only give a little
                double force = Math.Pow(1 - d, 1.5);
                burst._force[i] = force;
                burst._delay[i] = (.025 + (d * .09)) * (1 + ((R(i, 2) - .5) * .4));
                burst._velocity[i] = new Vector(away.X * (10 + (280 * force)), (away.Y * 140 * force) - (300 * force * (.5 + R(i, 3))));
                burst._rise[i] = (8 * (1 - force)) + (150 * force * R(i, 4));
                burst._spin[i] = (.6 + (16 * force)) * (R(i, 5) < .5 ? -1 : 1);
                var axis = new Vector3D(R(i, 6) - .5, R(i, 7) - .5, (R(i, 8) - .5) * .6);
                if (axis.Length < .05) axis = new Vector3D(1, 1, 0);
                axis.Normalize();
                burst._axis[i] = axis;
                burst._size[i] = .96;
                // the rim goes home first, the cubes from the hit lock in last
                burst._back[i] = Out + ((1 - d) * .12) + (R(i, 9) * .05);
            }

            return any ? burst : null;
        }

        public void Draw(DrawingContext dc, Rect box, double p)
        {
            double cell = box.Width / CubeGrid;
            double t = p * CubeSeconds;
            _order.Clear();
            for (int i = 0; i < _count; i++)
            {
                if (_piece[i] is null) continue;
                var home = new Point(box.X + (_home[i].X * box.Width), box.Y + (_home[i].Y * box.Height));

                // loose: thrown, falling, tumbling and coming at the viewer
                double u = Math.Max(0, t - (_delay[i] * CubeSeconds));
                double held = Math.Max(0, Math.Min(t, _back[i] * CubeSeconds) - (_delay[i] * CubeSeconds));
                var v = _velocity[i];
                // a cube still held by the wall moves only a hair; gravity pulls the ones knocked loose
                double grip = Math.Min(held, .12 + (_force[i] * 2));
                var flown = new Vector(v.X * grip, (v.Y * grip) + (.5 * Gravity * _force[i] * held * held));
                double z = _rise[i] * Math.Sin(Math.Min(1, held / .35) * Math.PI / 2);
                double angle = (_spin[i] * Math.Min(held, .05 + (_force[i] * 2))) + (u > 0 ? .5 * Math.Sign(_spin[i]) * Math.Min(1, u / .06) : 0);

                // home again: each cube flies back along an arc and snaps flat into the icon
                double k = Math.Clamp((p - _back[i]) / (Home - _back[i]), 0, 1);
                double e = k < 1 ? 1 - Math.Pow(1 - k, 3) : 1;
                double stay = 1 - e;
                var at = home + (flown * stay) + new Vector(0, -Math.Sin(e * Math.PI) * cell * .8 * Math.Min(1, flown.Length / cell / 3));
                z *= stay;
                angle *= stay;

                // a small dip as the cube is hit, before it lets go
                double dip = u <= 0 ? 0 : Math.Max(0, 1 - (u / .05)) * .18 * stay;
                double scale = (1 + (z / 260)) * (1 - dip);
                // loose cubes show a seam around them; home again they close it, so the icon ends whole
                double seam = 1 - ((1 - _size[i]) * stay * Math.Min(1, u / .05));
                var turn = Matrix3D.Identity;
                if (Math.Abs(angle) > 1e-4) turn.Rotate(new Quaternion(_axis[i], angle * 180 / Math.PI));
                _order.Add((z, i, turn, at, cell * seam * scale));

                // the hole the cube leaves, darker the deeper it is pushed in
                double gone = (at - home).Length + (z * .2);
                if (gone > cell * (_force[i] > .15 ? .2 : .6))
                {
                    var hole = new Rect(home.X - (cell / 2), home.Y - (cell / 2), cell, cell);
                    dc.DrawRectangle(Ink, null, hole);
                    hole.Inflate(-cell * .18, -cell * .18);
                    dc.PushOpacity(Math.Min(1, gone / cell));
                    dc.DrawRectangle(_hole, null, hole);
                    dc.Pop();
                }
            }

            // farther cubes first, so the ones flying at the viewer cover the rest
            _order.Sort((a, b) => a.Depth.CompareTo(b.Depth));
            foreach (var (_, i, turn, center, size) in _order) Cube(dc, i, turn, center, size);
        }

        private void Cube(DrawingContext dc, int i, Matrix3D turn, Point center, double size)
        {
            for (int c = 0; c < 8; c++)
            {
                var q = turn.Transform(Corners[c]);
                _p[c] = new Point(center.X + (q.X * size), center.Y + (q.Y * size));
            }

            bool flat = turn.IsIdentity;
            for (int f = 0; f < Faces.Length; f++)
            {
                var n = turn.Transform(Normals[f]);
                // a face seen edge-on would only leave a stray line
                if (n.Z <= .08) continue;
                for (int c = 0; c < 4; c++) _q[c] = _p[Faces[f][c]];
                var outline = Quad(_q);
                if (f == 0)
                {
                    // the face toward the viewer is the icon's own piece, carried along as the cube turns
                    var x = _q[1] - _q[0];
                    var y = _q[3] - _q[0];
                    dc.PushTransform(new MatrixTransform(new Matrix(x.X, x.Y, y.X, y.Y, _q[0].X, _q[0].Y)));
                    dc.DrawImage(_piece[i], new Rect(0, 0, 1, 1));
                    dc.Pop();
                    int shade = (int)Math.Round((1 - n.Z) * 8);
                    if (!flat) dc.DrawGeometry(shade > 0 ? Dark[Math.Min(8, shade)] : null, Edge, outline);
                }
                else
                {
                    var fill = f == 1 ? Ink : n.Y < -.25 ? _top[i] : _side[i];
                    dc.DrawGeometry(fill, Edge, outline);
                }
            }
        }

        private static StreamGeometry Quad(Point[] q)
        {
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                s.BeginFigure(q[0], true, true);
                s.PolyLineTo(new[] { q[1], q[2], q[3] }, true, false);
            }

            g.Freeze();
            return g;
        }

        private static Color Scale(Color c, double k, double alpha) => Color.FromArgb(
            (byte)(255 * alpha), (byte)Math.Min(255, c.R * k), (byte)Math.Min(255, c.G * k), (byte)Math.Min(255, c.B * k));

        private static T Frozen<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}
