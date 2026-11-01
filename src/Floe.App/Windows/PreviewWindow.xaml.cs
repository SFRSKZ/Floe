using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Floe.App.Services;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Floe.App.Effects.Cyber;
using Floe.Core.Interop;

namespace Floe.App.Windows;

public partial class PreviewWindow : Window
{
    private const int ThumbHeight = 150;
    private const int Gap = 10;
    private const int Inset = 8;
    private const int Pad = 10;
    private const int Caption = 24;

    private readonly List<DwmThumbnail.Thumbnail> _thumbnails = new();
    private readonly List<IntPtr> _sources = new();
    private EventHooks.Hook? _destroyHook;

    public event Action? WindowActivated;

    public event Action? SourcesGone;

    public event Action? SourcesChanged;

    public PreviewWindow()
    {
        InitializeComponent();
        ApplyTheme();
    }

    // A layered window lets clicks through wherever its pixels are fully clear, so the cards are solid and the gaps
    // between them keep the faintest fill: the pointer can cross from card to card without the popup closing.
    private static readonly Brush Catch = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

    // the popup is kept for the life of the dock, so its frame is read again every time it is shown
    private void ApplyTheme()
    {
        if (GlassSettings.Cyber)
        {
            Frame.CornerRadius = new CornerRadius(0);
            Frame.BorderThickness = new Thickness(1.5);
            Frame.BorderBrush = new SolidColorBrush(CyberLook.Current.Strip);
            Hint.FontFamily = CyberLook.Font;
            Frame.Background = Catch;
            return;
        }

        // the glass card the markup draws: rounded, a white hairline and the inherited font
        Frame.CornerRadius = new CornerRadius(12);
        Frame.BorderThickness = new Thickness(1);
        Frame.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        Hint.ClearValue(TextBlock.FontFamilyProperty);
        Frame.Background = Catch;
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
        ApplyTheme();
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
            x += width + (2 * Pad) + Gap;
        }

        if (placed.Count == 0)
        {
            Hint.Visibility = Visibility.Visible;
            Width = 160;
            Height = 80;
            UpdateLayout();
            return;
        }

        // every window has its own frame now, so the popup itself draws none
        Hint.Visibility = Visibility.Collapsed;
        Frame.BorderThickness = new Thickness(0);
        Width = x - Gap + Inset;
        Height = Caption + ThumbHeight + Pad + (2 * Inset);
        foreach (var (_, source, width, left) in placed) AddCard(source, left, width);
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        foreach (var (thumb, source, width, left) in placed)
        {
            thumb.Show(
                (int)Math.Round((left + Pad) * dpi.DpiScaleX),
                (int)Math.Round((Inset + Caption) * dpi.DpiScaleY),
                (int)Math.Round((left + Pad + width) * dpi.DpiScaleX),
                (int)Math.Round((Inset + Caption + ThumbHeight) * dpi.DpiScaleY));
            _thumbnails.Add(thumb);
            _sources.Add(source);
        }
    }

    // One window's frame: its title above the live thumbnail, which DWM draws over the card. The whole card is the button.
    private void AddCard(IntPtr source, int left, int thumbWidth)
    {
        bool cyber = GlassSettings.Cyber;
        var size = new Size(thumbWidth + (2 * Pad), Caption + ThumbHeight + Pad);
        var look = CyberLook.Current;
        var hot = cyber ? new SolidColorBrush(CyberLook.Readable(look.Accent, CyberLook.PanelInk)) : new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF));
        var cold = cyber ? new SolidColorBrush(CyberLook.Readable(look.Strip, CyberLook.PanelInk)) : new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        var fill = new SolidColorBrush(cyber ? CyberLook.PanelInk : Color.FromArgb(0xE0, 0x16, 0x16, 0x1A));

        var title = new TextBlock
        {
            Text = ScreenInput.GetWindowInfo(source).Title,
            Foreground = cyber ? cold : new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            FontFamily = CyberLook.UiFont,
            FontSize = cyber ? 13 : 11.5,
            FontWeight = cyber ? FontWeights.SemiBold : FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Height = Caption,
            Padding = new Thickness(0, 4, 0, 0),
        };

        var card = new Grid { Width = size.Width, Height = size.Height, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        Action<bool> show;
        if (cyber)
        {
            // Pad keeps the bar and the cut corner clear of the thumbnail, which would paint over them
            var plate = new ItemPlate();
            card.Children.Add(plate);
            show = on =>
            {
                plate.Hot = on;
                title.Foreground = on ? hot : cold;
            };
        }
        else
        {
            var glass = new Border { Background = fill, BorderBrush = cold, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10) };
            card.Children.Add(glass);
            show = on =>
            {
                glass.BorderBrush = on ? hot : cold;
                glass.BorderThickness = new Thickness(on ? 2 : 1);
            };
        }

        card.Children.Add(new Decorator { Child = title, Margin = new Thickness(Pad + 4, 0, Pad + 28, 0), VerticalAlignment = VerticalAlignment.Top });
        card.Children.Add(CloseButton(source, title.Foreground, new SolidColorBrush(cyber ? DockWindow.FxRed : Color.FromRgb(0xC4, 0x2B, 0x1C))));
        card.MouseEnter += (_, _) => show(true);
        card.MouseLeave += (_, _) => show(false);
        card.MouseLeftButtonUp += (_, e) =>
        {
            if (WindowActivator.Activate(source)) WindowActivated?.Invoke();
            e.Handled = true;
        };

        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, Inset);
        Cards.Children.Add(card);
    }

    // the X in the card's caption; it sits above the thumbnail, which would paint over it
    private FrameworkElement CloseButton(IntPtr source, Brush ink, Brush danger)
    {
        var cross = new Path
        {
            Data = Geometry.Parse("M0,0 L9,9 M9,0 L0,9"),
            Stroke = ink,
            StrokeThickness = 1.7,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var button = new Border
        {
            Width = 22,
            Height = 20,
            Margin = new Thickness(0, 4, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            ToolTip = Loc.T("Kapat"),
            Child = cross,
        };
        button.MouseEnter += (_, _) =>
        {
            button.Background = danger;
            cross.Stroke = Brushes.White;
        };
        button.MouseLeave += (_, _) =>
        {
            button.Background = Brushes.Transparent;
            cross.Stroke = ink;
        };
        button.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseSource(source);
        };
        return button;
    }

    private void CloseSource(IntPtr source)
    {
        AppWindows.RequestClose(source);

        // an app that closes to the tray only hides its window, which no destroy event reports
        var check = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        check.Tick += (_, _) =>
        {
            check.Stop();
            if (!AppWindows.IsShown(source)) OnSourceDestroyed(source);
        };
        check.Start();
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
        _sources.Clear();
        Cards.Children.Clear();
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
            SourcesChanged?.Invoke();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _destroyHook?.Dispose();
        ClearThumbnails();
        base.OnClosed(e);
    }
}
