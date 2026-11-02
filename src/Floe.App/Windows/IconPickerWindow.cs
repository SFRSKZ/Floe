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
    private readonly WrapPanel _grid = new() { Width = Columns * CellSize };
    private readonly TextBox _search = new();
    private readonly List<(Border Tab, bool Cards)> _tabs = new();
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

        if (GlassSettings.Cyber) AddSet(body);

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
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        tabs.Children.Add(SetTab(Loc.T("Kontur"), false));
        tabs.Children.Add(SetTab(Loc.T("Cyberpunk"), true));
        body.Children.Add(tabs);

        var search = _search;
        search.FontFamily = CyberLook.UiFont;
        search.FontSize = 14;
        search.Padding = new Thickness(8, 5, 8, 5);
        search.Background = CyberFxLayer.Solid(Color.FromRgb(0x1A, 0x1A, 0x24));
        search.Foreground = Cold;
        search.CaretBrush = Cold;
        search.BorderBrush = Cold;
        search.BorderThickness = new Thickness(1);
        search.Margin = new Thickness(0, 0, 0, 10);
        search.ToolTip = Loc.T("İkon ara");
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

        ShowSet(IconChoices.CardIcon(_item.App) is not null || (IconChoices.SetIcon(_item.App) is null && GlassSettings.IconSet == DockIconSet.Cards));
        body.Children.Add(new ScrollViewer
        {
            Height = SetHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Content = _grid,
        });
    }

    private Border SetTab(string text, bool cards)
    {
        var tab = new Border
        {
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 8, 0),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = Label(text, Cold, 14),
        };
        tab.MouseLeftButtonUp += (_, _) => ShowSet(cards);
        _tabs.Add((tab, cards));
        return tab;
    }

    private void ShowSet(bool cards)
    {
        foreach (var (tab, isCards) in _tabs)
        {
            bool active = isCards == cards;
            tab.BorderBrush = active ? Hot : Brushes.Transparent;
            ((TextBlock)tab.Child).Foreground = active ? Hot : Cold;
        }

        _cells.Clear();
        _grid.Children.Clear();
        var chosen = cards ? IconChoices.CardIcon(_item.App) : IconChoices.SetIcon(_item.App);
        foreach (var (name, file) in cards ? CardIcons.All : ContourIcons.All)
        {
            UIElement face = cards
                ? new Image { Source = CardIcons.Load(file) }
                : new Rectangle { Fill = Cold, OpacityMask = ContourIcons.Load(file) };
            Action choose = cards
                ? () => IconChoices.ChooseCardIcon(_item.App, file)
                : () => IconChoices.ChooseSetIcon(_item.App, file);
            var cell = Cell(name, face, file == chosen, choose);
            _cells.Add((cell, name));
            _grid.Children.Add(cell);
        }

        Filter(_search.Text);
    }

    private Border Cell(string name, UIElement face, bool chosen, Action choose)
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
            Child = face,
        };
        cell.MouseEnter += (_, _) => cell.Background = CyberFxLayer.Solid(Color.FromArgb(0x40, 0x80, 0x80, 0x90));
        cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
        cell.MouseLeftButtonUp += (_, _) => Finish(choose);
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
