// EXP04 - the headless model behind the dummy "Schedule night light" settings window.
//
// No WPF, no clock, no I/O: pure state plus transitions, so it can be driven and asserted
// without a UI (see ScheduleSettingsTests.cs, run by model-test.ps1). The window is a view
// over this object and holds no logic of its own beyond formatting.
//
// C# 5 only - the in-box csc.exe (.NET Framework 4.0 compiler) is what builds this.

using System;
using System.Globalization;

namespace Monitron.App
{
    internal enum ScheduleMode
    {
        SunsetToSunrise,
        SetHours,
    }

    internal sealed class ScheduleSettings
    {
        public const int StrengthMin = 0;
        public const int StrengthMax = 100;

        /// <summary>Dummy range: a real one needs a location, which this experiment does not have.</summary>
        public const string SunsetToSunriseRange = "12:00 AM - 12:00 AM";

        public ScheduleSettings()
        {
            Enabled = true;
            Strength = 38;                  // the sample value from the sketch
            GradualFade = true;
            Mode = ScheduleMode.SetHours;   // the sketch supplies turn-on/off times, so they start live
            TurnOn = new TimeSpan(17, 0, 0);    // 5:00 PM
            TurnOff = new TimeSpan(9, 0, 0);    // 9:00 AM
        }

        // --- state ---
        public bool Enabled { get; private set; }
        public int Strength { get; private set; }
        public bool GradualFade { get; private set; }
        public ScheduleMode Mode { get; private set; }
        public TimeSpan TurnOn { get; private set; }
        public TimeSpan TurnOff { get; private set; }

        /// <summary>
        /// Raised after a transition that actually changed the state - never for a refused value
        /// (an out-of-range strength, a bad clock) and never for a no-op set of the value already
        /// held. EXP07's persistence seams onto this, so a write cannot be triggered by the window
        /// re-rendering itself with the value it already has.
        /// </summary>
        public event Action Changed;

        private void RaiseChanged()
        {
            if (Changed != null) Changed();
        }

        // --- derived: exactly what the window enables, per feature state and mode ---
        public bool BodyEnabled { get { return Enabled; } }

        /// <summary>
        /// The window the scheduler works with. "Set hours" supplies the real times; "Sunset to
        /// sunrise" has no location yet, so it resolves to turn-on == turn-off - the zero-length
        /// window the scheduler treats as "not configured". EXP04's placeholder made literal.
        /// </summary>
        public void ResolvedWindow(out TimeSpan on, out TimeSpan off)
        {
            if (Mode == ScheduleMode.SetHours)
            {
                on = TurnOn;
                off = TurnOff;
                return;
            }
            on = TimeSpan.Zero;
            off = TimeSpan.Zero;
        }

        public bool SetHoursFieldsEnabled
        {
            get { return Enabled && Mode == ScheduleMode.SetHours; }
        }

        public string SunsetToSunriseLabel
        {
            get { return "Sunset to sunrise (" + SunsetToSunriseRange + ")"; }
        }

        public string TurnOnText { get { return FormatTime(TurnOn); } }
        public string TurnOffText { get { return FormatTime(TurnOff); } }

        // --- transitions ---
        public void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            Enabled = on;
            RaiseChanged();
        }

        public void ToggleEnabled() { SetEnabled(!Enabled); }

        public void ToggleGradualFade()
        {
            GradualFade = !GradualFade;
            RaiseChanged();
        }

        public void SetMode(ScheduleMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            RaiseChanged();
        }

        /// <summary>Strength is an integer percent; anything unparsable is refused and the old value kept.</summary>
        public bool TrySetStrength(string text)
        {
            int value;
            if (text == null) return false;
            if (!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return false;
            if (value < StrengthMin || value > StrengthMax) return false;
            if (Strength == value) return true;      // accepted, but nothing changed
            Strength = value;
            RaiseChanged();
            return true;
        }

        public bool TrySetTurnOn(int hour12, int minute, bool pm)
        {
            TimeSpan value;
            if (!TryClock(hour12, minute, pm, out value)) return false;
            if (TurnOn == value) return true;
            TurnOn = value;
            RaiseChanged();
            return true;
        }

        public bool TrySetTurnOff(int hour12, int minute, bool pm)
        {
            TimeSpan value;
            if (!TryClock(hour12, minute, pm, out value)) return false;
            if (TurnOff == value) return true;
            TurnOff = value;
            RaiseChanged();
            return true;
        }

        /// <summary>12-hour clock: hour 1..12, minute 0..59. Anything else is refused.</summary>
        public static bool TryClock(int hour12, int minute, bool pm, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (hour12 < 1 || hour12 > 12 || minute < 0 || minute > 59) return false;
            int hour = (hour12 == 12) ? 0 : hour12;
            if (pm) hour += 12;
            value = new TimeSpan(hour, minute, 0);
            return true;
        }

        /// <summary>
        /// 24-hour "HH:mm" (what a script or a command line says) into the 12-hour parts the model
        /// takes. One place for the arithmetic, so the verification seams and the tests agree.
        /// </summary>
        public static bool TryParseClock(string text, out int hour12, out int minute, out bool pm)
        {
            hour12 = 12;
            minute = 0;
            pm = false;
            if (text == null) return false;

            string[] parts = text.Trim().Split(':');
            if (parts.Length != 2) return false;

            int hour, min;
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out hour)) return false;
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out min)) return false;
            if (hour < 0 || hour > 23 || min < 0 || min > 59) return false;

            pm = hour >= 12;
            hour12 = hour % 12;
            if (hour12 == 0) hour12 = 12;
            minute = min;
            return true;
        }

        public static string FormatTime(TimeSpan time)
        {
            int hour = time.Hours;
            int hour12 = hour % 12;
            if (hour12 == 0) hour12 = 12;
            return hour12.ToString(CultureInfo.InvariantCulture) + ":" +
                time.Minutes.ToString("00", CultureInfo.InvariantCulture) + " " +
                (hour < 12 ? "AM" : "PM");
        }

        public static void ToParts(TimeSpan time, out int hour12, out int minute, out bool pm)
        {
            int hour = time.Hours;
            hour12 = hour % 12;
            if (hour12 == 0) hour12 = 12;
            minute = time.Minutes;
            pm = hour >= 12;
        }
    }
}
