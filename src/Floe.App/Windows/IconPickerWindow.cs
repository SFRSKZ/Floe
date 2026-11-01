using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Floe.App.Effects.Cyber;
using Floe.App.Services;
using Floe.App.ViewModels;
using Microsoft.Win32;

namespace Floe.App.Windows;

/// <summary>Picks the icon of one dock app: from the outline set, from an image file, or back to what it had.</summary>
public sealed class IconPickerWindow : Window
{
    private const double CellSize = 70;
    private const int Columns = 8;
    private const double SetHeight = 372;

    private readonly DockItemViewModel _item;
    private readonly Action _changed;
    private readonly List<(Border Cell, string Name)> _cells = new();
    private bool _leaving;
    private bool _dialogOpen;

    public IconPickerWindow(DockItemViewModel item, Action changed)
    {
        _item = item;
        _changed = changed;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        Width = (Columns * CellSize) + 44;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = Loc.T("İkonu değiştir");

        var body = new StackPanel { Margin = new Thickness(20, 14, 20, 18) };
        var title = Label($"{Loc.T("İkonu değiştir")} // {item.Name.ToUpper(Loc.Culture)}", Hot, 17);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.Margin = new Thickness(0, 0, 0, 12);
        body.Children.Add(title);

        if (GlassSettings.Cyber && GlassSettings.ContourIcons) AddSet(body);

        var actions = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(Action(Loc.T("Dosyadan seç"), PickFile));
        actions.Children.Add(Action(Loc.T("Gerçek ikon"), () => Finish(() => IconChoices.ChooseReal(_item.App))));
        var automatic = Action(Loc.T("Otomatik"), () => Finish(() => IconChoices.Clear(_item.App)));
        automatic.ToolTip = Loc.T("Setteki eşi varsa onu, yoksa gerçek ikonu kullanır");
        actions.Children.Add(automatic);
        actions.Children.Add(Action(Loc.T("Vazgeç"), Leave));
        body.Children.Add(actions);

        var root = new Grid();
        root.Children.Add(new ItemPlate());
        root.Children.Add(body);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            Leave();
        };
        Deactivated += (_, _) =>
        {
            if (!_dialogOpen) Leave();
        };
    }

    private static Brush Hot => CyberFxLayer.Solid(CyberLook.Readable(CyberLook.Current.Accent, CyberLook.PanelInk));

    private static Brush Cold => CyberFxLayer.Solid(CyberLook.Readable(CyberLook.Current.Strip, CyberLook.PanelInk));

    private void AddSet(Panel body)
    {
        var search = new TextBox
        {
            FontFamily = CyberLook.UiFont,
            FontSize = 14,
            Padding = new Thickness(8, 5, 8, 5),
            Background = CyberFxLayer.Solid(Color.FromRgb(0x1A, 0x1A, 0x24)),
            Foreground = Cold,
            CaretBrush = Cold,
            BorderBrush = Cold,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 0, 10),
            ToolTip = Loc.T("İkon ara"),
        };
        var hint = Label(Loc.T("İkon ara"), Cold, 14);
        hint.Opacity = .6;
        hint.Margin = new Thickness(10, 6, 0, 0);
        hint.IsHitTestVisible = false;
        search.TextChanged += (_, _) =>
        {
            hint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Filter(search.Text);
        };
        Loaded += (_, _) =>
        {
            Activate();
            search.Focus();
        };
        var box = new Grid();
        box.Children.Add(search);
        box.Children.Add(hint);
        body.Children.Add(box);

        var grid = new WrapPanel { Width = Columns * CellSize };
        var chosen = IconChoices.SetIcon(_item.App);
        foreach (var (name, file) in ContourIcons.All)
        {
            var cell = Cell(name, file, file == chosen);
            _cells.Add((cell, name));
            grid.Children.Add(cell);
        }

        body.Children.Add(new ScrollViewer
        {
            Height = SetHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Content = grid,
        });
    }

    private Border Cell(string name, string file, bool chosen)
    {
        var cell = new Border
        {
            Width = CellSize,
            Height = CellSize,
            Background = Brushes.Transparent,
            BorderBrush = chosen ? Hot : Brushes.Transparent,
            BorderThickness = new Thickness(chosen ? 2 : 0),
            Cursor = Cursors.Hand,
            ToolTip = name,
            Child = new Rectangle { Fill = Cold, OpacityMask = ContourIcons.Load(file) },
        };
        cell.MouseEnter += (_, _) => cell.Background = CyberFxLayer.Solid(Color.FromArgb(0x40, 0x80, 0x80, 0x90));
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        cell.MouseLeftButtonUp += (_, _) => Finish(() => IconChoices.ChooseSetIcon(_item.App, file));
        return cell;
    }

    private void Filter(string text)
    {
        var wanted = text.Trim();
        foreach (var (cell, name) in _cells)
        {
            bool match = wanted.Length == 0 || name.Contains(wanted, StringComparison.CurrentCultureIgnoreCase);
            cell.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void PickFile()
    {
        var dialog = new OpenFileDialog { Title = Loc.T("İkonu değiştir"), Filter = $"{Loc.T("Görsel")}|*.png;*.ico;*.jpg;*.jpeg;*.bmp" };
        _dialogOpen = true;
        var picked = dialog.ShowDialog(this) == true;
        _dialogOpen = false;
        if (picked) Finish(() => IconChoices.ChooseFile(_item.App, dialog.FileName));
        else Activate();
    }

    private void Finish(Action choose)
    {
        choose();
        _changed();
        Leave();
    }

    private void Leave()
    {
        if (_leaving) return;
        _leaving = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _leaving = true;
        base.OnClosing(e);
    }

    private FrameworkElement Action(string text, Action run)
    {
        var label = Label(text, Cold, 14);
        var button = new Border
        {
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            BorderBrush = Cold,
            BorderThickness = new Thickness(1),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = label,
        };
        button.MouseEnter += (_, _) => button.BorderBrush = label.Foreground = Hot;
        button.MouseLeave += (_, _) => button.BorderBrush = label.Foreground = Cold;
        button.MouseLeftButtonUp += (_, _) => run();
        return button;
    }

    private static TextBlock Label(string text, Brush brush, double size) => new()
    {
        Text = text,
        Foreground = brush,
        FontFamily = CyberLook.Font,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
    };
}
