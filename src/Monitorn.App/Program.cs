// EXP05 - Monitorn per-monitor control surface, now with the brightness functionality alive.
// EXP06 - and the night-light schedule applied: the surface drives real monitors, and the schedule
//         fades them down to the strength at turn-on and gives the previous value back at turn-off.
//
// One row per ACTIVE display: its real name, its real current brightness on a slider, and a gear
// that opens the settings window (EXP04). Dragging a slider is debounced by BrightnessWriter and
// then written to that monitor - DDC/CI for an external display, WMI for an internal panel - and
// the value read for each row comes from the monitor, not from a sample table.
//
// The schedule is NightLightScheduler (Schedule.cs): pure state, an injected clock, and the same
// settings object the window edits. The tick only asks it what it wants and hands that to the
// writer, which is why a drag and a fade share one actuator.
//
// Nothing is persisted yet. Built code-only (no XAML) with the in-box csc.exe - no SDK, no VS.
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

        public static Window Build(IList<MonitorInfo> monitors, BrightnessWriter writer, NightLightScheduler scheduler,
            double width, Action onSettings, out TextBlock status)
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
                foreach (MonitorInfo monitor in monitors) root.Children.Add(Row(monitor, writer, scheduler, onSettings));
            }

            // What the schedule is doing right now. It carries a fixed automation id so a verifier
            // can read this one element instead of guessing which text node is the status; its
            // AutomationProperties.Name is deliberately left alone, because UIA reports that name
            // instead of the text, and the text is the whole point.
            status = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = Ink,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(status, "NightLightStatus");
            root.Children.Add(status);

            root.Children.Add(Label(
                monitors.Count == 0
                    ? ""
                    : "Drag a slider to set a monitor. The night-light schedule dims every display to "
                      + "its strength at the time you set, and gives it back afterwards. Nothing is saved yet.",
                11, Muted, FontWeights.Normal, new Thickness(0, 8, 0, 0)));

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
        private static Border Row(MonitorInfo monitor, BrightnessWriter writer, NightLightScheduler scheduler, Action onSettings)
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
            // True while the slider is being moved by a write's outcome rather than by the user:
            // assigning Value raises ValueChanged, and that must not be mistaken for a drag - the
            // schedule would read its own move as the user taking over and stand down for the night.
            bool syncing = false;

            // The label shows what you asked for at once; the debounce decides when it lands.
            slider.ValueChanged += delegate(object sender, RoutedPropertyChangedEventArgs<double> e)
            {
                int v = (int)Math.Round(e.NewValue);
                value.Text = v.ToString(CultureInfo.InvariantCulture);
                if (syncing) return;
                scheduler.Request(monitor.Id, v);   // the schedule learns this was the user, and stands back
                writer.Request(monitor.Id, v);
            };

            // A write's outcome comes back from the writer, not from the slider - and the slider
            // follows it, so the row cannot show one value while the monitor holds another.
            writer.Written += delegate(string id, int applied)
            {
                if (id != monitor.Id) return;
                syncing = true;
                try { if ((int)Math.Round(slider.Value) != applied) slider.Value = applied; }
                finally { syncing = false; }
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

        /// <summary>The gradual fade's default length, in steps of <see cref="Step"/>.</summary>
        private static readonly TimeSpan Fade = TimeSpan.FromMinutes(30);

        /// <summary>How coarse the fade's staircase is: one write per step, per display.</summary>
        private static readonly TimeSpan Step = TimeSpan.FromMinutes(1);

        [STAThread]
        private static void Main(string[] args)
        {
            double width = 400;          // --width N: window width in DIPs
            bool settingsOnly = false;   // --settings: open the night-light settings window by itself
            bool schedule = true;        // --no-schedule: run the surface without the scheduler
            DateTimeOffset? start = null; // --now <date time>: the clock starts here instead of now
            double rate = 1.0;           // --rate X: and then runs X times faster (verification)
            TimeSpan? turnOn = null;     // --on HH:mm / --off HH:mm: set the window outright
            TimeSpan? turnOff = null;
            int? strength = null;        // --strength N
            TimeSpan? fade = null;       // --fade-seconds N (the whole fade in N seconds)
            TimeSpan? step = null;       // --step-seconds N (one staircase step every N seconds)

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--width" && i + 1 < args.Length) width = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--settings") settingsOnly = true;
                else if (a == "--no-schedule") schedule = false;
                else if (a == "--now" && i + 1 < args.Length) start = DateTimeOffset.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--rate" && i + 1 < args.Length) rate = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--on" && i + 1 < args.Length) turnOn = ParseClock(args[++i]);
                else if (a == "--off" && i + 1 < args.Length) turnOff = ParseClock(args[++i]);
                else if (a == "--strength" && i + 1 < args.Length) strength = int.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--fade-seconds" && i + 1 < args.Length) fade = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
                else if (a == "--step-seconds" && i + 1 < args.Length) step = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

            // --- the settings the window edits and the schedule reads: one object ---
            var settings = new ScheduleSettings();
            SetWindow(settings, turnOn, turnOff);
            if (strength.HasValue) settings.TrySetStrength(strength.Value.ToString(CultureInfo.InvariantCulture));
            if (!schedule) settings.SetEnabled(false);

            if (settingsOnly)
            {
                app.Run(SettingsSurface.Build(settings));
                return;
            }

            // --- the clock: real, unless a verification run asked for a scripted night ---
            Func<DateTimeOffset> clock;
            if (start.HasValue)
            {
                DateTimeOffset origin = start.Value;
                double scale = rate > 0 ? rate : 1.0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                clock = delegate { return origin.AddMilliseconds(watch.Elapsed.TotalMilliseconds * scale); };
            }
            else clock = delegate { return DateTimeOffset.Now; };

            // Real detection: active displays, read once. No sample rows any more.
            var controller = new BrightnessController();
            IList<MonitorInfo> monitors;
            try { monitors = controller.Detect(); }
            catch { monitors = new List<MonitorInfo>(); }

            var writer = new BrightnessWriter(controller, Settle, clock);
            foreach (MonitorInfo monitor in monitors) writer.Seed(monitor.Id, monitor.Current);

            var scheduler = new NightLightScheduler(settings, clock,
                fade.HasValue ? fade.Value : Fade, step.HasValue ? step.Value : Step);
            foreach (MonitorInfo monitor in monitors) scheduler.Observe(monitor.Id, monitor.Min, monitor.Max, monitor.Current);

            var host = new SettingsHost(settings);
            TextBlock status;
            Window window = Surface.Build(monitors, writer, scheduler, width, delegate { host.Show(); }, out status);

            // One pass at launch, so a run started inside the night window dims at once.
            Apply(scheduler, writer);
            status.Text = scheduler.Status;

            // One timer drives both machines: the scheduler decides what it wants (and dedups it),
            // the writer decides when it lands. 200 ms keeps a slider drag responsive.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += delegate
            {
                Apply(scheduler, writer);
                status.Text = scheduler.Status;
            };
            timer.Start();

            window.Closing += delegate
            {
                timer.Stop();
                foreach (NightWrite w in scheduler.Restore()) writer.Request(w.Id, w.Value);
                writer.Flush();       // land the last value if the window closes mid-drag or mid-fade
                controller.Dispose();
            };

            app.Run(window);
        }

        /// <summary>Asks the schedule what it wants and hands it to the writer, which debounces it.</summary>
        private static void Apply(NightLightScheduler scheduler, BrightnessWriter writer)
        {
            foreach (NightWrite w in scheduler.Advance()) writer.Request(w.Id, w.Value);
            writer.Advance();
        }

        /// <summary>24-hour "HH:mm" from a command line into the model's 12-hour parts.</summary>
        private static TimeSpan ParseClock(string text)
        {
            int hour12, minute; bool pm;
            if (!ScheduleSettings.TryParseClock(text, out hour12, out minute, out pm))
                throw new ArgumentException("not a 24-hour time: " + text);
            TimeSpan value;
            ScheduleSettings.TryClock(hour12, minute, pm, out value);
            return value;
        }

        private static void SetWindow(ScheduleSettings settings, TimeSpan? on, TimeSpan? off)
        {
            SetOne(settings, on, true);
            SetOne(settings, off, false);
        }

        private static void SetOne(ScheduleSettings settings, TimeSpan? value, bool isOn)
        {
            if (!value.HasValue) return;
            int hour12, minute; bool pm;
            ScheduleSettings.ToParts(value.Value, out hour12, out minute, out pm);
            if (isOn) settings.TrySetTurnOn(hour12, minute, pm);
            else settings.TrySetTurnOff(hour12, minute, pm);
        }
    }
}
