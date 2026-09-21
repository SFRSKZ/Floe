using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MacDock.App.Services;

namespace MacDock.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly List<(Border Row, int Index)> _rows = new();

    public SettingsWindow()
    {
        InitializeComponent();

        AuraSettings.Load();
        BuildPaletteRows();

        ModePalette.IsChecked = AuraSettings.Mode == AuraColorMode.Palette;
        ModeIcon.IsChecked = AuraSettings.Mode == AuraColorMode.Icon;
        ModePalette.Checked += (_, _) => AuraSettings.SetMode(AuraColorMode.Palette);
        ModeIcon.Checked += (_, _) => AuraSettings.SetMode(AuraColorMode.Icon);

        AuraSettings.Changed += RefreshRows;
        Closed += (_, _) => AuraSettings.Changed -= RefreshRows;

        RefreshRows();
    }

    private void BuildPaletteRows()
    {
        for (var i = 0; i < AuraSettings.Palettes.Count; i++)
        {
            var palette = AuraSettings.Palettes[i];
            var row = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 0, 6),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var color in palette.Colors)
            {
                content.Children.Add(new Ellipse
                {
                    Width = 14,
                    Height = 14,
                    Margin = new Thickness(0, 0, 4, 0),
                    Fill = new SolidColorBrush(color),
                });
            }

            content.Children.Add(new TextBlock
            {
                Text = palette.Name,
                Foreground = new SolidColorBrush(Color.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)),
                FontSize = 12.5,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });

            row.Child = content;

            var index = i;
            row.MouseLeftButtonUp += (_, _) => AuraSettings.SetPalette(index);

            _rows.Add((row, index));
            PaletteList.Children.Add(row);
        }
    }

    private void RefreshRows()
    {
        var paletteActive = AuraSettings.Mode == AuraColorMode.Palette;

        foreach (var (row, index) in _rows)
        {
            var selected = paletteActive && index == AuraSettings.PaletteIndex;
            row.Background = new SolidColorBrush(selected
                ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
            row.BorderBrush = new SolidColorBrush(selected
                ? Color.FromArgb(0x80, 0x5B, 0xC8, 0xFF)
                : Color.FromArgb(0x1E, 0xFF, 0xFF, 0xFF));
            row.Opacity = paletteActive ? 1 : 0.45;
            row.IsHitTestVisible = paletteActive;
        }

        PaletteHeader.Opacity = paletteActive ? 1 : 0.45;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
