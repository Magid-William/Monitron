// EXP06 - the night-light schedule as a state machine.
// EXP17 - the day value is *configured* (Settings.Daylight), not read off the panel and not set by the
//         slider: a display the app meets while it is already dimmed no longer comes back dim, and a
//         slider drag no longer becomes tomorrow's day value. The Default governs whenever the schedule
//         is not dimming (Day, switched off, or the sunset placeholder), and a slider drag is
//         *ephemeral* - it moves the display and stands the schedule down for it only until the next
//         boundary (the coming turn-on or turn-off), where the schedule resumes. A move with no clock
//         boundary carries its own ramp - and, being something the user just asked for, a *short* one
//         (dayFade/dayStep: 5 seconds by default) rather than the 30-minute night fade. Every ramp is
//         the same staircase, so a move steps at most once per its own step.
//
// Inputs: the clock and ScheduleSettings. Output: the brightness values the schedule wants the
// displays to have *now*, as a list of writes the caller hands to BrightnessWriter. There is no
// WPF, no P/Invoke and no timer here on purpose - the caller supplies `now` and the tick, so the
// headless test (ScheduleTests.cs, run by schedule-test.ps1) drives a whole night by moving a clock
// and never waits. The hardware stays in BrightnessController.cs.
//
// The phase is *derived from the clock*, not accumulated: an Advance() that lands well past a
// boundary computes the same phase as one that walked there tick by tick. The carried state is only
// what the clock cannot tell us - the reading each display had when we took it over, the last value
// we asked for, an ephemeral slider value and when it expires, and where each fade began (a Day-phase
// move has no boundary, and a fade must start from where the display actually is).
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
    ///   * The night level is the strength scaled into the display's own range; the day value is the
    ///     configured Default scaled the same way (EXP17).
    ///   * Request() is the *user* moving a slider: it is ephemeral - it moves the display and stands
    ///     the schedule down for it until the next boundary (turn-on or turn-off). It never changes the
    ///     Default, so the next day's daylight is the Default again.
    ///   * Editing the Default ends any ephemeral value while the schedule is not dimming.
    ///   * Restore() hands every display the Default back; it is what the app calls on close.
    /// </summary>
    internal sealed class NightLightScheduler
    {
        private sealed class Entry
        {
            public int Min;
            public int Max;
            public bool HasAdopted;
            public int Adopted;             // the reading the display held when we took it over (recorded)
            public bool HasLast;
            public int Last;                // the last value the schedule asked for (dedup)
            public bool Overridden;         // an ephemeral slider value stands for this display
            public DateTimeOffset OverrideUntil;   // ...until this boundary

            // EXP17: a move in the Day phase has no clock boundary to anchor it, so the ramp carries
            // when it began and from what value (and where it is heading, so an edit re-anchors it).
            public bool HasDayRamp;
            public int DayRampFrom;
            public DateTimeOffset DayRampStart;
            public int DayRampTo;

            // A fade must begin where the display actually is: the fade down starts from the day value
            // (or a slider value), and the fade up from the night level (or a slider value).
            public bool HasNightFrom;
            public int NightFrom;
            public bool HasDayFrom;
            public int DayFrom;
        }

        private readonly ScheduleSettings _settings;
        private readonly Func<DateTimeOffset> _now;
        private readonly TimeSpan _fade;      // the night fade (down, and back up in the morning)
        private readonly TimeSpan _step;
        private readonly TimeSpan _dayFade;   // EXP17: a move to the Default - short, it is not the night
        private readonly TimeSpan _dayStep;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        private NightPhase _phase = NightPhase.Day;
        private bool _engaged;              // the schedule has taken the displays over
        private int _lastDaylight;          // to notice a Default edit (EXP17)

        public NightLightScheduler(ScheduleSettings settings, Func<DateTimeOffset> now, TimeSpan fade, TimeSpan step,
            TimeSpan dayFade, TimeSpan dayStep)
        {
            _settings = settings;
            _now = now;
            _fade = fade > TimeSpan.Zero ? fade : TimeSpan.Zero;
            _step = step > TimeSpan.Zero ? step : TimeSpan.FromMinutes(1);
            _dayFade = dayFade > TimeSpan.Zero ? dayFade : TimeSpan.FromMinutes(1);
            _dayStep = dayStep > TimeSpan.Zero ? dayStep : TimeSpan.FromSeconds(1);
            _lastDaylight = settings.Daylight;
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
        /// A display was detected, with its real value. The reading is recorded as what the display held
        /// when we took it over - it is *not* the day value, which is the configured Default. This is
        /// what stops a display met while dimmed from freezing its dim value as the day value.
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

        /// <summary>
        /// EXP17: the user moved a slider. The value is *ephemeral* - it stands the schedule down for
        /// this display only until the next boundary (the coming turn-on or turn-off), and it never
        /// changes the Default. So the next day's daylight is the Default again, not the slider.
        /// </summary>
        public void Request(string id, int value)
        {
            Entry e = Get(id);
            e.Last = value;
            e.HasLast = true;
            e.HasDayRamp = false;          // the user's value stands; no day ramp is running
            e.Overridden = true;

            TimeSpan on, off;
            _settings.ResolvedWindow(out on, out off);
            if (on == off)   // the sunset placeholder / equal hours keep the times but no window
            {
                on = _settings.TurnOn;
                off = _settings.TurnOff;
            }
            e.OverrideUntil = NextBoundary(on, off, _now());
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

            // EXP17: editing the Default is an instruction to hold it. It ends any ephemeral slider
            // value while the schedule is not dimming (during the night it only changes the morning's
            // target - the display stays dimmed).
            if (_settings.Daylight != _lastDaylight)
            {
                _lastDaylight = _settings.Daylight;
                if (!owns)
                {
                    foreach (KeyValuePair<string, Entry> kv in _entries) kv.Value.Overridden = false;
                }
            }

            var writes = new List<NightWrite>();
            foreach (KeyValuePair<string, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.HasAdopted) continue;

                // The ephemeral slider value stands only until its boundary; then the schedule resumes.
                if (e.Overridden && now >= e.OverrideUntil) e.Overridden = false;

                // A fade begins from where the display is: the fade down from the day value (or a
                // slider value), the fade up from the night level (or a slider value).
                if (_phase == NightPhase.FadingIn)
                {
                    if (!e.HasNightFrom)
                    {
                        e.NightFrom = e.HasLast ? e.Last : e.Adopted;
                        e.HasNightFrom = true;
                    }
                }
                else e.HasNightFrom = false;
                if (_phase == NightPhase.FadingOut)
                {
                    if (!e.HasDayFrom)
                    {
                        e.DayFrom = e.HasLast ? e.Last : NightLevel(e.Min, e.Max, _settings.Strength);
                        e.HasDayFrom = true;
                    }
                }
                else e.HasDayFrom = false;

                if (!restoring && e.Overridden) continue;      // the user's value stands for now
                // The morning lands the Default in one step (the clock says the fade is done); every
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

        /// <summary>Hands every display the Default back - the app calls this when it closes.</summary>
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

        /// <summary>EXP17: the day value the schedule wants for this display right now (the Default).</summary>
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
                    return Lerp(e.HasNightFrom ? e.NightFrom : DayTarget(e), night,
                        Fraction(Since(on, now.TimeOfDay), fade, _step));
                case NightPhase.FadingOut:
                    return Lerp(e.HasDayFrom ? e.DayFrom : night, DayTarget(e),
                        Fraction(Since(off, now.TimeOfDay), fade, _step));
                default:
                    // Day / Off / Inert: the Default governs - the schedule is not dimming anything.
                    return DayRamp(e, now, fade);
            }
        }

        /// <summary>
        /// EXP17: the day value - the configured Default, scaled into the display's range exactly like
        /// the night strength. Nothing else sets it: a slider drag is ephemeral and does not change it.
        /// </summary>
        private int DayTarget(Entry e)
        {
            return NightLevel(e.Min, e.Max, _settings.Daylight);
        }

        /// <summary>
        /// EXP17: a display that must move in the day. There is no clock boundary to anchor this ramp
        /// (the app may have just started, or the Default may have just been edited), so it carries its
        /// own start - one step per `step`, and a single step when gradual fade is off.
        /// </summary>
        private int DayRamp(Entry e, DateTimeOffset now, TimeSpan fade)
        {
            int to = DayTarget(e);
            // `fade` is the night fade, passed only for its sign: zero means gradual fade is off, so
            // the Default lands in one step. The ramp's own length is the short day move (EXP17), so a
            // settings edit is not drawn out over the 30-minute night fade.
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
            return Lerp(e.DayRampFrom, to, Fraction(now - e.DayRampStart, _dayFade, _dayStep));
        }

        /// <summary>
        /// How far into a fade we are, as a step of the staircase: the value changes at most once per
        /// `step`, so a 30-minute night fade is ~30 writes and a 5-second day move ~10, no matter how
        /// often it is ticked.
        /// </summary>
        private static double Fraction(TimeSpan elapsed, TimeSpan fade, TimeSpan step)
        {
            if (fade <= TimeSpan.Zero || elapsed >= fade) return 1.0;
            if (elapsed <= TimeSpan.Zero) return 0.0;
            double steps = fade.TotalMilliseconds / step.TotalMilliseconds;
            if (steps < 1.0) steps = 1.0;
            double f = Math.Floor(elapsed.TotalMilliseconds / step.TotalMilliseconds) / steps;
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

        /// <summary>
        /// EXP17: the next boundary an ephemeral slider value runs to - the earlier of the next turn-on
        /// and the next turn-off. With no window (the sunset placeholder, equal hours) the earlier of
        /// the kept times is used, so the value still expires within a day.
        /// </summary>
        public static DateTimeOffset NextBoundary(TimeSpan on, TimeSpan off, DateTimeOffset now)
        {
            DateTimeOffset nextOn = NextOccurrence(on, now);
            if (on == off) return nextOn;
            DateTimeOffset nextOff = NextOccurrence(off, now);
            return nextOn <= nextOff ? nextOn : nextOff;
        }

        /// <summary>The next time-of-day after `now` (strictly after, so a boundary lands on the next day).</summary>
        private static DateTimeOffset NextOccurrence(TimeSpan time, DateTimeOffset now)
        {
            DateTimeOffset today = new DateTimeOffset(now.Date.Add(time), now.Offset);
            return now < today ? today : today.AddDays(1);
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
