// EXP06 - the night-light schedule as a state machine.
// EXP17 - the day value is *configured* (Settings.Daylight), not read off the panel: a display the
//         app meets while it is already dimmed no longer comes back dim. A move in the Day phase has
//         no clock boundary to anchor it, so it carries its own ramp (per display) and still steps at
//         most once per `step`. A slider drag stays the user's own day value for that display.
//
// Inputs: the clock and ScheduleSettings. Output: the brightness values the schedule wants the
// displays to have *now*, as a list of writes the caller hands to BrightnessWriter. There is no
// WPF, no P/Invoke and no timer here on purpose - the caller supplies `now` and the tick, so the
// headless test (ScheduleTests.cs, run by schedule-test.ps1) drives a whole night by moving a clock
// and never waits. The hardware stays in BrightnessController.cs.
//
// The phase is *derived from the clock*, not accumulated: an Advance() that lands well past a
// boundary computes the same phase as one that walked there tick by tick. The carried state is only
// what the clock cannot tell us - the reading each display had when we took it over, the user's own
// day value if a slider set one, the last value we asked for, and whether the user has taken a display
// over for this night. EXP17 adds one more: a move in the day (or with the feature off) has no boundary
// to anchor it, so it carries its own ramp, step by step, instead of jumping.
//
// C# 5 only - the in-box csc.exe (.NET Framework 4.0 compiler) is what builds this.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitron.App
{
    internal enum NightPhase
    {
        Day,        // inside the day window, not dimming
        FadingIn,   // just after turn-on: stepping down to the night level
        Night,      // at the night level
        FadingOut,  // just after turn-off: stepping back up to the day value
        Off,        // the feature's own switch is off
        Inert,      // no usable window (turn-on == turn-off), e.g. sunset mode without a location
    }

    /// <summary>One value the schedule wants written: which display, and what.</summary>
    internal sealed class NightWrite
    {
        public NightWrite(string id, int value) { Id = id; Value = value; }

        public string Id;
        public int Value;
    }

    /// <summary>
    /// The schedule machine. Rules:
    ///   * Advance() recomputes the phase from the clock and returns only the displays whose
    ///     wanted value changed - so ticking often is free and a fade costs at most `fade/step`
    ///     writes per display.
    ///   * The night level is the strength scaled into the display's own range, and the day value is
    ///     the configured daylight scaled the same way (EXP17) unless the user set this display's own.
    ///   * Request() is the *user* moving a slider: it becomes the day value, and inside the night
    ///     window it ends the schedule for that display for that night (nothing is written until
    ///     the next turn-on).
    ///   * Restore() hands every display its day value back; it is what the app calls on close.
    /// </summary>
    internal sealed class NightLightScheduler
    {
        private sealed class Entry
        {
            public int Min;
            public int Max;
            public bool HasAdopted;
            public int Adopted;             // the reading the display held when we took it over (recorded)
            public bool HasDayValue;        // EXP17: a slider drag set this display's own day value
            public int DayValue;
            public bool HasLast;
            public int Last;                // the last value the schedule asked for (dedup)
            public bool Overridden;
            public DateTimeOffset OverrideNight;

            // EXP17: a move in the Day phase has no clock boundary to anchor it, so the ramp carries
            // when it began and from what value (and where it is heading, so an edit re-anchors it).
            public bool HasDayRamp;
            public int DayRampFrom;
            public DateTimeOffset DayRampStart;
            public int DayRampTo;

            // EXP17: what the night began from, so the fade down starts where the day left off even if
            // the day's own ramp was still running when the window opened.
            public bool HasNightFrom;
            public int NightFrom;
        }

        private readonly ScheduleSettings _settings;
        private readonly Func<DateTimeOffset> _now;
        private readonly TimeSpan _fade;
        private readonly TimeSpan _step;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        private NightPhase _phase = NightPhase.Day;
        private bool _engaged;              // the schedule has taken the displays over

        public NightLightScheduler(ScheduleSettings settings, Func<DateTimeOffset> now, TimeSpan fade, TimeSpan step)
        {
            _settings = settings;
            _now = now;
            _fade = fade > TimeSpan.Zero ? fade : TimeSpan.Zero;
            _step = step > TimeSpan.Zero ? step : TimeSpan.FromMinutes(1);
        }

        /// <summary>The phase as of the last Advance().</summary>
        public NightPhase Phase { get { return _phase; } }

        /// <summary>True while the schedule owns the displays (fading in, at night, or fading back).</summary>
        public bool Engaged { get { return _engaged; } }

        /// <summary>One line for the surface: the phase and what it means right now.</summary>
        public string Status
        {
            get
            {
                TimeSpan on, off;
                _settings.ResolvedWindow(out on, out off);
                switch (_phase)
                {
                    case NightPhase.Off:
                        return "Night light: off";
                    case NightPhase.Inert:
                        return "Night light: not configured - set the hours (sunset needs a location)";
                    case NightPhase.FadingIn:
                        return "Night light: fading to " + StrengthText() + "%";
                    case NightPhase.Night:
                        return "Night light: " + StrengthText() + "% until " + ScheduleSettings.FormatTime(off);
                    case NightPhase.FadingOut:
                        return "Night light: returning to daytime brightness";
                    default:
                        return "Night light: daylight - dims to " + StrengthText() + "% at " + ScheduleSettings.FormatTime(on);
                }
            }
        }

        /// <summary>
        /// A display was detected, with its real value. EXP17: the reading is recorded as what the
        /// display held when we took it over - it is *not* the day value, which is configured
        /// (Settings.Daylight) unless the user has set this display's own. This is what stops a display
        /// met while dimmed from freezing its dim value as the day value.
        /// </summary>
        public void Observe(string id, int min, int max, int current)
        {
            Entry e = Get(id);
            e.Min = min;
            e.Max = max;
            if (!e.HasAdopted)
            {
                e.Adopted = current;
                e.HasAdopted = true;
            }
            if (!e.HasLast)
            {
                e.Last = current;
                e.HasLast = true;
            }
        }

        /// <summary>The user moved a slider: that value is that display's day value from now on.</summary>
        public void Request(string id, int value)
        {
            Entry e = Get(id);
            e.Adopted = value;
            e.HasAdopted = true;
            e.DayValue = value;
            e.HasDayValue = true;
            e.HasDayRamp = false;          // the user's value is the day value now - no ramp needed
            e.Last = value;
            e.HasLast = true;

            DateTimeOffset now = _now();
            TimeSpan on, off;
            _settings.ResolvedWindow(out on, out off);
            if (_settings.Enabled && InWindow(on, off, now.TimeOfDay))
            {
                e.Overridden = true;
                e.OverrideNight = NightStart(on, now);
            }
        }

        /// <summary>
        /// Recomputes the phase and returns the writes the schedule wants now - only displays whose
        /// wanted value changed. Call it on every tick; it is idempotent.
        /// </summary>
        public IList<NightWrite> Advance()
        {
            DateTimeOffset now = _now();
            TimeSpan on, off;
            _settings.ResolvedWindow(out on, out off);

            bool usable = _settings.Enabled && on != off;
            bool inWindow = usable && InWindow(on, off, now.TimeOfDay);
            TimeSpan fade = _settings.GradualFade ? _fade : TimeSpan.Zero;

            // An override belongs to one night: it lapses when the next turn-on arrives.
            if (usable)
            {
                DateTimeOffset night = NightStart(on, now);
                foreach (KeyValuePair<string, Entry> kv in _entries)
                {
                    Entry e = kv.Value;
                    if (e.Overridden && e.OverrideNight != night) e.Overridden = false;
                }
            }

            if (!usable) _phase = _settings.Enabled ? NightPhase.Inert : NightPhase.Off;
            else if (inWindow)
            {
                TimeSpan since = Since(on, now.TimeOfDay);
                _phase = (fade > TimeSpan.Zero && since < fade) ? NightPhase.FadingIn : NightPhase.Night;
                _engaged = true;
            }
            else if (_engaged)
            {
                TimeSpan since = Since(off, now.TimeOfDay);
                _phase = (fade > TimeSpan.Zero && since < fade) ? NightPhase.FadingOut : NightPhase.Day;
            }
            else _phase = NightPhase.Day;

            bool owns = _phase == NightPhase.FadingIn || _phase == NightPhase.Night || _phase == NightPhase.FadingOut;
            bool restoring = _engaged && !owns;   // the ramp is done, or the schedule was switched off
            if (!owns) _engaged = false;

            var writes = new List<NightWrite>();
            foreach (KeyValuePair<string, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.HasAdopted) continue;

                // EXP17: while a night owns the displays, remember what it began from, so the fade
                // down starts where the day left off even if the day's own ramp was still running.
                if (owns)
                {
                    if (!e.HasNightFrom)
                    {
                        e.NightFrom = e.HasLast ? e.Last : e.Adopted;
                        e.HasNightFrom = true;
                    }
                }
                else e.HasNightFrom = false;

                if (!restoring && e.Overridden) continue;      // the user's move ended it for tonight
                // The morning lands the day value in one step (the clock says the fade is done); every
                // other non-night phase goes through Level, which ramps to the Default (EXP17).
                int target = (restoring && _phase == NightPhase.Day)
                    ? DayTarget(e) : Level(e, now, on, off, fade);
                if (e.HasLast && e.Last == target) continue;   // nothing to say
                e.Last = target;
                e.HasLast = true;
                writes.Add(new NightWrite(kv.Key, target));
            }
            return writes;
        }

        /// <summary>What the schedule wants this display to be right now, or null for "no opinion".</summary>
        public int? Target(string id)
        {
            Entry e;
            if (!_entries.TryGetValue(id, out e) || !e.HasAdopted) return null;
            if (e.Overridden) return null;
            TimeSpan on, off;
            _settings.ResolvedWindow(out on, out off);
            return Level(e, _now(), on, off, _settings.GradualFade ? _fade : TimeSpan.Zero);
        }

        /// <summary>Hands every display its day value back - the app calls this when it closes.</summary>
        public IList<NightWrite> Restore()
        {
            var writes = new List<NightWrite>();
            foreach (KeyValuePair<string, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.HasAdopted) continue;
                e.HasDayRamp = false;
                int day = DayTarget(e);
                if (e.HasLast && e.Last == day) continue;
                e.Last = day;
                e.HasLast = true;
                writes.Add(new NightWrite(kv.Key, day));
            }
            _engaged = false;
            return writes;
        }

        /// <summary>The reading the display held when the schedule took it over (recorded, not written).</summary>
        public int Baseline(string id) { return Get(id).Adopted; }

        /// <summary>EXP17: the day value the schedule wants for this display right now.</summary>
        public int DayValue(string id) { return DayTarget(Get(id)); }

        public bool IsOverridden(string id) { return Get(id).Overridden; }

        /// <summary>
        /// The display has gone (EXP14): forget it, so Restore() cannot hand a value back to a monitor
        /// that is no longer attached, and a display re-attached later starts from its own reading.
        /// </summary>
        public void Forget(string id)
        {
            _entries.Remove(id);
        }

        /// <summary>How many displays the schedule knows about - evidence that a departed one was forgotten.</summary>
        public int Count { get { return _entries.Count; } }

        // --- the value a display should have in the current phase ---

        private int Level(Entry e, DateTimeOffset now, TimeSpan on, TimeSpan off, TimeSpan fade)
        {
            int night = NightLevel(e.Min, e.Max, _settings.Strength);
            switch (_phase)
            {
                case NightPhase.Night:
                    return night;
                case NightPhase.FadingIn:
                    // EXP17: start the fade down from where the night found the display, not from a
                    // value computed off the clock - a launch during the day may still be ramping up.
                    return Lerp(e.HasNightFrom ? e.NightFrom : DayTarget(e), night,
                        Fraction(Since(on, now.TimeOfDay), fade));
                case NightPhase.FadingOut:
                    return Lerp(night, DayTarget(e), Fraction(Since(off, now.TimeOfDay), fade));
                case NightPhase.Day:
                    return DayRamp(e, now, fade);
                case NightPhase.Off:
                    // EXP17: with the night light switched off, the Default value governs the day, so
                    // changing it moves the displays. (Inert - a window with no length - stays silent,
                    // so a sunset placeholder or equal hours touches nothing, as it always did.)
                    return DayRamp(e, now, fade);
                default:
                    return e.Adopted;
            }
        }

        /// <summary>
        /// EXP17: the value the schedule returns a display to when it is not dimming it. A slider drag
        /// made this display's own value; otherwise it is the configured daylight, scaled into the
        /// display's range exactly like the night strength.
        /// </summary>
        private int DayTarget(Entry e)
        {
            if (e.HasDayValue) return e.DayValue;
            return NightLevel(e.Min, e.Max, _settings.Daylight);
        }

        /// <summary>
        /// EXP17: a display that must move in the Day phase. There is no clock boundary to anchor this
        /// ramp (the app may have just started, or the daylight value may have just been edited), so it
        /// carries its own start - one step per `step`, and a single step when gradual fade is off.
        /// </summary>
        private int DayRamp(Entry e, DateTimeOffset now, TimeSpan fade)
        {
            int to = DayTarget(e);
            if (!e.HasLast || e.Last == to || fade <= TimeSpan.Zero)
            {
                e.HasDayRamp = false;
                return to;
            }

            if (!e.HasDayRamp || e.DayRampTo != to)
            {
                e.HasDayRamp = true;
                e.DayRampFrom = e.Last;
                e.DayRampStart = now;
                e.DayRampTo = to;
            }
            return Lerp(e.DayRampFrom, to, Fraction(now - e.DayRampStart, fade));
        }

        /// <summary>
        /// How far into a fade we are, as a step of the staircase: the value changes at most once per
        /// `step`, so a 30-minute fade is ~30 writes per display no matter how often it is ticked.
        /// </summary>
        private double Fraction(TimeSpan elapsed, TimeSpan fade)
        {
            if (fade <= TimeSpan.Zero || elapsed >= fade) return 1.0;
            if (elapsed <= TimeSpan.Zero) return 0.0;
            double steps = fade.TotalMilliseconds / _step.TotalMilliseconds;
            if (steps < 1.0) steps = 1.0;
            double f = Math.Floor(elapsed.TotalMilliseconds / _step.TotalMilliseconds) / steps;
            if (f < 0.0) f = 0.0;
            if (f > 1.0) f = 1.0;
            return f;
        }

        private string StrengthText()
        {
            return _settings.Strength.ToString(CultureInfo.InvariantCulture);
        }

        private Entry Get(string id)
        {
            Entry e;
            if (!_entries.TryGetValue(id, out e))
            {
                e = new Entry();
                _entries[id] = e;
            }
            return e;
        }

        // --- pure helpers (also used by the assertions) ---

        /// <summary>A window that wraps midnight when turn-on is later in the day than turn-off.</summary>
        public static bool InWindow(TimeSpan on, TimeSpan off, TimeSpan t)
        {
            if (on == off) return false;
            if (on < off) return t >= on && t < off;
            return t >= on || t < off;
        }

        /// <summary>How long ago the last occurrence of this time of day was, in [0, 24h).</summary>
        public static TimeSpan Since(TimeSpan boundary, TimeSpan t)
        {
            TimeSpan d = t - boundary;
            if (d < TimeSpan.Zero) d += TimeSpan.FromDays(1);
            return d;
        }

        /// <summary>The turn-on that started the night cycle `now` belongs to.</summary>
        public static DateTimeOffset NightStart(TimeSpan on, DateTimeOffset now)
        {
            DateTimeOffset today = new DateTimeOffset(now.Date.Add(on), now.Offset);
            return now >= today ? today : today.AddDays(-1);
        }

        /// <summary>The strength as a value in the display's own range (0-100 stays 0-100).</summary>
        public static int NightLevel(int min, int max, int strength)
        {
            if (max <= min) return min;
            int level = min + (int)Math.Round((max - min) * (strength / 100.0), MidpointRounding.AwayFromZero);
            if (level < min) level = min;
            if (level > max) level = max;
            return level;
        }

        private static int Lerp(int from, int to, double fraction)
        {
            return from + (int)Math.Round((to - from) * fraction, MidpointRounding.AwayFromZero);
        }
    }
}
