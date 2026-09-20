using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using MacDock.Core.Interop;

namespace MacDock.App.Windows;

public partial class PreviewWindow : Window
{
    private const int ThumbHeight = 150;
    private const int Gap = 8;
    private const int Inset = 8;

    private readonly List<DwmThumbnail.Thumbnail> _thumbnails = new();

    public PreviewWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyling.MakeNonActivatingToolWindow(new WindowInteropHelper(this).Handle);
    }

    public void ShowFor(IReadOnlyList<IntPtr> sources)
    {
        ClearThumbnails();

        var hwnd = new WindowInteropHelper(this).Handle;
        var placed = new List<(DwmThumbnail.Thumbnail Thumb, int Width, int X)>();
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
            placed.Add((thumb, width, x));
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

        foreach (var (thumb, width, left) in placed)
        {
            thumb.Show(left, Inset, left + width, Inset + ThumbHeight);
            _thumbnails.Add(thumb);
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
    }
}


