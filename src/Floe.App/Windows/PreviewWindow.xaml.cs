using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Floe.Core.Interop;

namespace Floe.App.Windows;

public partial class PreviewWindow : Window
{
    private const int ThumbHeight = 150;
    private const int Gap = 8;
    private const int Inset = 8;

    private readonly List<DwmThumbnail.Thumbnail> _thumbnails = new();
    private readonly List<(IntPtr Source, int Left, int Width)> _items = new();
    private readonly List<IntPtr> _sources = new();
    private EventHooks.Hook? _destroyHook;

    public event Action? WindowActivated;

    public event Action? SourcesGone;

    public PreviewWindow()
    {
        InitializeComponent();
    }

    public bool IsCursorOver
    {
        get
        {
            if (!IsVisible)
            {
                return false;
            }

            var (x, y) = ScreenInput.GetCursorPosition();
            var topLeft = PointToScreen(new Point(0, 0));
            var bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
            return x >= topLeft.X && x <= bottomRight.X && y >= topLeft.Y && y <= bottomRight.Y;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        WindowStyling.MakeNonActivatingToolWindow(hwnd);
        _destroyHook = new EventHooks.Hook(EventHooks.EventObjectDestroy, EventHooks.EventObjectDestroy, OnSourceDestroyed);
    }

    public void ShowFor(IReadOnlyList<IntPtr> sources)
    {
        ClearThumbnails();

        var hwnd = new WindowInteropHelper(this).Handle;
        var placed = new List<(DwmThumbnail.Thumbnail Thumb, IntPtr Source, int Width, int X)>();
        var x = Inset;

        foreach (var source in sources.Take(4))
        {
            var thumb = DwmThumbnail.Thumbnail.Register(hwnd, source);
            if (thumb is null)
            {
                continue;
            }

            var (sourceWidth, sourceHeight) = thumb.GetSourceSize();
            if (sourceWidth <= 0 || sourceHeight <= 0)
            {
                thumb.Dispose();
                continue;
            }

            var width = (int)Math.Round(ThumbHeight * (double)sourceWidth / sourceHeight);
            placed.Add((thumb, source, width, x));
            x += width + Gap;
        }

        if (placed.Count == 0)
        {
            Hint.Visibility = Visibility.Visible;
            Width = 160;
            Height = 80;
            UpdateLayout();
            return;
        }

        Hint.Visibility = Visibility.Collapsed;
        Width = x - Gap + Inset;
        Height = ThumbHeight + (2 * Inset);
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        foreach (var (thumb, source, width, left) in placed)
        {
            thumb.Show(
                (int)Math.Round(left * dpi.DpiScaleX),
                (int)Math.Round(Inset * dpi.DpiScaleY),
                (int)Math.Round((left + width) * dpi.DpiScaleX),
                (int)Math.Round((Inset + ThumbHeight) * dpi.DpiScaleY));
            _thumbnails.Add(thumb);
            _items.Add((source, left, width));
            _sources.Add(source);
        }
    }

    public void Close3()
    {
        ClearThumbnails();
        Hide();
    }

    private void ClearThumbnails()
    {
        foreach (var thumbnail in _thumbnails)
        {
            thumbnail.Dispose();
        }

        _thumbnails.Clear();
        _items.Clear();
        _sources.Clear();
    }

    private void OnSourceDestroyed(IntPtr hwnd)
    {
        if (!_sources.Remove(hwnd))
        {
            return;
        }

        if (_sources.Count == 0)
        {
            SourcesGone?.Invoke();
            return;
        }

        if (IsVisible)
        {
            ShowFor(_sources.ToArray());
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        var position = e.GetPosition(this);
        foreach (var (source, left, width) in _items)
        {
            if (position.X < left
                || position.X > left + width
                || position.Y < Inset
                || position.Y > Inset + ThumbHeight)
            {
                continue;
            }

            if (WindowActivator.Activate(source))
            {
                WindowActivated?.Invoke();
            }

            e.Handled = true;
            return;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _destroyHook?.Dispose();
        ClearThumbnails();
        base.OnClosed(e);
    }
}
