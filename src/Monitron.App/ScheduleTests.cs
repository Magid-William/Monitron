// EXP06 - drives NightLightScheduler through its transitions and asserts the exposed state.
//
// The clock only moves when the test says so, so a whole night - the dim, the hold, the restore, the
// wrap across midnight, the override - is asserted in milliseconds without waiting on anything.
// This is the AGENTS rule for machine logic, and the reason Schedule.cs holds no timer, no WPF and
// no hardware.
//
// Built as a console exe by schedule-test.ps1 (a winexe cannot print).

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Monitron.App
{
    /// <summary>A clock the test moves by hand. (Named apart from BrightnessWriterTests' FakeClock.)</summary>
    internal sealed class NightClock
    {
        private DateTimeOffset _now = new DateTimeOffset(2026, 10, 15, 16, 0, 0, TimeSpan.Zero);

        public DateTimeOffset Now { get { return _now; } }

        public void Set(DateTimeOffset value) { _now = value; }

        public void At(int day, int hour, int minute)
        {
            _now = new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.Zero);
        }

        public void Advance(TimeSpan by) { _now = _now.Add(by); }
    }

    internal static class ScheduleTests
    {
        private static int _passed;
        private static int _failed;

        // EXP17: the app's own move tuning (Program.cs DaySpeed/DayGrain), so the fixture drives the
        // glide the shipped app actually has: a constant speed in fixed-size steps.
        private static readonly double DaySpeed = 10.0;
        private static readonly int DayGrain = 2;
        /// <summary>How long one grain-sized step of a glide takes (200 ms).</summary>
        private static readonly TimeSpan Step = TimeSpan.FromSeconds(DayGrain / DaySpeed);

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static int Main()
        {
            Console.WriteLine("NightLightScheduler - clock driven, state asserted");

            PureHelpers();
            TheNightGlidesAndHolds();
            LaunchAtNightGlides();
            TheDimIsABoundedGlide();
            ADayDragIsEphemeral();
            ANightDragEndsAtTheMorning();
            EditingTheDefaultEndsEphemeralValues();
            SwitchingTheFeatureOffReturnsToDefault();
            SunsetModeAppliesTheDefault();
            StrengthAndHoursChangesGlide();
            RangesArePerDisplay();
            IdempotentAndQuiet();
            RestoreHandsTheValueBack();
            StatusReportsThePhase();
            TheDayValueIsTheDaylightSetting();
            ADisplayMetDimmedComesBackToDaylight();
            TheNightRetargetsFromWhereTheDayIs();
            EditingTheDaylightReanchorsTheRamp();
            TheGoalIsTheInstruction();
            TheFeatureOffAppliesTheDefault();

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        // --- fixture: one 0-100 display reading 70, the sketch's schedule (5:00 PM -> 9:00 AM) ---

        private sealed class Rig
        {
            public readonly NightClock Clock = new NightClock();
            public readonly ScheduleSettings Settings = new ScheduleSettings();
            public readonly NightLightScheduler Scheduler;

            /// <summary>The mechanics fixture: the Default matches the reading, so the day is quiet.</summary>
            public Rig(string on, string off, int strength) : this(on, off, strength, 70) { }

            /// <summary>EXP17: a fixture whose configured Default differs from the reading.</summary>
            public Rig(string on, string off, int strength, int daylight)
            {
                Clock.At(15, 16, 0);
                SetWindow(Settings, on, off);
                Settings.TrySetStrength(strength.ToString(CultureInfo.InvariantCulture));
                Settings.TrySetDaylight(daylight.ToString(CultureInfo.InvariantCulture));
                Scheduler = new NightLightScheduler(Settings, delegate { return Clock.Now; }, DaySpeed, DayGrain);
                Scheduler.Observe("A", 0, 100, 70);
            }
        }

        private static void SetWindow(ScheduleSettings s, string on, string off)
        {
            int hour12, minute; bool pm;
            if (!ScheduleSettings.TryParseClock(on, out hour12, out minute, out pm)) throw new Exception("bad time " + on);
            s.TrySetTurnOn(hour12, minute, pm);
            if (!ScheduleSettings.TryParseClock(off, out hour12, out minute, out pm)) throw new Exception("bad time " + off);
            s.TrySetTurnOff(hour12, minute, pm);
        }

        /// <summary>Moves the clock `times` x `by` and collects every write the schedule asked for.</summary>
        private static List<NightWrite> Run(NightLightScheduler s, NightClock c, TimeSpan by, int times)
        {
            var all = new List<NightWrite>();
            for (int i = 0; i < times; i++)
            {
                c.Advance(by);
                foreach (NightWrite w in s.Advance()) all.Add(w);
            }
            return all;
        }

        private static string Dump(IList<NightWrite> writes)
        {
            var parts = new List<string>();
            foreach (NightWrite w in writes) parts.Add(w.Id + "=" + w.Value.ToString(CultureInfo.InvariantCulture));
            return string.Join(",", parts.ToArray());
        }

        private static bool NonIncreasing(IList<NightWrite> writes)
        {
            for (int i = 1; i < writes.Count; i++) if (writes[i].Value > writes[i - 1].Value) return false;
            return true;
        }

        private static bool NonDecreasing(IList<NightWrite> writes)
        {
            for (int i = 1; i < writes.Count; i++) if (writes[i].Value < writes[i - 1].Value) return false;
            return true;
        }

        private static bool StrictlyIncreasing(IList<NightWrite> writes)
        {
            for (int i = 1; i < writes.Count; i++) if (writes[i].Value <= writes[i - 1].Value) return false;
            return true;
        }

        // --- the assertions ---

        private static void PureHelpers()
        {
            Console.WriteLine("pure helpers: the window and the level");
            Check(NightLightScheduler.InWindow(new TimeSpan(17, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(23, 0, 0)),
                "a window that wraps midnight contains 23:00");
            Check(NightLightScheduler.InWindow(new TimeSpan(17, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(2, 0, 0)),
                "and it still contains 02:00 the next morning");
            Check(!NightLightScheduler.InWindow(new TimeSpan(17, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(12, 0, 0)),
                "and it excludes 12:00");
            Check(!NightLightScheduler.InWindow(new TimeSpan(17, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(9, 0, 0)),
                "turn-off is the first moment outside it");
            Check(!NightLightScheduler.InWindow(new TimeSpan(5, 0, 0), new TimeSpan(5, 0, 0), new TimeSpan(6, 0, 0)),
                "turn-on == turn-off is no window at all");
            Check(NightLightScheduler.InWindow(new TimeSpan(5, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(6, 0, 0))
                && !NightLightScheduler.InWindow(new TimeSpan(5, 0, 0), new TimeSpan(9, 0, 0), new TimeSpan(12, 0, 0)),
                "a same-day window works too");

            Check(NightLightScheduler.NightLevel(0, 100, 38) == 38, "strength 38 of a 0-100 display is 38");
            Check(NightLightScheduler.NightLevel(20, 80, 38) == 43, "scaled into a display that starts at 20 (got "
                + NightLightScheduler.NightLevel(20, 80, 38).ToString(CultureInfo.InvariantCulture) + ")");
            Check(NightLightScheduler.NightLevel(20, 80, 0) == 20, "strength 0 lands on the display's minimum");
            Check(NightLightScheduler.NightLevel(0, 100, 100) == 100, "strength 100 lands on its maximum");
            Check(NightLightScheduler.NightLevel(10, 10, 50) == 10, "a zero-width range cannot be scaled");

            int hour12, minute; bool pm;
            Check(ScheduleSettings.TryParseClock("16:05", out hour12, out minute, out pm) && hour12 == 4 && minute == 5 && pm,
                "16:05 parses as 4:05 PM");
            Check(ScheduleSettings.TryParseClock("09:00", out hour12, out minute, out pm) && hour12 == 9 && minute == 0 && !pm,
                "09:00 parses as 9:00 AM");
            Check(ScheduleSettings.TryParseClock("00:00", out hour12, out minute, out pm) && hour12 == 12 && !pm,
                "00:00 parses as 12:00 AM");
            Check(ScheduleSettings.TryParseClock("12:30", out hour12, out minute, out pm) && hour12 == 12 && pm,
                "12:30 parses as 12:30 PM");
            Check(!ScheduleSettings.TryParseClock("24:00", out hour12, out minute, out pm)
                && !ScheduleSettings.TryParseClock("9", out hour12, out minute, out pm)
                && !ScheduleSettings.TryParseClock("abc", out hour12, out minute, out pm)
                && !ScheduleSettings.TryParseClock(null, out hour12, out minute, out pm),
                "24:00, '9', 'abc' and null are all refused");
        }

        private static void TheNightGlidesAndHolds()
        {
            Console.WriteLine("the night glides to the strength, holds, and glides back to the Default");
            var r = new Rig("17:00", "09:00", 38);
            Check(r.Scheduler.Advance().Count == 0, "16:00: the schedule wants nothing");
            Check(r.Scheduler.Phase == NightPhase.Day, "16:00: phase is Day");
            Check(r.Scheduler.Target("A") == 70, "16:00: the display is left at the Default (70)");

            r.Clock.At(15, 17, 0);
            Check(r.Scheduler.Advance().Count == 0, "17:00: the glide starts where the display is, so nothing jumps");
            Check(r.Scheduler.Phase == NightPhase.Night, "17:00: phase is Night");
            List<NightWrite> down = Run(r.Scheduler, r.Clock, Step, 16);       // 70 -> 38 is 32 points, 2 at a time
            Check(down.Count == 16, "16 even 2-point steps take it down (got " + Dump(down) + ")");
            Check(NonIncreasing(down), "every step is at or below the one before it");
            Check(down[down.Count - 1].Value == 38, "the last step lands exactly on the strength (38)");
            bool overshoot = false;
            foreach (NightWrite w in down) if (w.Value < 38) overshoot = true;
            Check(!overshoot, "no step undershoots the night level");
            Check(r.Scheduler.Advance().Count == 0, "ticking during the hold writes nothing at all");

            r.Clock.At(16, 2, 0);
            Check(r.Scheduler.Advance().Count == 0, "02:00 next day: still the night, nothing to change");
            Check(r.Scheduler.Target("A") == 38, "02:00: still 38 across midnight (the window wraps)");
            Check(r.Scheduler.Phase == NightPhase.Night, "02:00: phase is still Night");

            r.Clock.At(16, 9, 0);
            Check(r.Scheduler.Advance().Count == 0, "09:00: the night ends exactly here, value unchanged yet");
            Check(r.Scheduler.Phase == NightPhase.Day, "09:00: phase is Day again");
            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 16);         // 38 -> 70
            Check(up.Count == 16 && NonDecreasing(up) && up[up.Count - 1].Value == 70,
                "and it glides back up to the Default in even steps (got " + Dump(up) + ")");
            Check(r.Scheduler.Advance().Count == 0, "and then the day is quiet again");
        }

        private static void LaunchAtNightGlides()
        {
            Console.WriteLine("launched inside the window, the display glides down rather than snapping");
            var r = new Rig("17:00", "09:00", 38);
            r.Clock.At(15, 22, 0);
            Check(r.Scheduler.Advance().Count == 0, "22:00 at launch: the glide starts at the reading (70)");
            Check(r.Scheduler.Phase == NightPhase.Night, "and the phase is already Night");
            List<NightWrite> down = Run(r.Scheduler, r.Clock, Step, 16);
            Check(down.Count == 16 && down[down.Count - 1].Value == 38,
                "and it glides to the night level (got " + Dump(down) + ")");
        }

        private static void TheDimIsABoundedGlide()
        {
            Console.WriteLine("a fine tick costs nothing: the night is one write per 2-point step, however often it is ticked");
            var r = new Rig("17:00", "09:00", 38);
            r.Clock.At(15, 16, 59);
            r.Scheduler.Advance();

            List<NightWrite> all = new List<NightWrite>();
            for (int i = 0; i < 700; i++)                 // 16:59:00 + 100 ms each, across the turn-on
            {
                r.Clock.Advance(TimeSpan.FromMilliseconds(100));
                foreach (NightWrite w in r.Scheduler.Advance()) all.Add(w);
            }
            Check(all.Count == 16, "ticking 10x a second across the dim still costs one write per 2-point step (got "
                + all.Count.ToString(CultureInfo.InvariantCulture) + ")");
            Check(all[all.Count - 1].Value == 38, "and the last one is the night level");
            Check(r.Scheduler.Advance().Count == 0, "a tick during the hold writes nothing");
        }

        private static void ADayDragIsEphemeral()
        {
            Console.WriteLine("EXP17: a slider drag glides to the value, is ephemeral, and never changes the Default");
            var r = new Rig("17:00", "09:00", 38, 70);
            r.Clock.At(15, 14, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Target("A") == 70, "14:00: the day holds the Default 70");

            r.Scheduler.Request("A", 50);              // the user drags the slider
            Check(r.Scheduler.IsOverridden("A"), "the drag stands the schedule down for this display");
            Check(r.Scheduler.Target("A") == null, "the schedule has no opinion while it stands");
            Check(r.Scheduler.DayValue("A") == 70, "and the Default is UNCHANGED (still 70)");
            Check(r.Scheduler.Advance().Count == 0, "the glide starts where the display is (70), so nothing jumps");

            List<NightWrite> glide = Run(r.Scheduler, r.Clock, Step, 10);    // 2 s for 20 points
            Check(glide.Count == 10 && glide[glide.Count - 1].Value == 50,
                "the dimming glides to the value in even steps (got " + Dump(glide) + ")");

            r.Clock.At(15, 16, 59);
            Check(r.Scheduler.Advance().Count == 0, "16:59: it does not put the Default back");
            Check(r.Scheduler.IsOverridden("A"), "and the ephemeral value still stands");

            r.Clock.At(15, 17, 0);                     // the next boundary (turn-on) takes over
            Check(r.Scheduler.Advance().Count == 0, "17:00: the freshest boundary resumes the schedule, no jump");
            Check(!r.Scheduler.IsOverridden("A"), "the ephemeral value is done at the boundary");
            Check(r.Scheduler.Phase == NightPhase.Night, "and the night begins");

            r.Clock.At(15, 17, 30);
            Check(r.Scheduler.Target("A") == 38, "17:30: at the night level");

            r.Clock.At(16, 9, 31);
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 0, "and the morning starts its glide from where the display is");
            List<NightWrite> back = Run(r.Scheduler, r.Clock, Step, 16);
            Check(back.Count > 0 && back[back.Count - 1].Value == 70,
                "the morning returns the Default 70, not the slider's 50 (got " + Dump(back) + ")");
        }

        private static void ANightDragEndsAtTheMorning()
        {
            Console.WriteLine("EXP17: a drag during the night glides up, and is undone at the turn-off");
            var r = new Rig("17:00", "09:00", 38, 70);
            r.Clock.At(15, 22, 0);
            r.Scheduler.Advance();
            Run(r.Scheduler, r.Clock, Step, 16);
            Check(r.Scheduler.Target("A") == 38, "22:00: the schedule is holding 38");

            r.Scheduler.Request("A", 92);              // the user drags the slider
            Check(r.Scheduler.IsOverridden("A"), "the move stands the schedule down");
            Check(r.Scheduler.DayValue("A") == 70, "the Default is untouched (70)");
            Check(r.Scheduler.Advance().Count == 0, "the glide starts where the display is (38), so nothing jumps");

            List<NightWrite> glide = Run(r.Scheduler, r.Clock, Step, 27);    // 5.4 s for 54 points
            Check(glide.Count == 27 && glide[glide.Count - 1].Value == 92,
                "the dimming glides up to the value in even steps (got " + Dump(glide) + ")");

            r.Clock.At(16, 1, 0);
            Check(r.Scheduler.Advance().Count == 0, "01:00: the user's value stands");

            r.Clock.At(16, 9, 0);                      // the morning boundary
            Check(r.Scheduler.Advance().Count == 0, "09:00: the glide back starts where the display is (92), no jump");
            Check(!r.Scheduler.IsOverridden("A"), "the ephemeral value is done at the turn-off");
            Check(r.Scheduler.Phase == NightPhase.Day, "and the day begins");

            List<NightWrite> w = Run(r.Scheduler, r.Clock, Step, 11);        // 92 -> 70 is 22 points
            Check(w.Count == 11 && w[w.Count - 1].Value == 70, "and it lands on the Default 70 (got " + Dump(w) + ")");
        }

        private static void EditingTheDefaultEndsEphemeralValues()
        {
            Console.WriteLine("EXP17: editing the Default ends a standing slider value while it is not dimming");
            var r = new Rig("17:00", "09:00", 38, 80);
            r.Clock.At(15, 14, 0);
            r.Scheduler.Advance();
            r.Scheduler.Request("A", 50);              // the user drags
            Check(r.Scheduler.IsOverridden("A"), "the drag stands the schedule down");

            r.Settings.TrySetDaylight("90");           // then edits the Default
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(!r.Scheduler.IsOverridden("A"), "editing the Default clears the standing value");
            Check(r.Scheduler.DayValue("A") == 90, "the Default is 90 now");
            Check(w.Count == 0, "the glide begins where the display is, so nothing jumps (got " + Dump(w) + ")");

            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 10);       // 70 -> 90
            Check(up.Count > 0 && up[up.Count - 1].Value == 90, "and the display glides to the new Default (got " + Dump(up) + ")");
        }

        private static void SwitchingTheFeatureOffReturnsToDefault()
        {
            Console.WriteLine("EXP17: the switch off mid-night returns the display to the Default, smoothly");
            var r = new Rig("17:00", "09:00", 38, 70);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Run(r.Scheduler, r.Clock, Step, 16);
            Check(r.Scheduler.Target("A") == 38, "23:00: dimmed");

            r.Settings.SetEnabled(false);
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 0, "the glide starts where the display is, so nothing jumps (got " + Dump(w) + ")");
            Check(r.Scheduler.Phase == NightPhase.Off, "phase is Off");

            List<NightWrite> back = Run(r.Scheduler, r.Clock, Step, 16);
            Check(back.Count > 0 && NonDecreasing(back), "it glides back up (got " + Dump(back) + ")");
            Check(back[back.Count - 1].Value == 70, "and lands on the Default 70 (got "
                + back[back.Count - 1].Value.ToString(CultureInfo.InvariantCulture) + ")");

            Check(r.Scheduler.Advance().Count == 0, "then it stays quiet while off");

            r.Settings.SetEnabled(true);
            Check(r.Scheduler.Advance().Count == 0, "switching it back on starts the glide from where the display is");
            List<NightWrite> down = Run(r.Scheduler, r.Clock, Step, 16);
            Check(down.Count == 16 && down[down.Count - 1].Value == 38, "and it dims again (got " + Dump(down) + ")");
        }

        private static void SunsetModeAppliesTheDefault()
        {
            Console.WriteLine("EXP17: the sunset placeholder never dims, but the Default still governs");
            var r = new Rig("17:00", "09:00", 38, 70);
            r.Settings.SetMode(ScheduleMode.SunsetToSunrise);
            r.Clock.At(15, 23, 0);
            Check(r.Scheduler.Advance().Count == 0, "23:00 in sunset mode: nothing is written (the Default is the reading)");
            Check(r.Scheduler.Phase == NightPhase.Inert, "phase is Inert");
            Check(r.Scheduler.Target("A") == 70, "the display holds the Default");
            Check(r.Scheduler.Status.IndexOf("not configured", StringComparison.Ordinal) >= 0,
                "and the status says so (got '" + r.Scheduler.Status + "')");

            // A Default that differs from the reading DOES move the display, even though it never dims.
            var r2 = new Rig("17:00", "09:00", 38, 90);
            r2.Settings.SetMode(ScheduleMode.SunsetToSunrise);
            r2.Clock.At(15, 12, 0);
            Check(r2.Scheduler.Advance().Count == 0, "the glide starts where the display is");
            List<NightWrite> up = Run(r2.Scheduler, r2.Clock, Step, 10);
            Check(up.Count > 0 && up[up.Count - 1].Value == 90, "and it glides to the Default 90 (got " + Dump(up) + ")");

            r2.Settings.SetMode(ScheduleMode.SetHours);
            SetWindow(r2.Settings, "17:00", "17:00");
            Check(r2.Scheduler.Advance().Count == 0, "explicit equal hours are inert too");
            Check(r2.Scheduler.Phase == NightPhase.Inert, "same phase");
        }

        private static void StrengthAndHoursChangesGlide()
        {
            Console.WriteLine("editing the strength or the hours retargets the glide - nothing snaps");
            var r = new Rig("17:00", "09:00", 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Run(r.Scheduler, r.Clock, Step, 16);
            Check(r.Scheduler.Target("A") == 38, "23:00: holding 38");

            r.Settings.TrySetStrength("20");
            Check(r.Scheduler.Advance().Count == 0, "a strength change starts a glide from where the display is (38)");
            List<NightWrite> w = Run(r.Scheduler, r.Clock, Step, 9);         // 38 -> 20 is 18 points
            Check(w.Count == 9 && w[w.Count - 1].Value == 20, "and it glides to the new level 20 (got " + Dump(w) + ")");

            SetWindow(r.Settings, "22:00", "23:00");   // the window now ends at 23:00, so it is day again
            Check(r.Scheduler.Advance().Count == 0, "moving the window to end now starts the glide back");
            Check(r.Scheduler.Phase == NightPhase.Day, "phase follows the new window");
            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 25);       // 20 -> 70 is 50 points
            Check(up.Count == 25 && up[up.Count - 1].Value == 70, "and it glides back to the Default 70 (got " + Dump(up) + ")");
        }

        private static void RangesArePerDisplay()
        {
            Console.WriteLine("each display is scaled into its own range, and has its own baseline");
            var clock = new NightClock();
            var settings = new ScheduleSettings();
            SetWindow(settings, "17:00", "09:00");
            settings.TrySetStrength("38");
            var s = new NightLightScheduler(settings, delegate { return clock.Now; }, DaySpeed, DayGrain);
            s.Observe("A", 0, 100, 70);
            s.Observe("B", 20, 80, 60);
            clock.At(15, 23, 0);
            Check(s.Advance().Count == 0, "the glide starts where each display is");

            Run(s, clock, Step, 16);                 // the 0-100 display needs the full 32 points
            Check(s.Target("A") == 38, "0-100 display: 38");
            Check(s.Target("B") == 43, "20-80 display: 43, not 38");
            Check(s.Baseline("A") == 70 && s.Baseline("B") == 60, "each keeps its own reading");
            Check(s.Target("Z") == null, "an undetected display has no target at all");
            Check(s.Baseline("Z") == 0, "and no baseline to restore");
        }

        private static void IdempotentAndQuiet()
        {
            Console.WriteLine("advancing twice at the same moment changes nothing");
            var r = new Rig("17:00", "09:00", 38);
            r.Clock.At(15, 23, 0);
            Check(r.Scheduler.Advance().Count == 0, "23:00: the glide starts, nothing written yet");
            Check(r.Scheduler.Advance().Count == 0, "the very next tick at the same instant has nothing to do");
            Run(r.Scheduler, r.Clock, Step, 16);
            Check(r.Scheduler.Advance().Count == 0, "and once it has arrived there is nothing more to say");
            Check(r.Scheduler.Target("A") == 38, "the night level is held");

            r.Clock.At(16, 12, 0);
            Check(r.Scheduler.Advance().Count == 0, "a jump back to midday starts the glide from where it is");
            Run(r.Scheduler, r.Clock, Step, 16);
            Check(r.Scheduler.Target("A") == 70, "and it glides to the Default");
            Check(r.Scheduler.Phase == NightPhase.Day, "and the phase is Day");
            Check(r.Scheduler.Advance().Count == 0, "midday again: untouched");
        }

        private static void RestoreHandsTheValueBack()
        {
            Console.WriteLine("closing the app hands back the Default");
            var r = new Rig("17:00", "09:00", 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Run(r.Scheduler, r.Clock, Step, 16);      // it has actually dimmed
            Check(r.Scheduler.Target("A") == 38, "23:00: dimmed");

            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Restore()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 70, "Restore() writes the Default (got " + Dump(w) + ")");

            List<NightWrite> again = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Restore()) again.Add(x);
            Check(again.Count == 0, "a second Restore() has nothing left to do");

            var r2 = new Rig("17:00", "09:00", 38);
            r2.Clock.At(15, 12, 0);
            r2.Scheduler.Advance();
            Check(r2.Scheduler.Restore().Count == 0, "closing during the day writes nothing at all");
        }

        private static void StatusReportsThePhase()
        {
            Console.WriteLine("the status line names the phase - it is what the surface shows");
            var r = new Rig("17:00", "09:00", 38);
            r.Clock.At(15, 12, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status.IndexOf("daylight", StringComparison.Ordinal) >= 0
                && r.Scheduler.Status.IndexOf("38%", StringComparison.Ordinal) >= 0
                && r.Scheduler.Status.IndexOf("5:00 PM", StringComparison.Ordinal) >= 0,
                "day: '" + r.Scheduler.Status + "'");

            r.Clock.At(15, 17, 5);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status.IndexOf("38% until 9:00 AM", StringComparison.Ordinal) >= 0,
                "night: '" + r.Scheduler.Status + "'");

            r.Settings.SetEnabled(false);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status == "Night light: off", "off: '" + r.Scheduler.Status + "'");
        }

        // --- EXP17: the day value is configured, not read off the panel ---

        private static void TheDayValueIsTheDaylightSetting()
        {
            Console.WriteLine("EXP17: the day value is the configured daylight, not the reading");
            var r = new Rig("17:00", "09:00", 38, 90);
            Check(r.Scheduler.DayValue("A") == 90,
                "the day value is the configured 90, not the reading 70 (got "
                + r.Scheduler.DayValue("A").ToString(CultureInfo.InvariantCulture) + ")");

            r.Clock.At(15, 12, 0);
            Check(r.Scheduler.Advance().Count == 0,
                "12:00: the glide begins at the value the display has now, so nothing jumps");
            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 5);
            Check(up.Count > 0 && NonDecreasing(up), "the glide climbs (got " + Dump(up) + ")");
            Check(r.Scheduler.Target("A") == 80, "half way through the move it is at 80 (got "
                + r.Scheduler.Target("A").Value.ToString(CultureInfo.InvariantCulture) + ")");
            List<NightWrite> rest = Run(r.Scheduler, r.Clock, Step, 6);
            Check(rest.Count > 0 && rest[rest.Count - 1].Value == 90 && r.Scheduler.Target("A") == 90,
                "the move lands on the daylight 90 (last " + rest[rest.Count - 1].Value.ToString(CultureInfo.InvariantCulture)
                + ", target " + r.Scheduler.Target("A").Value.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static void ADisplayMetDimmedComesBackToDaylight()
        {
            Console.WriteLine("EXP17: a display met while dimmed still gets the configured day value back");
            var r = new Rig("17:00", "09:00", 38, 90);
            // The report: the app adopts the display at the night level (a restart mid-night / a
            // re-attach), so its reading is 38 - its day value must still be the configured 90.
            r.Scheduler.Observe("B", 0, 100, 38);
            Check(r.Scheduler.DayValue("B") == 90, "a display read at 38 still has 90 as its day value");
            Check(r.Scheduler.Baseline("B") == 38, "and 38 is only what it is handed back on exit");

            r.Clock.At(15, 17, 30);
            r.Scheduler.Advance();
            Run(r.Scheduler, r.Clock, Step, 16);
            Check(r.Scheduler.Target("A") == 38 && r.Scheduler.Target("B") == 38, "both end at the night level");

            r.Clock.At(16, 9, 31);
            Check(r.Scheduler.Advance().Count == 0, "the glide back starts where the display is");
            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 26);       // 38 -> 90 is 52 points
            bool backToDaylight = false;
            foreach (NightWrite x in up) if (x.Id == "B" && x.Value == 90) backToDaylight = true;
            Check(backToDaylight, "and B, met dimmed, ends the morning at the configured 90 (got " + Dump(up) + ")");
        }

        private static void TheNightRetargetsFromWhereTheDayIs()
        {
            Console.WriteLine("EXP17: a move still running at turn-on retargets from where the display is, no jump");
            var r = new Rig("17:00", "09:00", 38, 90);

            r.Clock.At(15, 16, 59);
            r.Scheduler.Advance();                       // the move to 90 begins from the reading (70)
            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 5);        // 1 s -> 80
            Check(up.Count > 0 && StrictlyIncreasing(up), "the move climbs (got " + Dump(up) + ")");
            int beforeNight = r.Scheduler.Target("A").Value;
            Check(beforeNight > 70 && beforeNight < 90,
                "still below the Default when the window opens (got " + beforeNight.ToString(CultureInfo.InvariantCulture) + ")");

            r.Clock.At(15, 17, 0);
            Check(r.Scheduler.Advance().Count == 0, "17:00: it turns around from where it is, so nothing jumps");
            Check(r.Scheduler.Phase == NightPhase.Night, "AND the phase is Night");
            List<NightWrite> down = Run(r.Scheduler, r.Clock, Step, (beforeNight - 38) / 2);
            Check(down.Count > 0 && NonIncreasing(down) && down[0].Value < beforeNight,
                "it only goes down from there, never up to the Default first (got " + Dump(down) + ")");
        }

        private static void EditingTheDaylightReanchorsTheRamp()
        {
            Console.WriteLine("EXP17: editing the daylight mid-move re-anchors it, with no jump");
            var r = new Rig("17:00", "09:00", 38, 90);
            r.Clock.At(15, 12, 0);
            r.Scheduler.Advance();                       // the move 70 -> 90 begins
            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 5);        // half the move in
            Check(up.Count > 0 && NonDecreasing(up) && r.Scheduler.Target("A") == 80,
                "half way in, the display is at 80 (got " + Dump(up) + ")");

            r.Settings.TrySetDaylight("60");             // the user lowers the day value
            List<NightWrite> down = Run(r.Scheduler, r.Clock, Step, 11);
            Check(NonIncreasing(down), "the move turns around and never steps up (got " + Dump(down) + ")");
            Check(r.Scheduler.Target("A") == 60, "and its target is the new 60 (got "
                + r.Scheduler.Target("A").Value.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static void TheGoalIsTheInstruction()
        {
            Console.WriteLine("EXP17: the goal is where the display is headed - the knobs land on it, the dimming catches up");
            var r = new Rig("17:00", "09:00", 38, 70);
            r.Clock.At(15, 12, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Goal("A") == 70, "by day the goal is the Default");

            r.Settings.TrySetDaylight("90");
            r.Scheduler.Advance();
            Check(r.Scheduler.Goal("A") == 90, "a Default edit moves the goal at once - the knob lands on it");
            Check(r.Scheduler.Target("A") == 70, "while the value itself is still on its way (got "
                + r.Scheduler.Target("A").Value.ToString(CultureInfo.InvariantCulture) + ")");

            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Goal("A") == 38, "at night the goal is the night level");

            r.Scheduler.Request("A", 50);
            Check(r.Scheduler.Goal("A") == null, "and none while the user's own value stands");

            r.Clock.At(16, 9, 31);
            r.Scheduler.Advance();
            Check(r.Scheduler.Goal("A") == 90, "back in daylight the goal is the Default again, not the slider's 50 (got "
                + (r.Scheduler.Goal("A").HasValue ? r.Scheduler.Goal("A").Value.ToString(CultureInfo.InvariantCulture) : "null") + ")");
        }

        private static void TheFeatureOffAppliesTheDefault()
        {
            Console.WriteLine("EXP17: with the night light off, the Default governs - and changing it moves the displays");
            var r = new Rig("17:00", "09:00", 38, 90);
            r.Settings.SetEnabled(false);
            r.Clock.At(15, 12, 0);
            Check(r.Scheduler.Advance().Count == 0, "the glide starts at the value the display has now");
            Check(r.Scheduler.Phase == NightPhase.Off, "phase is Off");

            List<NightWrite> up = Run(r.Scheduler, r.Clock, Step, 10);       // 70 -> 90
            Check(up.Count > 0 && NonDecreasing(up), "the display glides to the Default even with the feature off (got " + Dump(up) + ")");
            Check(up[up.Count - 1].Value == 90 && r.Scheduler.Target("A") == 90,
                "and lands on the Default 90 (got "
                + up[up.Count - 1].Value.ToString(CultureInfo.InvariantCulture) + ")");

            r.Settings.TrySetDaylight("40");             // changing it moves the display again
            Check(r.Scheduler.Advance().Count == 0, "the turn-around starts where the display is");
            List<NightWrite> down = Run(r.Scheduler, r.Clock, Step, 25);     // 90 -> 40
            Check(NonIncreasing(down), "changing the Default turns the display around, no jump (got " + Dump(down) + ")");
            Check(r.Scheduler.Target("A") == 40, "and it heads for the new Default 40 (got "
                + r.Scheduler.Target("A").Value.ToString(CultureInfo.InvariantCulture) + ")");
        }
    }
}
