// EXP06 - the night-light schedule as a state machine.
//
// Inputs: the clock and ScheduleSettings. Output: the brightness values the schedule wants the
// displays to have *now*, as a list of writes the caller hands to BrightnessWriter. There is no
// WPF, no P/Invoke and no timer here on purpose - the caller supplies `now` and the tick, so the
// headless test (ScheduleTests.cs, run by schedule-test.ps1) drives a whole night by moving a clock
// and never waits. The hardware stays in BrightnessController.cs.
//
// The phase is *derived from the clock*, not accumulated: an Advance() that lands well past a
// boundary computes the same phase as one that walked there tick by tick. The only carried state is
// what the clock cannot tell us - each display's day value, the last value we asked for, and
// whether the user has taken a display over for this night.
//
// C# 5 only - the in-box csc.exe (.NET Framework 4.0 compiler) is what builds this.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitorn.App
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
    ///   * The night level is the strength scaled into the display's own range.
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
            public bool HasBaseline;
            public int Baseline;            // the day value: read at detection, or set by the user
            public bool HasLast;
            public int Last;                // the last value the schedule asked for (dedup)
            public bool Overridden;
            public DateTimeOffset OverrideNight;
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

        /// <summary>A display was detected, with its real value. Its day value is captured once.</summary>
        public void Observe(string id, int min, int max, int current)
        {
            Entry e = Get(id);
            e.Min = min;
            e.Max = max;
            if (!e.HasBaseline)
            {
                e.Baseline = current;
                e.HasBaseline = true;
            }
            if (!e.HasLast)
            {
                e.Last = current;
                e.HasLast = true;
            }
        }

        /// <summary>The user moved a slider: that value is the day value from now on.</summary>
        public void Request(string id, int value)
        {
            Entry e = Get(id);
            e.Baseline = value;
            e.HasBaseline = true;
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
                if (!e.HasBaseline) continue;
                if (!restoring && e.Overridden) continue;      // the user's move ended it for tonight
                int target = restoring ? e.Baseline : Level(e, now, on, off, fade);
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
            if (!_entries.TryGetValue(id, out e) || !e.HasBaseline) return null;
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
                if (!e.HasBaseline) continue;
                if (e.HasLast && e.Last == e.Baseline) continue;
                e.Last = e.Baseline;
                e.HasLast = true;
                writes.Add(new NightWrite(kv.Key, e.Baseline));
            }
            _engaged = false;
            return writes;
        }

        public int Baseline(string id) { return Get(id).Baseline; }
        public bool IsOverridden(string id) { return Get(id).Overridden; }

        // --- the value a display should have in the current phase ---

        private int Level(Entry e, DateTimeOffset now, TimeSpan on, TimeSpan off, TimeSpan fade)
        {
            int night = NightLevel(e.Min, e.Max, _settings.Strength);
            switch (_phase)
            {
                case NightPhase.Night:
                    return night;
                case NightPhase.FadingIn:
                    return Lerp(e.Baseline, night, Fraction(Since(on, now.TimeOfDay), fade));
                case NightPhase.FadingOut:
                    return Lerp(night, e.Baseline, Fraction(Since(off, now.TimeOfDay), fade));
                default:
                    return e.Baseline;
            }
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
