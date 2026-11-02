using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using Floe.App.Effects.Cyber;
using Floe.App.Services;

namespace Floe.App.Windows;

public partial class DockWindow
{
    // A USB device goes in like a chip into the slot: the cyber themes draw the relic mark above the bar and read it.

    private const int DeviceChangeMessage = 0x0219;
    private const int DeviceArrival = 0x8000;
    private const int DeviceInterfaceType = 5;
    private const long RelicCooldownMs = 3000;
    private const double RelicSeconds = 3.4;
    private const double RelicStroke = 68;
    private const double RelicHeight = 590;
    private static readonly Guid UsbDeviceClass = new("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    private static readonly Color RelicGold = Color.FromRgb(0xD8, 0xB0, 0x5A);

    // the centre of the logo's one stroke, in the logo's own pixels
    private static readonly Point[] RelicLine =
    {
        new(39, 345), new(39, 541), new(268, 541), new(186, 425), new(281, 328), new(281, 223), new(39, 223), new(39, 49),
        new(491, 49), new(491, 541), new(420, 541), new(305, 403), new(405, 290), new(405, 134), new(110, 134),
    };

    private static readonly PathGeometry RelicPath = BuildRelicPath();
    private static readonly double RelicLength = MeasureRelicPath();

    private IntPtr _usbNotification;
    private long _lastRelic = long.MinValue / 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInterfaceFilter
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
        public Guid ClassGuid;
        public short Name;
    }

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "RegisterDeviceNotificationW")]
    private static extern IntPtr RegisterDeviceNotification(IntPtr recipient, ref DeviceInterfaceFilter filter, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr handle);

    // plain windows only hear about drives on their own, a keyboard or a phone needs this
    private void WatchUsbDevices()
    {
        var filter = new DeviceInterfaceFilter
        {
            Size = Marshal.SizeOf<DeviceInterfaceFilter>(),
            DeviceType = DeviceInterfaceType,
            ClassGuid = UsbDeviceClass,
        };
        _usbNotification = RegisterDeviceNotification(_hwnd, ref filter, 0);
    }

    private void StopWatchingUsbDevices()
    {
        if (_usbNotification == IntPtr.Zero) return;
        UnregisterDeviceNotification(_usbNotification);
        _usbNotification = IntPtr.Zero;
    }

    // one plug can arrive several times over, as a device, a hub and a drive
    private void NoteUsbArrival()
    {
        long now = Environment.TickCount64;
        if (now - _lastRelic < RelicCooldownMs) return;
        _lastRelic = now;
        if (!GlassSettings.Cyber || !IsVisible || FxAsleep) return;
        PlayRelic();
    }

    private void PlayRelic()
    {
        const double width = 300, height = 66;
        var look = CyberLook.Current;
        var red = CyberFxLayer.Solid(look.Accent);
        var ink = CyberFxLayer.Solid(FxInk);
        var gold = CyberFxLayer.Solid(RelicGold);
        var cyan = CyberFxLayer.Solid(FxCyan);
        var edge = new Pen(red, 1.5);
        string title = "RELIC";
        string line = Loc.T("USB // BAĞLANTI KURULDU");

        _fx.Play(RelicSeconds, (dc, p) =>
        {
            double tick = Math.Floor(p * RelicSeconds * 20);
            double tear = CyberFxLayer.Segment(p, .92, 1);
            if (tear > 0 && tick % 2 == 0) return;

            var rect = CapsuleRect();
            double jitter = tear > 0 ? (CyberFxLayer.Random(tick + .3) - .5) * 14 : 0;
            var card = new Rect(rect.Left + (rect.Width / 2) - (width / 2) + jitter, Math.Max(1, RestTop - height - 6), width, height);
            dc.DrawGeometry(ink, edge, CyberFxLayer.Plate(card, 9));

            var slot = new Rect(card.X + 12, card.Y + 7, 47, 52);
            DrawRelicMark(dc, slot, CyberFxLayer.Segment(p, .03, .6), look.Accent);

            _fx.Text(dc, CyberFxLayer.Decode(title, CyberFxLayer.Segment(p, .5, .68), tick), slot.Right + 12, card.Y + 6, 26, red);
            _fx.Text(dc, CyberFxLayer.Decode(line, CyberFxLayer.Segment(p, .55, .74), tick), slot.Right + 12, card.Y + 38, 13, cyan);

            // the contacts of the chip seat one after another
            for (var i = 0; i < 8; i++)
            {
                if (CyberFxLayer.Segment(p, .6, .78) * 8 <= i) continue;
                dc.DrawRectangle(gold, null, new Rect(card.Right - 58 + (i * 6), card.Y + 12, 3.5, height - 24));
            }
        });
    }

    // the mark is one unbroken line: it draws itself from its first end to its last, led by a white-hot tip
    private static void DrawRelicMark(DrawingContext dc, Rect box, double drawn, Color colour)
    {
        if (drawn <= 0) return;
        double scale = box.Height / RelicHeight;
        double units = RelicLength / RelicStroke;
        double run = drawn * units;
        dc.PushTransform(new TranslateTransform(box.X, box.Y));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, new Pen(CyberFxLayer.Solid(colour), RelicStroke)
        {
            LineJoin = PenLineJoin.Round,
            DashCap = PenLineCap.Round,
            DashStyle = new DashStyle(new[] { run, units * 2 }, 0),
        }, RelicPath);

        if (drawn < 1)
        {
            const double tip = .9;
            dc.DrawGeometry(null, new Pen(Brushes.White, RelicStroke)
            {
                LineJoin = PenLineJoin.Round,
                DashCap = PenLineCap.Round,
                DashStyle = new DashStyle(new[] { tip, units * 3 }, (units * 3) + tip - Math.Max(0, run - tip)),
            }, RelicPath);
        }

        dc.Pop();
        dc.Pop();
    }

    private static PathGeometry BuildRelicPath()
    {
        const double round = 34;
        var figure = new PathFigure { StartPoint = RelicLine[0], IsFilled = false };
        for (var i = 1; i < RelicLine.Length - 1; i++)
        {
            Point corner = RelicLine[i];
            Vector back = RelicLine[i - 1] - corner, on = RelicLine[i + 1] - corner;
            double cut = Math.Min(round, Math.Min(back.Length, on.Length) * .45);
            back.Normalize();
            on.Normalize();
            figure.Segments.Add(new LineSegment(corner + (back * cut), true));
            figure.Segments.Add(new QuadraticBezierSegment(corner, corner + (on * cut), true));
        }

        figure.Segments.Add(new LineSegment(RelicLine[^1], true));
        var path = new PathGeometry(new[] { figure });
        path.Freeze();
        return path;
    }

    private static double MeasureRelicPath()
    {
        const int samples = 800;
        double length = 0;
        RelicPath.GetPointAtFractionLength(0, out var last, out _);
        for (var i = 1; i <= samples; i++)
        {
            RelicPath.GetPointAtFractionLength(i / (double)samples, out var point, out _);
            length += (point - last).Length;
            last = point;
        }

        return length;
    }
}
