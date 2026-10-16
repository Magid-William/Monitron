// EXP04 - the "Schedule night light" settings window.
// EXP06 - it is no longer a dummy: the model it edits is the one the scheduler reads, so a change
//         here is applied to the displays while the app runs.
//
// A view over ScheduleSettings and nothing more: every handler calls a model transition and
// then re-renders, so the window holds no rules of its own. EXP07: each transition is written
// back to the settings file by the caller (Program.cs subscribes to the model's Changed event).
// What is still out of scope -
//   * "Sunset to sunrise" shows a fixed range placeholder; there is no location lookup.
//
// Built code-only (no XAML) with the in-box csc.exe, like EXP03. C# 5 only.

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Monitron.App
{
    internal sealed class SettingsSurface
    {
        private readonly ScheduleSettings _model;
        private readonly ThemeState _theme;
        private readonly ThemeBrushes _t;

        private CheckBox _enabledBox;
        private RadioButton _sunsetRadio;
        private RadioButton _hoursRadio;
        private TextBox _strengthBox;
        private TextBox _defaultBox;
        private TextBox _onHour;
        private TextBox _onMinute;
        private TextBox _offHour;
        private TextBox _offMinute;
        private Button _onMeridiem;
        private Button _offMeridiem;
        private StackPanel _body;
        private StackPanel _hoursPanel;
        private TextBlock _hoursNote;

        private bool _rendering;

        private SettingsSurface(ScheduleSettings model, ThemeState theme)
        {
            _model = model;
            _theme = theme;
            _t = theme.Brushes;
        }

        public static Window Build(ScheduleSettings model, ThemeState theme)
        {
            return new SettingsSurface(model, theme).Create();
        }

        private Window Create()
        {
            var root = new StackPanel { Margin = new Thickness(18, 16, 18, 14) };
            // EXP17: the Default brightness sits *outside* the night-light section - it is the level
            // every display is returned to, and it is not something the schedule's own switch gates.
            root.Children.Add(DefaultRow());
            root.Children.Add(Header());

            _body = new StackPanel();
            _body.Children.Add(StrengthRow());
            _body.Children.Add(ModeRow());
            _body.Children.Add(HoursPanel());
            root.Children.Add(_body);

            root.Children.Add(Label(
                "Saved to your settings file and applied while Monitron is running.",
                11, _t.Muted, FontWeights.Normal, new Thickness(0, 14, 0, 0)));

            var window = new Window
            {
                Title = "Monitron settings",
                Width = 380,
                SizeToContent = SizeToContent.Height,
                MinWidth = 320,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                // Standalone (--settings) it is its own main window; the host overrides this to
                // CenterOwner when it is opened from a gear.
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = _t.WindowBg,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                Content = root,
            };

            Render();
            Theming.Bind(window, _theme);   // the palette, and the templates the OS theme would keep light
            // EXP09: the surface now sits in the bottom-right corner, so an owner-centred settings window
            // would hang off the screen - keep it inside the work area (it is not resized, only moved).
            Place.Clamp(window, delegate(IntPtr hwnd) { return SystemPlacement.Read(hwnd, true); });
            return window;
        }

        // --- sections ---

        /// <summary>Section title with the feature's own on/off switch at the right.</summary>
        private UIElement Header()
        {
            var title = Label("Schedule night light", 15, _t.Ink, FontWeights.SemiBold, new Thickness(0));
            title.VerticalAlignment = VerticalAlignment.Center;

            _enabledBox = new CheckBox
            {
                Content = "On",
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "Turn the night-light schedule on or off",
            };
            _enabledBox.Checked += delegate { _model.SetEnabled(true); Render(); };
            _enabledBox.Unchecked += delegate { _model.SetEnabled(false); Render(); };

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(title, 0);
            Grid.SetColumn(_enabledBox, 1);
            grid.Children.Add(title);
            grid.Children.Add(_enabledBox);
            return grid;
        }

        private UIElement StrengthRow()
        {
            _strengthBox = new TextBox
            {
                Width = 46,
                MaxLength = 3,
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Night-light strength, 0-100 percent",
            };
            _strengthBox.TextChanged += delegate { CommitStrength(); };
            _strengthBox.LostFocus += delegate { Render(); };   // leaving the field canonicalises or reverts it
            _strengthBox.KeyDown += OnCommitKey;

            var percent = Label("%", 13, _t.Ink, FontWeights.Normal, new Thickness(8, 0, 0, 0));
            percent.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var caption = Label("Strength", 13, _t.Ink, FontWeights.Normal, new Thickness(0));
            caption.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(caption, 0);
            Grid.SetColumn(_strengthBox, 1);
            Grid.SetColumn(percent, 2);
            grid.Children.Add(caption);
            grid.Children.Add(_strengthBox);
            grid.Children.Add(percent);
            return grid;
        }

        /// <summary>
        /// EXP17: the Default brightness, above the night-light section - the level the schedule
        /// returns each display to. It sits outside the section's On switch, so it is always editable.
        /// </summary>
        private UIElement DefaultRow()
        {
            _defaultBox = new TextBox
            {
                Width = 46,
                MaxLength = 3,
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Default brightness: the level the schedule returns each display to, 0-100 percent",
            };
            _defaultBox.TextChanged += delegate { CommitDefault(); };
            _defaultBox.LostFocus += delegate { Render(); };    // leaving the field canonicalises or reverts it
            _defaultBox.KeyDown += OnCommitKey;

            var percent = Label("%", 13, _t.Ink, FontWeights.Normal, new Thickness(8, 0, 0, 0));
            percent.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var caption = Label("Default:", 13, _t.Ink, FontWeights.Normal, new Thickness(0));
            caption.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(caption, 0);
            Grid.SetColumn(_defaultBox, 1);
            Grid.SetColumn(percent, 2);
            grid.Children.Add(caption);
            grid.Children.Add(_defaultBox);
            grid.Children.Add(percent);
            return grid;
        }

        /// <summary>The two timing options: the fixed sunset range, or explicit hours.</summary>
        private UIElement ModeRow()
        {
            _sunsetRadio = new RadioButton
            {
                Content = _model.SunsetToSunriseLabel,
                GroupName = "Timing",
                Margin = new Thickness(0, 14, 0, 0),
                Cursor = Cursors.Hand,
            };
            _sunsetRadio.Checked += delegate
            {
                if (_model.Mode != ScheduleMode.SunsetToSunrise)
                {
                    _model.SetMode(ScheduleMode.SunsetToSunrise);
                    Render();
                }
            };
            _sunsetRadio.ToolTip = "The range comes from a location; this dummy has none yet";

            _hoursRadio = new RadioButton
            {
                Content = "Set hours",
                GroupName = "Timing",
                Margin = new Thickness(0, 8, 0, 0),
                Cursor = Cursors.Hand,
            };
            _hoursRadio.Checked += delegate
            {
                if (_model.Mode != ScheduleMode.SetHours)
                {
                    _model.SetMode(ScheduleMode.SetHours);
                    Render();
                }
            };

            var panel = new StackPanel();
            panel.Children.Add(_sunsetRadio);
            panel.Children.Add(_hoursRadio);
            return panel;
        }

        private UIElement HoursPanel()
        {
            _hoursPanel = new StackPanel { Margin = new Thickness(22, 10, 0, 0) };
            _hoursPanel.Children.Add(TimeRow("Turn on", true, out _onHour, out _onMinute, out _onMeridiem));

            Grid off = TimeRow("Turn off", false, out _offHour, out _offMinute, out _offMeridiem);
            off.Margin = new Thickness(0, 8, 0, 0);
            _hoursPanel.Children.Add(off);

            _hoursNote = Label(
                "These times are kept but not used while \"Sunset to sunrise\" is selected.",
                11, _t.Muted, FontWeights.Normal, new Thickness(0, 8, 0, 0));
            _hoursPanel.Children.Add(_hoursNote);
            return _hoursPanel;
        }

        /// <summary>One 12-hour clock: hour, colon, minute, AM/PM toggle.</summary>
        private Grid TimeRow(string caption, bool isOn, out TextBox hour, out TextBox minute, out Button meridiem)
        {
            var label = Label(caption, 13, _t.Ink, FontWeights.Normal, new Thickness(0));
            label.VerticalAlignment = VerticalAlignment.Center;

            hour = ClockBox();
            minute = ClockBox();
            hour.ToolTip = "Hour, 1-12";
            minute.ToolTip = "Minute, 00-59";

            var colon = Label(":", 13, _t.Muted, FontWeights.Normal, new Thickness(3, 0, 3, 0));
            colon.VerticalAlignment = VerticalAlignment.Center;

            meridiem = new Button
            {
                Width = 46,
                Height = 25,
                Padding = new Thickness(0),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                Background = _t.ControlFill,
                BorderBrush = _t.ControlEdge,
                BorderThickness = new Thickness(1),
                ToolTip = "Switch between AM and PM",
            };
            meridiem.Click += delegate { FlipMeridiem(isOn); };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            UIElement[] cells = new UIElement[] { label, hour, colon, minute, meridiem };
            for (int i = 0; i < cells.Length; i++)
            {
                Grid.SetColumn(cells[i], i);
                grid.Children.Add(cells[i]);
            }
            return grid;
        }

        private TextBox ClockBox()
        {
            var box = new TextBox
            {
                Width = 34,
                MaxLength = 2,
                TextAlignment = TextAlignment.Right,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            box.TextChanged += delegate { CommitTimes(); };
            box.LostFocus += delegate { Render(); };            // leaving the field canonicalises or reverts it
            box.KeyDown += OnCommitKey;
            return box;
        }

        // --- transitions, all delegated to the model ---

        /// <summary>Enter canonicalises what is on screen; leaving a field does the same.</summary>
        private void OnCommitKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) Render();
        }

        /// <summary>
        /// Commits as the text changes, and does not re-render: rewriting the text while the user
        /// types would fight them (clearing the field would refill it). Anything unparsable simply
        /// leaves the model alone; the next Render - on leaving the field - puts the model's value
        /// back on screen. Committing here rather than on LostFocus also means UI automation can
        /// drive these fields without the window holding the OS foreground.
        /// </summary>
        private void CommitStrength()
        {
            if (_rendering) return;
            _model.TrySetStrength(_strengthBox.Text);
        }

        private void CommitDefault()
        {
            if (_rendering) return;
            _model.TrySetDaylight(_defaultBox.Text);
        }

        private void CommitTimes()
        {
            if (_rendering) return;

            {
                int hour, minute;
                if (int.TryParse(_onHour.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
                    && int.TryParse(_onMinute.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute))
                {
                    _model.TrySetTurnOn(hour, minute, MeridiemOf(true));
                }
            }
            {
                int hour, minute;
                if (int.TryParse(_offHour.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)
                    && int.TryParse(_offMinute.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out minute))
                {
                    _model.TrySetTurnOff(hour, minute, MeridiemOf(false));
                }
            }
        }

        private bool MeridiemOf(bool isOn)
        {
            TimeSpan time = isOn ? _model.TurnOn : _model.TurnOff;
            int hour12, minute; bool pm;
            ScheduleSettings.ToParts(time, out hour12, out minute, out pm);
            return pm;
        }

        private void FlipMeridiem(bool isOn)
        {
            TimeSpan time = isOn ? _model.TurnOn : _model.TurnOff;
            int hour12, minute; bool pm;
            ScheduleSettings.ToParts(time, out hour12, out minute, out pm);
            if (isOn) _model.TrySetTurnOn(hour12, minute, !pm);
            else _model.TrySetTurnOff(hour12, minute, !pm);
            Render();
        }

        /// <summary>
        /// Push model state onto the controls; guards its own events. Assigning IsChecked raises
        /// Checked/Unchecked, so a handler can land back here - that nested call must be a no-op,
        /// otherwise its finally block would clear the guard while the outer render is still going.
        /// </summary>
        private void Render()
        {
            if (_rendering) return;
            _rendering = true;
            try
            {
                _enabledBox.IsChecked = _model.Enabled;
                _strengthBox.Text = _model.Strength.ToString(CultureInfo.InvariantCulture);
                _defaultBox.Text = _model.Daylight.ToString(CultureInfo.InvariantCulture);
                _sunsetRadio.IsChecked = _model.Mode == ScheduleMode.SunsetToSunrise;
                _hoursRadio.IsChecked = _model.Mode == ScheduleMode.SetHours;

                Show(_model.TurnOn, _onHour, _onMinute, _onMeridiem);
                Show(_model.TurnOff, _offHour, _offMinute, _offMeridiem);

                // The coupling this experiment exists to show: the body follows the On switch,
                // and the time fields additionally follow the mode.
                _body.IsEnabled = _model.BodyEnabled;
                _body.Opacity = _model.BodyEnabled ? 1.0 : 0.5;

                bool hours = _model.SetHoursFieldsEnabled;
                _hoursPanel.IsEnabled = hours;
                _hoursPanel.Opacity = hours ? 1.0 : 0.5;

                bool explain = _model.BodyEnabled && _model.Mode == ScheduleMode.SunsetToSunrise;
                _hoursNote.Visibility = explain ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _rendering = false;
            }
        }

        private static void Show(TimeSpan time, TextBox hour, TextBox minute, Button meridiem)
        {
            int hour12, minuteValue; bool pm;
            ScheduleSettings.ToParts(time, out hour12, out minuteValue, out pm);
            hour.Text = hour12.ToString(CultureInfo.InvariantCulture);
            minute.Text = minuteValue.ToString("00", CultureInfo.InvariantCulture);
            meridiem.Content = pm ? "PM" : "AM";
        }

        // --- shared look: the one palette both windows draw from (EXP08, Theme.cs) ---

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
    }

    /// <summary>
    /// Keeps the one settings window alive for the app's lifetime (modeless, single instance).
    /// EXP06: the model is injected - the same object the scheduler reads - so an edit here takes
    /// effect on the displays on the next tick.
    /// </summary>
    internal sealed class SettingsHost
    {
        private readonly ScheduleSettings _model;
        private readonly ThemeState _theme;
        private Window _window;

        public SettingsHost(ScheduleSettings model, ThemeState theme)
        {
            _model = model;
            _theme = theme;
        }

        public void Show()
        {
            if (_window == null)
            {
                _window = SettingsSurface.Build(_model, _theme);
                _window.Closed += delegate { _window = null; };
            }

            if (!_window.IsVisible)
            {
                if (Application.Current != null && Application.Current.MainWindow != null
                    && !object.ReferenceEquals(Application.Current.MainWindow, _window))
                {
                    _window.Owner = Application.Current.MainWindow;
                    _window.WindowStartupLocation = WindowStartupLocation.CenterOwner;  // not the OS default cascade
                }
                _window.Show();
            }
            _window.Activate();
        }
    }
}
