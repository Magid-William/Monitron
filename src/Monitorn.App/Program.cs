// EXP05 - Monitorn per-monitor control surface, now with the brightness functionality alive.
//
// One row per ACTIVE display: its real name, its real current brightness on a slider, and a gear
// that opens the settings window (EXP04). Dragging a slider is debounced by BrightnessWriter and
// then written to that monitor - DDC/CI for an external display, WMI for an internal panel - and
// the value read for each row comes from the monitor, not from a sample table.
//
// Nothing is persisted and no schedule is applied yet; this experiment is detection + control.
// Built code-only (no XAML) with the in-box csc.exe - no SDK, no VS.
// Target: .NET Framework 4.8, WPF.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Monitorn.App
{
    internal static class Surface
    {
        private static readonly FontFamily GlyphFont = new FontFamily("Segoe MDL2 Assets");
        private const string GearGlyph = "\uE713"; // Segoe MDL2 Assets: Settings

        private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x76, 0x76, 0x76));
        private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x2B));
        private static readonly Brush RowFill = new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF7));
        private static readonly Brush RowEdge = new SolidColorBrush(Color.FromRgb(0xE4, 0xE4, 0xE4));
        private static readonly Brush GearFill = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly Brush GearEdge = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));

        public static Window Build(IList<MonitorInfo> monitors, BrightnessWriter writer, double width, Action onSettings)
        {
            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };

            root.Children.Add(Label("Monitorn", 16, Ink, FontWeights.SemiBold, new Thickness(0, 0, 0, 2)));
            root.Children.Add(Label("Brightness per display", 12, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 14)));

            if (monitors.Count == 0)
            {
                root.Children.Add(Label(
                    "No controllable displays were detected. A display is controllable when it is an "
                    + "active target and answers DDC/CI or the WMI brightness API.",
                    12, Muted, FontWeights.Normal, new Thickness(0, 0, 0, 0)));
            }
            else
            {
                foreach (MonitorInfo monitor in monitors) root.Children.Add(Row(monitor, writer, onSettings));
            }

            root.Children.Add(Label(
                monitors.Count == 0
                    ? ""
                    : "Drag a slider - the value is written to that monitor after it settles. "
                      + "Nothing is saved or scheduled yet.",
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
        private static Border Row(MonitorInfo monitor, BrightnessWriter writer, Action onSettings)
        {
            var value = new TextBlock
            {
                Text = monitor.Current.ToString(CultureInfo.InvariantCulture),
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
                Text = monitor.DisplayName,
                Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = monitor.Kind == MonitorKind.External ? "External display (DDC/CI)" : "Internal panel (WMI)",
            };

            var notes = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = Warn,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 4, 0, 0),
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
                Minimum = monitor.Min,
                Maximum = monitor.Max,
                Value = monitor.Current,
                SmallChange = 1,
                LargeChange = 10,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                IsEnabled = monitor.Max > monitor.Min,
                Margin = new Thickness(0, 8, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = monitor.DisplayName + " brightness, " + monitor.Min + "-" + monitor.Max,
            };
            // The label shows what you asked for at once; the debounce decides when it lands.
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                int v = (int)Math.Round(e.NewValue);
                value.Text = v.ToString(CultureInfo.InvariantCulture);
                writer.Request(monitor.Id, v);
            };

            // A write's outcome comes back from the writer, not from the slider.
            writer.Written += delegate(string id, int applied)
            {
                if (id != monitor.Id) return;
                value.Text = applied.ToString(CultureInfo.InvariantCulture);
                notes.Visibility = Visibility.Collapsed;
            };
            writer.Failed += delegate(string id, string error)
            {
                if (id != monitor.Id) return;
                notes.Text = "Last change was not applied (" + error + ").";
                notes.Visibility = Visibility.Visible;
            };

            var column = new StackPanel();
            column.Children.Add(caption);
            column.Children.Add(slider);
            column.Children.Add(notes);

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
                ToolTip = "Settings for " + monitor.DisplayName,
            };
            gear.Click += delegate { onSettings(); };
            // An icon-only button has no readable name; give it one (accessibility), which is also
            // what the verifier finds it by - a WPF Slider puts its own Buttons in the UIA tree.
            System.Windows.Automation.AutomationProperties.SetName(gear, "Settings for " + monitor.DisplayName);

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
        /// <summary>How long a slider must sit still before its value is written to the monitor.</summary>
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(250);

        [STAThread]
        private static void Main(string[] args)
        {
            double width = 400; // --width N: window width in DIPs
            bool settingsOnly = false; // --settings: open the night-light settings window by itself
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--width" && i + 1 < args.Length) width = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (args[i] == "--settings") settingsOnly = true;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

            if (settingsOnly)
            {
                app.Run(SettingsSurface.Build(new ScheduleSettings()));
                return;
            }

            // Real detection: active displays, read once. No sample rows any more.
            var controller = new BrightnessController();
            IList<MonitorInfo> monitors;
            try { monitors = controller.Detect(); }
            catch { monitors = new List<MonitorInfo>(); }

            var writer = new BrightnessWriter(controller, Settle, delegate { return DateTimeOffset.Now; });
            foreach (MonitorInfo monitor in monitors) writer.Seed(monitor.Id, monitor.Current);

            var host = new SettingsHost();
            Window window = Surface.Build(monitors, writer, width, delegate { host.Show(); });

            // The debounce timer: it only decides *when*; BrightnessWriter decides what and whether.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += delegate { writer.Advance(); };
            timer.Start();

            window.Closing += delegate
            {
                timer.Stop();
                writer.Flush();       // land the last value if the window closes mid-drag
                controller.Dispose();
            };

            app.Run(window);
        }
    }
}
