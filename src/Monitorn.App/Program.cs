// EXP03/EXP04 - Monitorn per-monitor control surface (UI only).
//
// The control surface of the tray app: one row per monitor, each with the monitor's
// name, a 0-100 brightness slider and a gear button. This is a UI spike only:
//   * monitor rows are HARDCODED samples - nothing is detected,
//   * the sliders change their own value label and nothing else,
//   * no DDC/CI, no WMI, no brightness write of any kind.
//
// EXP04 wires the gear to the dummy "Schedule night light" settings window (SettingsWindow.cs)
// and adds --settings, which opens that window on its own so it can be verified directly.
//
// Built code-only (no XAML) so the in-box csc.exe alone can produce it - no SDK, no VS.
// Target: .NET Framework 4.8, WPF.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Monitorn.App
{
    /// <summary>Presentation model for one monitor row.</summary>
    internal sealed class MonitorRow
    {
        public MonitorRow(int index, string name, int brightness)
        {
            Index = index;
            Name = name;
            Brightness = brightness;
        }

        public int Index { get; private set; }
        public string Name { get; private set; }
        public int Brightness { get; set; }

        /// <summary>A display always gets a label, even when the monitor reports no name.</summary>
        public string DisplayName
        {
            get { return string.IsNullOrEmpty(Name) ? "Display " + Index.ToString(CultureInfo.InvariantCulture) : Name; }
        }
    }

    internal static class Surface
    {
        private static readonly FontFamily GlyphFont = new FontFamily("Segoe MDL2 Assets");
        private const string GearGlyph = "\uE713"; // Segoe MDL2 Assets: Settings

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x76, 0x76, 0x76));
        private static readonly Brush RowFill = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
        private static readonly Brush RowEdge = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));
        private static readonly Brush GearFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush GearEdge = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));

        public static Window Build(IList<MonitorRow> rows, double width, Action onSettings)
        {
            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

            root.Children.Add(Label("Monitorn", 16, Ink, FontWeights.SemiBold, new Thickness(0, 0, 0, 2)));
            root.Children.Add(Label("Brightness per display", 12, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 14)));

            foreach (MonitorRow row in rows) root.Children.Add(Row(row, onSettings));

            root.Children.Add(Label(
                "UI spike - sliders change their own label, the gear opens dummy settings, nothing is written to a monitor.",
                11, Muted, FontWeights.Normal, new Thickness(0, 4, 0, 0)));

            return new Window
            {
                Title = "Monitorn",
                Width = width,
                SizeToContent = SizeToContent.Height,
                MinWidth = 300,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = Brushes.White,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Content = root,
            };
        }

        private static TextBlock Label(string text, double size, Brush brush, FontWeight weight, Thickness margin)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = brush,
                FontWeight = weight,
                TextWrapping = TextWrapping.Wrap,
                Margin = margin,
            };
        }

        /// <summary>One monitor: name + live value above a slider, gear button at the right.</summary>
        private static Border Row(MonitorRow row, Action onSettings)
        {
            var value = new TextBlock
            {
                Text = row.Brightness.ToString(CultureInfo.InvariantCulture),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ink,
                MinWidth = 24,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var name = new TextBlock
            {
                Text = row.DisplayName,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = string.IsNullOrEmpty(row.Name) ? "This display reported no name" : row.Name,
            };

            var caption = new Grid();
            caption.ColumnDefinitions.Add(new ColumnDefinition()); // name takes the slack
            caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(name, 0);
            Grid.SetColumn(value, 1);
            caption.Children.Add(name);
            caption.Children.Add(value);

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                Value = row.Brightness,
                SmallChange = 1,
                LargeChange = 10,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Margin = new Thickness(0, 8, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                row.Brightness = (int)Math.Round(e.NewValue);
                value.Text = row.Brightness.ToString(CultureInfo.InvariantCulture);
            };

            var column = new StackPanel();
            column.Children.Add(caption);
            column.Children.Add(slider);

            var gear = new Button
            {
                Content = new TextBlock { Text = GearGlyph, FontFamily = GlyphFont, FontSize = 16, Foreground = Ink },
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                Background = GearFill,
                BorderBrush = GearEdge,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                ToolTip = "Settings for " + row.DisplayName,
                // EXP04: the gear now opens the (dummy) night-light schedule settings window.
            };
            gear.Click += delegate { onSettings(); };
            // An icon-only button has no readable name; give it one (accessibility), which is also
            // what the verifier finds it by - a WPF Slider puts its own Buttons in the UIA tree.
            System.Windows.Automation.AutomationProperties.SetName(gear, "Settings for " + row.DisplayName);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(column, 0);
            Grid.SetColumn(gear, 1);
            grid.Children.Add(column);
            grid.Children.Add(gear);

            return new Border
            {
                Child = grid,
                Background = RowFill,
                BorderBrush = RowEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 10, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int count = 3;      // --rows N: how many sample monitors to lay out
            double width = 400; // --width N: window width in DIPs
            bool settingsOnly = false; // --settings: open the night-light settings window by itself
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--rows" && i + 1 < args.Length) count = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--width" && i + 1 < args.Length) width = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--settings") settingsOnly = true;
            }
            if (count < 1) count = 1;

            // The two monitors EXP02 found on this machine, plus one unnamed row to exercise the
            // fallback label. Sample data only - no detection happens here.
            var known = new List<MonitorRow>
            {
                new MonitorRow(1, "AOC Q27G3G3R3", 47),  // external, DDC/CI
                new MonitorRow(2, "BOE BOE0764", 62),    // internal panel, WMI
                new MonitorRow(3, null, 50),             // unnamed -> "Display 3"
            };

            var rows = new List<MonitorRow>();
            for (int i = 0; i < count; i++) rows.Add(known[i % known.Count]);

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

            if (settingsOnly)
            {
                app.Run(SettingsSurface.Build(new ScheduleSettings()));
                return;
            }

            var host = new SettingsHost();
            app.Run(Surface.Build(rows, width, delegate { host.Show(); }));
        }
    }
}
