// EXP06 - the night-light schedule as a state machine.
// EXP17 - the day value is *configured* (Settings.Daylight), not read off the panel and not set by the
//         slider: a display the app meets while it is already dimmed no longer comes back dim, and a
//         slider drag no longer becomes tomorrow's day value. The Default governs whenever the schedule
//         is not dimming (Day, switched off, or the sunset placeholder), and a slider drag is
//         *ephemeral* - it moves the display and stands the schedule down for it only until the next
//         boundary (the coming turn-on or turn-off), where the schedule resumes.
//         Every change of a display's *wanted* value glides at a constant speed in fixed-size steps
//         (daySpeed/dayGrain): the Default, a slider, the night dimming, a strength or an hours edit.
//         Nothing jumps, and there is no separate "fade" - a move is the same move whatever asked for
//         it, which is why the old gradual-fade switch is gone.
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
// we asked for, an ephemeral slider value and when it expires, and where the current glide began.
//
// C# 5 only - the in-box csc.exe (.NET Framework 4.0 compiler) is what builds this.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitron.App
{
    internal enum NightPhase
    {
        Day,        // outside the night window: the Default
        Night,      // inside the night window: the night level
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
    ///     wanted value changed - so ticking often is free.
    ///   * The night level is the strength scaled into the display's own range; the day value is the
    ///     configured Default scaled the same way (EXP17).
    ///   * Every change of a display's wanted value glides: a settings edit, a slider move, the night
    ///     dimming at turn-on and the restore at turn-off. Nothing jumps (EXP17).
    ///   * Request() is the *user* moving a slider: it is ephemeral - it glides the display and stands
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
            public int Manual;              // the value that slider asked for

            // EXP17: every move carries where it began, when, and where it is heading - the wanted value
            // can change under it (a phase change, a settings edit), and then it re-anchors and glides on.
            public bool HasRamp;
            public int RampFrom;
            public DateTimeOffset RampStart;
            public int RampTo;
        }

        private readonly ScheduleSettings _settings;
        private readonly Func<DateTimeOffset> _now;
        private readonly double _daySpeed;    // points per second - every move runs at this speed
        private readonly int _dayGrain;       // ... in steps this size, so smoothness is size-independent
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();

        private NightPhase _phase = NightPhase.Day;
        private int _lastDaylight;            // to notice a Default edit (EXP17)

        public NightLightScheduler(ScheduleSettings settings, Func<DateTimeOffset> now, double daySpeed, int dayGrain)
        {
            _settings = settings;
            _now = now;
            _daySpeed = daySpeed > 0 ? daySpeed : 10.0;
            _dayGrain = dayGrain > 0 ? dayGrain : 1;
            _lastDaylight = settings.Daylight;
        }

        /// <summary>The phase as of the last Advance().</summary>
        public NightPhase Phase { get { return _phase; } }

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
                    case NightPhase.Night:
                        return "Night light: " + StrengthText() + "% until " + ScheduleSettings.FormatTime(off);
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
        /// changes the Default. The display *glides* to it at the same speed every other move uses, so
        /// the knob can jump to the click while the dimming catches up.
        /// </summary>
        public void Request(string id, int value)
        {
            Entry e = Get(id);
            e.Overridden = true;
            e.Manual = value;

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
            if (!usable) _phase = _settings.Enabled ? NightPhase.Inert : NightPhase.Off;
            else _phase = InWindow(on, off, now.TimeOfDay) ? NightPhase.Night : NightPhase.Day;

            // EXP17: editing the Default is an instruction to hold it. It ends any ephemeral slider value
            // while the schedule is not dimming (during the night it changes nothing - the display is dim).
            if (_settings.Daylight != _lastDaylight)
            {
                _lastDaylight = _settings.Daylight;
                if (_phase != NightPhase.Night)
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

                // Whatever asked for it - the phase, a settings edit, or the user's own slider - the move
                // is the same glide from where the display is to where it should be.
                int target = Ramp(e, now, e.Overridden ? e.Manual : Wanted(e));
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
            return Ramp(e, _now(), Wanted(e));
        }

        /// <summary>Hands every display the Default back - the app calls this when it closes.</summary>
        public IList<NightWrite> Restore()
        {
            var writes = new List<NightWrite>();
            foreach (KeyValuePair<string, Entry> kv in _entries)
            {
                Entry e = kv.Value;
                if (!e.HasAdopted) continue;
                e.HasRamp = false;
                int day = DayTarget(e);
                if (e.HasLast && e.Last == day) continue;
                e.Last = day;
                e.HasLast = true;
                writes.Add(new NightWrite(kv.Key, day));
            }
            return writes;
        }

        /// <summary>The reading the display held when the schedule took it over (recorded, not written).</summary>
        public int Baseline(string id) { return Get(id).Adopted; }

        /// <summary>EXP17: the day value the schedule wants for this display right now (the Default).</summary>
        public int DayValue(string id) { return DayTarget(Get(id)); }

        /// <summary>
        /// EXP17: where the schedule is taking this display - the night level in the night window, the
        /// Default otherwise. Null for a display it has no opinion about, or one the user's own slider
        /// value currently stands for. The row lands the knob here at once and lets the dimming catch up.
        /// </summary>
        public int? Goal(string id)
        {
            Entry e;
            if (!_entries.TryGetValue(id, out e) || !e.HasAdopted) return null;
            if (e.Overridden) return null;
            return Wanted(e);
        }

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

        // --- the value a display wants, and the glide that takes it there ---

        /// <summary>The value this display *wants* in the current phase: the night level, or the Default.</summary>
        private int Wanted(Entry e)
        {
            return _phase == NightPhase.Night
                ? NightLevel(e.Min, e.Max, _settings.Strength)
                : DayTarget(e);
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
        /// EXP17: the glide toward `to` - a constant speed in fixed-size steps, from where the display is
        /// now. A one-point change is a single write; a long one glides over distance/speed seconds, and
        /// every step is `dayGrain` points, so smoothness never depends on how far it has to travel.
        /// Re-anchors whenever the wanted value changes, so a phase change or a settings edit mid-move
        /// simply retargets it.
        /// </summary>
        private int Ramp(Entry e, DateTimeOffset now, int to)
        {
            if (!e.HasLast || e.Last == to)
            {
                e.HasRamp = false;
                return to;
            }
            if (!e.HasRamp || e.RampTo != to)
            {
                e.HasRamp = true;
                e.RampFrom = e.Last;
                e.RampStart = now;
                e.RampTo = to;
            }
            return GlideAt(e.RampFrom, to, now - e.RampStart, _daySpeed, _dayGrain);
        }

        /// <summary>
        /// EXP17: where a move from `from` to `to` has got to after `elapsed`, at `speed` points per
        /// second in `grain`-sized steps. The duration follows the distance - a one-point move is a
        /// single step - while the step size does not, so smoothness never depends on how far the
        /// display has to travel.
        /// </summary>
        private static int GlideAt(int from, int to, TimeSpan elapsed, double speed, int grain)
        {
            if (from == to || speed <= 0 || grain <= 0) return to;
            int delta = Math.Abs(to - from);
            int dir = to >= from ? 1 : -1;
            // Count the steps in ticks, not seconds: 0.2 s * 10 / 2 in doubles lands just under 1.0, and
            // an uneven staircase is exactly the choppiness this is here to remove.
            double ticksPerStep = TimeSpan.TicksPerSecond * (double)grain / speed;
            if (ticksPerStep < 1.0) ticksPerStep = 1.0;
            long steps = (long)(elapsed.Ticks / ticksPerStep);
            if (steps < 0) steps = 0;
            long moved = steps * grain;
            if (moved >= delta) return to;
            return from + dir * (int)moved;
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
    }
}
