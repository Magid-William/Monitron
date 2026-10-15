// EXP05 - Monitron per-monitor control surface, now with the brightness functionality alive.
// EXP06 - and the night-light schedule applied: the surface drives real monitors, and the schedule
//         fades them down to the strength at turn-on and gives the previous value back at turn-off.
// EXP07 - and the schedule's settings persisted, so a restart keeps them (SettingsPersistence.cs).
// EXP08 - and the theme: one palette both windows draw from (Theme.cs), following the Windows app
//         theme, with the dark title bar and the control templates the OS theme would keep light
//         (Theming.cs, SystemTheme.cs). --theme and --theme-report are its verification seams.
// EXP09 - and the screen position: the surface sits in the notification area's corner of the monitor
//         it is on, above that monitor's taskbar, like a tray flyout (Placement.cs, SystemPlacement.cs,
//         PlacementApp.cs). --no-place and --place-* are its verification seams.
// EXP10 - and the verifier seam: each row's slider and gear carry that display's own id as their UI
//         Automation id ("DDC:<device path>" or "WMI:<InstanceName>"), so a verifier can bind a row
//         to a display on a machine with more than one active target.
// EXP11 - and the tray shell: a notification-area icon whose menu shows/hides the surface, toggles a
//         "Run on Windows startup" Run entry (AutoStart.cs) and quits - and closing the window now hides it,
//         so the schedule keeps running with no window open. TrayIcon.cs is the shell half, Tray.cs
//         the pure Show/Hide state. --no-tray / --hidden / --tray-menu / --startup-value /
//         --tray-report / --quit-after are its verification seams.
// EXP12 - and the app's own icon: the designed logo.svg, generated into a nine-frame Monitron.ico and
//         embedded in the exe (IconAssets.cs), for the exe, the window and the tray. --icon-report is
//         its verification seam; the logo is never edited here, it is an input.
// EXP13 - and the row's slider made reachable: a press on the track lands on the press rather than
//         one LargeChange away from it (WPF's move-to-point), the slider carries its display's name
//         for a screen reader, and its row shows where the keyboard is - the row's edge takes the
//         accent while that row's slider has the focus. verify-slider.ps1 drives all three.
//         Added at the user's ask after conclusion: the hint paragraph that sat under the schedule
//         status ("Drag a slider to set a monitor...") is gone - the rows and the status say enough.
// EXP14 - and the app's picture of the desktop made live. The display set is no longer read once at
//         launch: a WM_DISPLAYCHANGE re-detects, a DisplaySet diff adds and removes rows and prunes the
//         writer and the schedule, and the tray's Show re-detects too (DisplaySet.cs, DisplayWatcher.cs).
//         --monitors / --display-change-after / --display-report are its verification seams; the
//         placement (EXP09) and every other --place-* seam are unchanged. (A pointer-driven placement
//         was tried in this experiment and rolled back; see experiments/EXP14/README.md.)
// EXP15 - and the surface follows the tray: it opens on the monitor that holds the notification area
//         (the main taskbar's monitor), re-read on launch, on Show and on a real display/settings/DPI
//         change - not on the window's own monitor, which a hidden surface keeps stale. The re-place
//         hook is added on the spot (WindowHook), where EXP09's SourceInitialized hook never fired.
//         --place-anchor tray|window and --place-monitor are its seams; --place-report gains
//         anchor= and placements=.
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
// The settings live in a per-user file (SettingsStore): loaded at launch, saved on every real edit.
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

namespace Monitron.App
{
    /// <summary>
    /// The surface window and the rows in it. EXP14: the rows are no longer fixed at construction -
    /// a display that appears is added and one that goes away is removed, and a removed row unhooks
    /// itself from the writer so a departed display leaves nothing behind.
    /// </summary>
    internal sealed class SurfaceView
    {
        // The palette lives in Theme.cs now (EXP08): one ThemeBrushes instance is handed to every
        // row, so flipping the theme recolours the whole surface without rebuilding it.
        private static readonly FontFamily GlyphFont = new FontFamily("Segoe MDL2 Assets");
        private const string GearGlyph = "\uE713"; // Segoe MDL2 Assets: Settings

        private readonly BrightnessWriter _writer;
        private readonly NightLightScheduler _scheduler;
        private readonly ThemeBrushes _t;
        private readonly Action _onSettings;

        private readonly StackPanel _rows = new StackPanel();       // one card per display, in detection order
        private readonly TextBlock _empty;                          // shown when there is no row at all
        private readonly List<string> _order = new List<string>();  // the ids on screen, in order
        private readonly Dictionary<string, Border> _cards = new Dictionary<string, Border>();
        private readonly Dictionary<string, Action> _detach = new Dictionary<string, Action>();

        /// <summary>The window itself - it lives for the app's life, its rows do not.</summary>
        public Window Shell { get; private set; }

        /// <summary>The schedule's line, which the app rewrites on every tick.</summary>
        public TextBlock Status { get; private set; }

        /// <summary>The displays on screen right now.</summary>
        public IList<string> Ids { get { return _order; } }

        private SurfaceView(IList<MonitorInfo> monitors, BrightnessWriter writer, NightLightScheduler scheduler,
            ThemeState theme, double width, Action onSettings)
        {
            _writer = writer;
            _scheduler = scheduler;
            _t = theme.Brushes;
            _onSettings = onSettings;

            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
            root.Children.Add(Label("Monitron", 16, _t.Ink, FontWeights.SemiBold, new Thickness(0, 0, 0, 2)));
            root.Children.Add(Label("Brightness per display", 12, _t.Muted, FontWeights.Normal, new Thickness(0, 0, 0, 14)));
            root.Children.Add(_rows);

            _empty = Label(
                "No controllable displays were detected. A display is controllable when it is an "
                + "active target and answers DDC/CI or the WMI brightness API.",
                12, _t.Muted, FontWeights.Normal, new Thickness(0, 0, 0, 0));
            _empty.Visibility = Visibility.Collapsed;
            root.Children.Add(_empty);

            // What the schedule is doing right now. It carries a fixed automation id so a verifier
            // can read this one element instead of guessing which text node is the status; its
            // AutomationProperties.Name is deliberately left alone, because UIA reports that name
            // instead of the text, and the text is the whole point.
            Status = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = _t.Ink,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };
            System.Windows.Automation.AutomationProperties.SetAutomationId(Status, "NightLightStatus");
            root.Children.Add(Status);

            Shell = new Window
            {
                Title = "Monitron",
                Width = width,
                SizeToContent = SizeToContent.Height,
                MinWidth = 300,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Background = _t.WindowBg,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Content = root,
            };
            Theming.Bind(Shell, theme);   // the palette, and the templates the OS theme would keep light

            foreach (MonitorInfo monitor in monitors) Add(monitor);
        }

        public static SurfaceView Build(IList<MonitorInfo> monitors, BrightnessWriter writer, NightLightScheduler scheduler,
            ThemeState theme, double width, Action onSettings)
        {
            return new SurfaceView(monitors, writer, scheduler, theme, width, onSettings);
        }

        /// <summary>A display just appeared: give it a row (EXP14).</summary>
        public void Add(MonitorInfo monitor)
        {
            if (_cards.ContainsKey(monitor.Id)) return;
            RowWidget row = Row(monitor, _writer, _scheduler, _t, _onSettings);
            _rows.Children.Add(row.Card);
            _cards[monitor.Id] = row.Card;
            _detach[monitor.Id] = row.Detach;
            _order.Add(monitor.Id);
            UpdateEmpty();
        }

        /// <summary>A display has gone: take its row away and unhook it from the writer (EXP14).</summary>
        public void Remove(string id)
        {
            Border card;
            if (!_cards.TryGetValue(id, out card)) return;
            _rows.Children.Remove(card);
            Action detach;
            if (_detach.TryGetValue(id, out detach) && detach != null) detach();
            _cards.Remove(id);
            _detach.Remove(id);
            _order.Remove(id);
            UpdateEmpty();
        }

        private void UpdateEmpty()
        {
            _empty.Visibility = _cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        private static RowWidget Row(MonitorInfo monitor, BrightnessWriter writer, NightLightScheduler scheduler,
            ThemeBrushes t, Action onSettings)
        {
            var value = new TextBlock
            {
                Text = monitor.Current.ToString(CultureInfo.InvariantCulture),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = t.Ink,
                MinWidth = 24,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            };

            var name = new TextBlock
            {
                Text = monitor.DisplayName,
                Foreground = t.Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = monitor.Kind == MonitorKind.External ? "External display (DDC/CI)" : "Internal panel (WMI)",
            };

            var notes = new TextBlock
            {
                Text = "",
                FontSize = 11,
                Foreground = t.Warn,
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
                // EXP13: a press on the track lands on the press, not one LargeChange away from it.
                // WPF's own move-to-point: the thumb goes to the point, snapped to the tick and
                // clamped at the ends, and a press on the thumb itself is left to the drag.
                IsMoveToPointEnabled = true,
                IsEnabled = monitor.Max > monitor.Min,
                Margin = new Thickness(0, 8, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = monitor.DisplayName + " brightness, " + monitor.Min + "-" + monitor.Max,
            };
            // Which display this row drives, in the row's own words: monitor.Id is already
            // "DDC:<device path>" or "WMI:<InstanceName>", so a verifier can bind the row to a
            // display - and to the WMI instance it must read to witness an internal panel - without
            // guessing by position or by a value that two rows could share (EXP10).
            System.Windows.Automation.AutomationProperties.SetAutomationId(slider, monitor.Id);
            // EXP13: a slider with no name is announced as just "slider, 57". The gear beside it
            // already names itself; this one names the display it drives (the id above is for a
            // verifier, this name is for a person).
            System.Windows.Automation.AutomationProperties.SetName(slider, monitor.DisplayName + " brightness");
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
            // follows it, so the row cannot show one value while the monitor holds another. The two
            // handlers are kept so a removed row can unsubscribe (EXP14).
            Action<string, int> onWritten = delegate(string id, int applied)
            {
                if (id != monitor.Id) return;
                syncing = true;
                try { if ((int)Math.Round(slider.Value) != applied) slider.Value = applied; }
                finally { syncing = false; }
                value.Text = applied.ToString(CultureInfo.InvariantCulture);
                notes.Visibility = Visibility.Collapsed;
            };
            Action<string, string> onFailed = delegate(string id, string error)
            {
                if (id != monitor.Id) return;
                notes.Text = "Last change was not applied (" + error + ").";
                notes.Visibility = Visibility.Visible;
            };
            writer.Written += onWritten;
            writer.Failed += onFailed;

            var column = new StackPanel();
            column.Children.Add(caption);
            column.Children.Add(slider);
            column.Children.Add(notes);

            var gear = new Button
            {
                Content = new TextBlock { Text = GearGlyph, FontFamily = GlyphFont, FontSize = 16, Foreground = t.Ink },
                Width = 34,
                Height = 34,
                Padding = new Thickness(0),
                Background = t.ControlFill,
                BorderBrush = t.ControlEdge,
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
            System.Windows.Automation.AutomationProperties.SetAutomationId(gear, monitor.Id);   // EXP10: this row's display

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(column, 0);
            Grid.SetColumn(gear, 1);
            grid.Children.Add(column);
            grid.Children.Add(gear);

            var card = new Border
            {
                Child = grid,
                Background = t.RowFill,
                BorderBrush = t.RowEdge,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 10, 12),
                Margin = new Thickness(0, 0, 0, 10),
            };

            // EXP13: the row shows where the keyboard is. WPF's own cue is FocusVisualStyle, and
            // Windows draws it only for a user who has just typed - its AlwaysShowFocusVisual is
            // latched from the keyboard-cues setting when the process starts, and that setting is
            // off for anyone using the mouse - so the cue is ours: the row's edge takes the accent
            // while this row's slider has the keyboard. The gear beside it shows focus its own way,
            // from the template in Theming.cs.
            slider.IsKeyboardFocusWithinChanged += delegate
            {
                card.BorderBrush = slider.IsKeyboardFocusWithin ? t.Accent : t.RowEdge;
            };

            // A removed row unhooks itself, so the writer's event list does not keep rows that are no
            // longer on screen (EXP14).
            return new RowWidget
            {
                Card = card,
                Detach = delegate { writer.Written -= onWritten; writer.Failed -= onFailed; },
            };
        }

        /// <summary>A row's card, and how to unhook it from the writer when its display goes away.</summary>
        private sealed class RowWidget
        {
            public Border Card;
            public Action Detach;
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
            string settingsFile = null;   // --settings-file <path>: where the settings live (verification)
            ThemePreference preference = ThemePreference.Auto; // --theme auto|light|dark
            bool osGiven = false;         // --os-theme light|dark: stand in for the OS reading
            bool osLight = true;
            bool templates = true;        // --theme-no-templates: the measurement, not the look
            string reportDir = null;      // --theme-report <dir>: render and sample both windows
            TimeSpan? toggleAfter = null; // --theme-toggle-after <sec> <light|dark>: drive a live switch
            bool toggleLight = false;
            bool place = true;            // --no-place: leave the window where Windows puts it (the baseline)
            double placeMargin = Place.DefaultMargin; // --place-margin N: clearance from the corner, in DIPs
            TaskbarEdge? placeEdge = null;   // --place-edge bottom|top|left|right: as if the taskbar were docked there
            double? placeDpi = null;         // --place-dpi N: as if the monitor scaled differently
            PlacementBox? placeWork = null;  // --place-work L,T,R,B: as if the work area were this (pixels)
            bool placeTray = true;            // --place-anchor window: pin the pre-EXP15 anchor (the window's monitor)
            PlacementBox? placeMonitor = null; // --place-monitor L,T,R,B: as if the notification area's monitor were this
            string placeReportDir = null;    // --place-report <dir>: the reading and the computed rectangle
            TimeSpan? placeAfter = null;     // --place-after <sec> L,T,R,B: a work-area change while it runs
            PlacementBox placeAfterWork = PlacementBox.Empty;
            bool tray = true;             // --no-tray: the pre-tray lifecycle - closing the window exits
            bool hidden = false;          // --hidden: start tray-only (what the autostart entry passes)
            bool trayMenu = false;        // --tray-menu: open the tray menu at once (verification)
            string startupValue = StartupEntry.DefaultName;  // --startup-value <name>: a throwaway Run value name
            string trayReportDir = null;  // --tray-report <dir>: the app's own tray reading
            string iconReportDir = null;  // --icon-report <dir>: the app's own icon reading (EXP12)
            TimeSpan? quitAfter = null;   // --quit-after <sec>: run the real Quit after N seconds (verification)
            string monitorsSpec = null;   // --monitors <spec>: a scripted display set (EXP14, verification)
            TimeSpan? displayAfter = null; // --display-change-after <sec> <spec>: a scripted display change
            string displayAfterSpec = null;
            string displayReportDir = null; // --display-report <dir>: the app's own display reading (EXP14)

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
                else if (a == "--settings-file" && i + 1 < args.Length) settingsFile = args[++i];
                else if (a == "--theme" && i + 1 < args.Length)
                {
                    if (!ThemeChoice.TryParsePreference(args[++i], out preference))
                        throw new ArgumentException("--theme takes auto, light or dark");
                }
                else if (a == "--os-theme" && i + 1 < args.Length) { osGiven = true; SetOsTheme(args[++i], out osLight); }
                else if (a == "--theme-no-templates") templates = false;
                else if (a == "--theme-report" && i + 1 < args.Length) reportDir = args[++i];
                else if (a == "--theme-toggle-after" && i + 2 < args.Length)
                {
                    toggleAfter = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
                    SetOsTheme(args[++i], out toggleLight);
                }
                else if (a == "--no-place") place = false;
                else if (a == "--place-margin" && i + 1 < args.Length) placeMargin = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--place-edge" && i + 1 < args.Length)
                {
                    TaskbarEdge edge;
                    if (!PlacementGeometry.TryParseEdge(args[++i], out edge))
                        throw new ArgumentException("--place-edge takes bottom, top, left or right");
                    placeEdge = edge;
                }
                else if (a == "--place-dpi" && i + 1 < args.Length) placeDpi = double.Parse(args[++i], CultureInfo.InvariantCulture);
                else if (a == "--place-work" && i + 1 < args.Length)
                {
                    PlacementBox box;
                    if (!PlacementGeometry.TryParseBox(args[++i], out box))
                        throw new ArgumentException("--place-work takes L,T,R,B in whole pixels");
                    placeWork = box;
                }
                else if (a == "--place-anchor" && i + 1 < args.Length)
                {
                    PlacementAnchor anchor;
                    if (!PlacementChoice.TryParseAnchor(args[++i], out anchor))
                        throw new ArgumentException("--place-anchor takes tray or window");
                    placeTray = anchor == PlacementAnchor.Tray;
                }
                else if (a == "--place-monitor" && i + 1 < args.Length)
                {
                    PlacementBox box;
                    if (!PlacementGeometry.TryParseBox(args[++i], out box))
                        throw new ArgumentException("--place-monitor takes L,T,R,B in whole pixels");
                    placeMonitor = box;
                }
                else if (a == "--place-report" && i + 1 < args.Length) placeReportDir = args[++i];
                else if (a == "--place-after" && i + 2 < args.Length)
                {
                    placeAfter = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
                    if (!PlacementGeometry.TryParseBox(args[++i], out placeAfterWork))
                        throw new ArgumentException("--place-after takes a number of seconds and L,T,R,B in whole pixels");
                }
                else if (a == "--no-tray") tray = false;
                else if (a == "--hidden") hidden = true;
                else if (a == "--tray-menu") trayMenu = true;
                else if (a == "--startup-value" && i + 1 < args.Length) startupValue = args[++i];
                else if (a == "--tray-report" && i + 1 < args.Length) trayReportDir = args[++i];
                else if (a == "--icon-report" && i + 1 < args.Length) iconReportDir = args[++i];
                else if (a == "--quit-after" && i + 1 < args.Length) quitAfter = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
                else if (a == "--monitors" && i + 1 < args.Length) monitorsSpec = args[++i];
                else if (a == "--display-change-after" && i + 2 < args.Length)
                {
                    displayAfter = TimeSpan.FromSeconds(double.Parse(args[++i], CultureInfo.InvariantCulture));
                    displayAfterSpec = args[++i];
                }
                else if (a == "--display-report" && i + 1 < args.Length) displayReportDir = args[++i];
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

            // --- the theme: what Windows says, with the command line on top ---
            // --os-theme stands in for the OS reading, the same way --now stands in for the clock, so
            // a verifier can drive both themes without touching the user's real Windows theme. Without
            // it the registry is read, and an unreadable key means light - the app's historical look.
            bool[] scripted = new bool[] { osGiven ? osLight : (SystemTheme.ReadAppThemeLight() ?? true) };
            bool followScript = osGiven || toggleAfter.HasValue;
            Func<bool> osReader = followScript
                ? (Func<bool>)delegate { return scripted[0]; }
                : delegate { bool? read = SystemTheme.ReadAppThemeLight(); return read.HasValue ? read.Value : true; };
            var theme = new ThemeState(preference, osReader, templates);

            if (toggleAfter.HasValue)
            {
                // The live path, driven: flip what the OS reader returns and re-resolve, exactly what
                // a WM_SETTINGCHANGE from Windows would do (SystemTheme.Follow calls Refresh too).
                DispatcherTimer toggle = new DispatcherTimer { Interval = toggleAfter.Value };
                toggle.Tick += delegate { toggle.Stop(); scripted[0] = toggleLight; theme.Refresh(); };
                toggle.Start();
            }

            // --- the settings the window edits and the schedule reads: one object, loaded from disk ---
            // The file is read first; the command-line seams are applied on top of it and are NEVER
            // saved, so a verifier's --strength/--on/--off cannot rewrite the user's settings.
            var store = new SettingsStore(settingsFile);
            ScheduleSettings settings = store.Load();
            SetWindow(settings, turnOn, turnOff);
            if (strength.HasValue) settings.TrySetStrength(strength.Value.ToString(CultureInfo.InvariantCulture));
            if (!schedule) settings.SetEnabled(false);

            if (settingsOnly && reportDir == null)
            {
                // A real edit in the settings window is written straight back to the file.
                settings.Changed += delegate { store.Save(settings); };
                app.Run(SettingsSurface.Build(settings, theme));
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

            // Real detection: active displays from the hardware - unless a verifier scripted them
            // (--monitors), the seam a display change is driven through without touching the user's
            // displays. EXP14: detection is a machine the app re-runs, not a launch-only read.
            IBrightnessController controller;
            IDisposable controllerResources = null;
            ScriptedController scriptedController = null;
            if (monitorsSpec != null) { scriptedController = new ScriptedController(monitorsSpec); controller = scriptedController; }
            else { var hardware = new BrightnessController(); controller = hardware; controllerResources = hardware; }

            var writer = new BrightnessWriter(controller, Settle, clock);
            var scheduler = new NightLightScheduler(settings, clock,
                fade.HasValue ? fade.Value : Fade, step.HasValue ? step.Value : Step);
            var registry = new DisplayRegistry(controller, writer, scheduler);
            IList<MonitorInfo> monitors = registry.Seed();   // adopt every display: a row, a seed, an observe

            var host = new SettingsHost(settings, theme);
            SurfaceView view = SurfaceView.Build(monitors, writer, scheduler, theme, width, delegate { host.Show(); });
            Window window = view.Shell;

            // The app's own icon (EXP12): the generated Monitron.ico, embedded in the exe, on the
            // window as well as in the tray - and on the --no-tray lifecycle too, where nothing else
            // would set it. A missing asset leaves the stock icon rather than failing anything.
            try { window.Icon = IconAssets.Window(); }
            catch { /* an icon is a nicety, never a reason to fail */ }

            if (reportDir != null)
            {
                // Report mode must not touch a monitor: no scheduler timer, no restore, no writes.
                // It runs the surface window for its pixels only, and a settings window beside it.
                double seconds = toggleAfter.HasValue ? toggleAfter.Value.TotalSeconds + 1.5 : 2.0;
                ThemeReport.Run(app, theme, reportDir, window, SettingsSurface.Build(settings, theme), seconds);
                return;
            }

            // --- the position: the notification area's corner of the monitor the tray is on (EXP15) ---
            // The reading is injectable (--place-edge / --place-dpi / --place-work / --place-monitor /
            // --place-anchor) so a verifier can drive another taskbar edge, DPI, work area or tray
            // monitor without moving the user's taskbar or displays; --no-place is the baseline
            // everything else is measured against.
            Action replace = null;   // kept so showing the surface from the tray can re-place it
            if (place)
            {
                var placeScript = new PlacementScript { Edge = placeEdge, Dpi = placeDpi, WorkArea = placeWork, Monitor = placeMonitor };
                bool preferTray = placeTray;
                Func<IntPtr, PlacementReading> placementReading = delegate(IntPtr hwnd) { return SystemPlacement.Read(hwnd, preferTray); };
                if (placeScript.Any || placeAfter.HasValue)
                {
                    placementReading = delegate(IntPtr hwnd) { return placeScript.Apply(SystemPlacement.Read(hwnd, preferTray)); };
                }
                replace = Place.Attach(window, placementReading, placeMargin, placeReportDir);

                if (placeAfter.HasValue)
                {
                    // The work area changing under a running window, driven: the same re-place a
                    // WM_SETTINGCHANGE from Windows would call, on a clock like --now's.
                    DispatcherTimer move = new DispatcherTimer { Interval = placeAfter.Value };
                    move.Tick += delegate
                    {
                        move.Stop();
                        placeScript.WorkArea = placeAfterWork;   // the reading changes, and the window must follow it
                        replace();
                    };
                    move.Start();
                }
            }

            // A real edit is written to the file at once; a failed write is surfaced on the status
            // line rather than thrown, and the app keeps running.
            string saveError = null;
            settings.Changed += delegate
            {
                saveError = store.Save(settings) ? null : store.LastError;
            };

            // --- EXP14: the display set is live. A display change re-reads it and reconciles the
            // rows, the writer and the schedule; the tray's Show does the same (below). ---
            Action refreshDisplays = delegate
            {
                DisplayDiff diff = registry.Refresh();
                foreach (string id in diff.Removed) view.Remove(id);
                foreach (MonitorInfo monitor in diff.Added) view.Add(monitor);
                WriteDisplayReport(displayReportDir, registry, view);
                if (diff.Any)
                {
                    Apply(scheduler, writer);   // an arriving display takes the current phase at once
                    view.Status.Text = StatusText(scheduler, saveError);
                }
            };
            DisplayWatcher.Attach(window, refreshDisplays);
            WriteDisplayReport(displayReportDir, registry, view);

            if (displayAfter.HasValue)
            {
                // A display change, driven: the same re-read a WM_DISPLAYCHANGE calls, on a clock like
                // --now's. With --monitors it switches the scripted set, so a row really comes and goes.
                DispatcherTimer change = new DispatcherTimer { Interval = displayAfter.Value };
                change.Tick += delegate
                {
                    change.Stop();
                    if (scriptedController != null) scriptedController.Set(displayAfterSpec);
                    refreshDisplays();
                };
                change.Start();
            }

            // One pass at launch, so a run started inside the night window dims at once.
            Apply(scheduler, writer);
            view.Status.Text = StatusText(scheduler, saveError);

            // One timer drives both machines: the scheduler decides what it wants (and dedups it),
            // the writer decides when it lands. 200 ms keeps a slider drag responsive.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            timer.Tick += delegate
            {
                Apply(scheduler, writer);
                view.Status.Text = StatusText(scheduler, saveError);
            };
            timer.Start();

            // --- the tray shell (EXP11): the icon, its menu, and what closing the window now means ---
            // The tray attaches to the full surface, not to the --settings spike. --no-tray restores
            // the pre-tray lifecycle (closing exits), which the older verify scripts still pass.
            bool trayOn = tray && !settingsOnly;
            var trayModel = new TrayModel(!hidden);

            bool quitting = false;
            bool cleanedUp = false;
            Action cleanup = delegate
            {
                if (cleanedUp) return;
                cleanedUp = true;
                timer.Stop();
                foreach (NightWrite w in scheduler.Restore()) writer.Request(w.Id, w.Value);
                writer.Flush();   // land the last value if the window closes mid-drag or mid-fade
                if (controllerResources != null) controllerResources.Dispose();
            };

            TrayIcon icon = null;
            Action quit = delegate
            {
                quitting = true;
                cleanup();
                if (icon != null) icon.Remove();
                app.Shutdown();
            };

            Action show = delegate
            {
                // EXP14: opening reconciles the displays - they may have changed while it was hidden.
                refreshDisplays();
                if (!window.IsVisible) window.Show();
                if (replace != null) replace();   // the work area may have moved while it was hidden
                window.Activate();
                trayModel.Show();
            };
            Action hide = delegate
            {
                window.Hide();
                trayModel.Hide();
            };
            Action toggleVisibility = delegate { if (trayModel.Visible) hide(); else show(); };

            if (trayOn)
            {
                // With the surface able to hide, closing it must not end the app: Quit is the only
                // exit, and the schedule keeps running with no window open. OnExplicitShutdown is that.
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.MainWindow = window;   // the settings window still centres on the surface

                var startupStore = new RegistryStartup();
                string exePath = System.Reflection.Assembly.GetEntryAssembly().Location;
                Action toggleStartup = delegate
                {
                    if (AutoStart.IsEnabled(startupStore, startupValue, exePath)) AutoStart.Disable(startupStore, startupValue);
                    else AutoStart.Enable(startupStore, startupValue, exePath);
                };

                icon = TrayIcon.Attach(window, theme, trayModel, startupStore, startupValue, exePath,
                    toggleVisibility, toggleStartup, quit, trayReportDir);

                if (icon != null && !icon.Added)
                {
                    // The shell refused the icon. Without a tray there is no way back into a hidden
                    // window and no way out, so fall back to the ordinary window lifecycle.
                    icon.Remove();
                    icon = null;
                    trayOn = false;
                    app.ShutdownMode = ShutdownMode.OnMainWindowClose;
                }
                else
                {
                    // A logoff or shutdown must give the displays back, exactly as Quit does.
                    app.SessionEnding += delegate { if (quit != null) quit(); };
                }
            }

            // --- the app's own icon reading (EXP12): which asset it carries, and where it landed ---
            if (iconReportDir != null) WriteIconReport(iconReportDir, window, trayOn && icon != null && icon.Added);

            window.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (trayOn && !quitting)
                {
                    // The X hides to the tray; the schedule keeps dimming with no window open.
                    e.Cancel = true;
                    hide();
                    return;
                }
                cleanup();
            };

            if (trayOn)
            {
                if (quitAfter.HasValue)
                {
                    // The real Quit, after a delay: what a verifier uses to tear a run down cleanly.
                    var stop = new DispatcherTimer { Interval = quitAfter.Value };
                    stop.Tick += delegate { stop.Stop(); quit(); };
                    stop.Start();
                }
                if (trayMenu)
                {
                    // A right click, scripted: the shell needs a moment before the icon is registered.
                    var openMenu = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    openMenu.Tick += delegate { openMenu.Stop(); icon.ShowMenu(); };
                    openMenu.Start();
                }

                if (!hidden) show();
                app.Run();
            }
            else
            {
                app.Run(window);
            }
        }

        /// <summary>Asks the schedule what it wants and hands it to the writer, which debounces it.</summary>
        private static void Apply(NightLightScheduler scheduler, BrightnessWriter writer)
        {
            foreach (NightWrite w in scheduler.Advance()) writer.Request(w.Id, w.Value);
            writer.Advance();
        }

        /// <summary>The schedule's line, plus a note if the settings could not be written.</summary>
        private static string StatusText(NightLightScheduler scheduler, string saveError)
        {
            if (string.IsNullOrEmpty(saveError)) return scheduler.Status;
            return scheduler.Status + "\nSettings not saved: " + saveError;
        }

        /// <summary>
        /// The app's own icon reading (--icon-report): the asset it carries and the frames inside it,
        /// whether the window got it, and whether the shell took the tray icon. The outside witnesses
        /// (the shell's icon for the exe, Win32's icon for the window) are the verifier's; this is
        /// only what the app believes.
        /// </summary>
        private static void WriteIconReport(string dir, Window window, bool trayIcon)
        {
            var text = new System.Text.StringBuilder();
            text.Append("icon.report=1").Append('\n');
            text.Append("resource=").Append(IconAssets.Embedded ? IconAssets.ResourceName : "missing").Append('\n');

            var sizes = new System.Collections.Generic.List<string>();
            foreach (int size in IconAssets.FrameSizes()) sizes.Add(size.ToString(CultureInfo.InvariantCulture));
            text.Append("frames=").Append(string.Join(",", sizes.ToArray())).Append('\n');
            text.Append("windowIcon=").Append(window.Icon == null ? "false" : "true").Append('\n');
            text.Append("trayIcon=").Append(trayIcon ? "true" : "false").Append('\n');

            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "icon.txt");
                string temp = path + ".tmp";
                System.IO.File.WriteAllText(temp, text.ToString());
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                else System.IO.File.Move(temp, path);
            }
            catch { /* a report is evidence, never a reason to fail */ }
        }

        /// <summary>
        /// The app's own display reading (--display-report): how many times the displays were read,
        /// what was detected, and the rows on screen. The outside witness is UI Automation's row set;
        /// this is only what the app believes.
        /// </summary>
        private static void WriteDisplayReport(string dir, DisplayRegistry registry, SurfaceView view)
        {
            if (string.IsNullOrEmpty(dir)) return;

            var text = new System.Text.StringBuilder();
            text.Append("display.report=1").Append('\n');
            text.Append("detections=").Append(registry.Detections.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("detected=").Append(Join(registry.Ids)).Append('\n');
            text.Append("rows=").Append(Join(view.Ids)).Append('\n');

            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, "display.txt");
                string temp = path + ".tmp";
                System.IO.File.WriteAllText(temp, text.ToString());
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                else System.IO.File.Move(temp, path);
            }
            catch { /* a report is evidence, never a reason to fail */ }
        }

        private static string Join(IList<string> ids)
        {
            var text = new System.Text.StringBuilder();
            foreach (string id in ids)
            {
                if (text.Length > 0) text.Append(';');
                text.Append(id);
            }
            return text.ToString();
        }

        /// <summary>"light" | "dark" for the OS reading, from a command line.</summary>
        private static void SetOsTheme(string text, out bool light)
        {
            ThemeMode mode;
            if (!ThemeChoice.TryParseMode(text, out mode))
                throw new ArgumentException("--os-theme takes light or dark: " + text);
            light = mode == ThemeMode.Light;
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
