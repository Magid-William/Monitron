// EXP06 - drives NightLightScheduler through its transitions and asserts the exposed state.
//
// The clock only moves when the test says so, so a whole night - fade down, hold, fade back, the
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

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static int Main()
        {
            Console.WriteLine("NightLightScheduler - clock driven, state asserted");

            PureHelpers();
            CulminatesInTheNight();
            TheFadeIsABoundedStaircase();
            AWholeNightCostsBoundedWrites();
            FadeOffIsOneStep();
            LaunchInsideTheWindowStepsAtOnce();
            OverrideEndsTheNight();
            OverrideLapsesAtTheNextTurnOn();
            UserMoveByDayIsNotAnOverride();
            SwitchingTheFeatureOffRestores();
            SunsetModeIsInert();
            StrengthChangesTakeEffectAtOnce();
            RangesArePerDisplay();
            IdempotentAndQuietByDay();
            RestoreHandsTheDayValueBack();
            StatusReportsThePhase();

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

            public Rig(string on, string off, int fadeMinutes, int stepMinutes, int strength)
            {
                Clock.At(15, 16, 0);
                SetWindow(Settings, on, off);
                Settings.TrySetStrength(strength.ToString(CultureInfo.InvariantCulture));
                Scheduler = new NightLightScheduler(Settings, delegate { return Clock.Now; },
                    TimeSpan.FromMinutes(fadeMinutes), TimeSpan.FromMinutes(stepMinutes));
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

        // --- the assertions ---

        private static void PureHelpers()
        {
            Console.WriteLine("pure helpers: the window, the elapsed time, the level");
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

            Check(NightLightScheduler.Since(new TimeSpan(17, 0, 0), new TimeSpan(23, 0, 0)) == TimeSpan.FromHours(6),
                "elapsed since a boundary is measured forward");
            Check(NightLightScheduler.Since(new TimeSpan(17, 0, 0), new TimeSpan(2, 0, 0)) == TimeSpan.FromHours(9),
                "and wraps past midnight (17:00 -> 02:00 is 9 h)");
            Check(NightLightScheduler.Since(new TimeSpan(9, 0, 0), new TimeSpan(9, 5, 0)) == TimeSpan.FromMinutes(5),
                "five minutes after turn-off is five minutes");

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

        private static void CulminatesInTheNight()
        {
            Console.WriteLine("the day is untouched, and the night ends up at the strength");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            Check(r.Scheduler.Advance().Count == 0, "16:00: the schedule wants nothing");
            Check(r.Scheduler.Target("A") == 70, "16:00: the display is left at its day value (70)");
            Check(r.Scheduler.Phase == NightPhase.Day, "16:00: phase is Day");
            Check(r.Scheduler.Engaged == false, "16:00: the schedule does not own the display");

            r.Clock.At(15, 17, 0);
            Check(r.Scheduler.Advance().Count == 0, "17:00 sharp: the ramp starts at the day value, so nothing changes");
            Check(r.Scheduler.Phase == NightPhase.FadingIn, "17:00: phase is FadingIn");

            r.Clock.At(15, 23, 0);
            Check(r.Scheduler.Advance().Count == 1, "23:00: one write to reach the night level");
            Check(r.Scheduler.Target("A") == 38, "23:00: the display is at the strength (38)");
            Check(r.Scheduler.Phase == NightPhase.Night, "23:00: phase is Night");

            r.Clock.At(16, 2, 0);
            Check(r.Scheduler.Advance().Count == 0, "02:00 next day: still the night, nothing to change");
            Check(r.Scheduler.Target("A") == 38, "02:00: still 38 across midnight (the window wraps)");
            Check(r.Scheduler.Phase == NightPhase.Night, "02:00: phase is still Night");

            r.Clock.At(16, 9, 0);
            Check(r.Scheduler.Advance().Count == 0, "09:00: the night ends exactly here, value unchanged yet");
            Check(r.Scheduler.Phase == NightPhase.FadingOut, "09:00: phase is FadingOut");

            r.Clock.At(16, 9, 30);
            Check(r.Scheduler.Advance().Count == 1, "09:30: the fade back ends with one write");
            Check(r.Scheduler.Target("A") == 70, "09:30: the display is back at its own day value (70)");
            Check(r.Scheduler.Phase == NightPhase.Day, "09:30: phase is Day again");

            r.Clock.At(16, 12, 0);
            Check(r.Scheduler.Advance().Count == 0, "12:00: the day is quiet again");
        }

        private static void TheFadeIsABoundedStaircase()
        {
            Console.WriteLine("the fade steps down monotonically, never below the target");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 16, 59);
            r.Scheduler.Advance();

            List<NightWrite> down = Run(r.Scheduler, r.Clock, TimeSpan.FromMinutes(1), 31);   // 17:00 .. 17:30
            Check(down.Count == 30, "30 steps for a 30-minute fade (got " + down.Count.ToString(CultureInfo.InvariantCulture) + ")");
            Check(NonIncreasing(down), "every step is at or below the one before it");
            Check(down[down.Count - 1].Value == 38, "the last step lands exactly on the strength (38)");
            Check(down[0].Value == 69, "the first step is one increment below the day value (got "
                + down[0].Value.ToString(CultureInfo.InvariantCulture) + ")");
            bool overshoot = false;
            foreach (NightWrite w in down) if (w.Value < 38) overshoot = true;
            Check(!overshoot, "no step undershoots the night level");

            // and the same staircase on the way back up
            var r2 = new Rig("17:00", "09:00", 30, 1, 38);
            r2.Clock.At(16, 0, 0);   // midnight: at the night level
            r2.Scheduler.Advance();
            List<NightWrite> up = Run(r2.Scheduler, r2.Clock, TimeSpan.FromMinutes(1), 571);  // 00:01 .. 09:31
            Check(up.Count == 30, "the fade back is a staircase too: 29 steps plus the day value (got "
                + up.Count.ToString(CultureInfo.InvariantCulture) + ")");
            Check(NonDecreasing(up), "every step is at or above the one before it");
            Check(up[up.Count - 1].Value == 70, "and it ends exactly on the day value (70)");
        }

        private static void AWholeNightCostsBoundedWrites()
        {
            Console.WriteLine("a fine tick costs nothing: a whole night is at most fade/step writes each way");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 16, 59);
            r.Scheduler.Advance();

            List<NightWrite> all = new List<NightWrite>();
            for (int i = 0; i < 60 * 13; i++)                    // 16:59 -> 05:59, every 15 seconds
            {
                r.Clock.Advance(TimeSpan.FromSeconds(15));
                foreach (NightWrite w in r.Scheduler.Advance()) all.Add(w);
            }
            Check(all.Count == 30, "ticking 4x a minute from 17:00 to 06:00 still costs 30 writes (got "
                + all.Count.ToString(CultureInfo.InvariantCulture) + ")");
            Check(all[all.Count - 1].Value == 38, "and the last one is the night level");

            List<NightWrite> same = new List<NightWrite>();
            for (int i = 0; i < 60 * 4; i++)                     // 06:00 -> 07:00, four times a minute
            {
                r.Clock.Advance(TimeSpan.FromSeconds(15));
                foreach (NightWrite w in r.Scheduler.Advance()) same.Add(w);
            }
            Check(same.Count == 0, "and a tick during the hold writes nothing at all");
        }

        private static void FadeOffIsOneStep()
        {
            Console.WriteLine("gradual fade off: one step at each boundary");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Settings.ToggleGradualFade();

            r.Clock.At(15, 16, 59);
            Check(r.Scheduler.Advance().Count == 0, "16:59: still the day");
            r.Clock.At(15, 17, 0);
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 38, "17:00: straight to 38 in one write (got " + Dump(w) + ")");
            Check(r.Scheduler.Phase == NightPhase.Night, "17:00: phase jumps to Night");

            r.Clock.At(16, 9, 0);
            w.Clear();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 70, "09:00: straight back to 70 in one write (got " + Dump(w) + ")");
            Check(r.Scheduler.Phase == NightPhase.Day, "09:00: phase is Day");
        }

        private static void LaunchInsideTheWindowStepsAtOnce()
        {
            Console.WriteLine("launched at night, the display is dimmed at once (not faded from the clock's past)");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 22, 0);
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 38, "22:00 at launch: one write to the night level (got " + Dump(w) + ")");
            Check(r.Scheduler.Phase == NightPhase.Night, "and the phase is already Night");
        }

        private static void OverrideEndsTheNight()
        {
            Console.WriteLine("a manual move inside the window ends the schedule for that night");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Target("A") == 38, "23:00: the schedule is holding 38");

            r.Scheduler.Request("A", 80);              // the user drags the slider
            Check(r.Scheduler.IsOverridden("A"), "the move is recorded as an override");
            Check(r.Scheduler.Target("A") == null, "the schedule has no opinion any more");
            Check(r.Scheduler.Baseline("A") == 80, "and the user's value becomes the day value");

            r.Clock.At(15, 23, 30);
            Check(r.Scheduler.Advance().Count == 0, "23:30: it does not put the display back");
            r.Clock.At(16, 1, 0);
            Check(r.Scheduler.Advance().Count == 0, "01:00: still not fighting the user");
            Check(r.Scheduler.IsOverridden("A"), "the override survives the rest of the night");

            r.Clock.At(16, 9, 0);
            Check(r.Scheduler.Advance().Count == 0, "09:00: turn-off writes nothing - the user's 80 stands");
            Check(r.Scheduler.IsOverridden("A"), "the override lapses only at the NEXT turn-on");
        }

        private static void OverrideLapsesAtTheNextTurnOn()
        {
            Console.WriteLine("...and the next turn-on re-arms the schedule from the user's value");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            r.Scheduler.Request("A", 80);

            r.Clock.At(16, 12, 0);                     // the next day, still in the day window
            Check(r.Scheduler.Advance().Count == 0, "daytime: quiet");
            Check(r.Scheduler.IsOverridden("A"), "the override has not lapsed yet");

            r.Clock.At(16, 17, 0);
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(!r.Scheduler.IsOverridden("A"), "17:00 the next day: the override lapses");
            Check(r.Scheduler.Phase == NightPhase.FadingIn, "and the ramp starts again");
            Check(w.Count == 0, "from the user's own value, so the first step is a no-op");

            r.Clock.At(16, 17, 1);
            w.Clear();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 79, "one minute later it steps down from 80 (got " + Dump(w) + ")");
        }

        private static void UserMoveByDayIsNotAnOverride()
        {
            Console.WriteLine("a move during the day is just the day value - no override needed");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 12, 0);
            r.Scheduler.Advance();
            r.Scheduler.Request("A", 85);
            Check(!r.Scheduler.IsOverridden("A"), "a daytime move is not an override");
            Check(r.Scheduler.Advance().Count == 0, "and the schedule has nothing to correct");
            Check(r.Scheduler.Target("A") == 85, "the new day value is used");

            r.Clock.At(15, 17, 15);
            r.Scheduler.Advance();
            Check(r.Scheduler.Target("A") == 61,
                "17:15: the ramp runs from the new day value (85 -> 38 is 61 at half way, got "
                + r.Scheduler.Target("A").Value.ToString(CultureInfo.InvariantCulture) + ")");
        }

        private static void SwitchingTheFeatureOffRestores()
        {
            Console.WriteLine("the switch off mid-night hands the display back");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Target("A") == 38, "23:00: dimmed");

            r.Settings.SetEnabled(false);
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 70, "switching it off writes the day value back (got " + Dump(w) + ")");
            Check(r.Scheduler.Phase == NightPhase.Off, "phase is Off");
            Check(!r.Scheduler.Engaged, "and the schedule lets go");

            r.Clock.At(15, 23, 30);
            Check(r.Scheduler.Advance().Count == 0, "and it stays quiet while off");

            r.Settings.SetEnabled(true);
            w.Clear();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 38, "switching it back on dims again (got " + Dump(w) + ")");
        }

        private static void SunsetModeIsInert()
        {
            Console.WriteLine("sunset-to-sunrise has no location yet, so it does nothing");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Settings.SetMode(ScheduleMode.SunsetToSunrise);
            r.Clock.At(15, 23, 0);
            Check(r.Scheduler.Advance().Count == 0, "23:00 in sunset mode: nothing is written");
            Check(r.Scheduler.Phase == NightPhase.Inert, "phase is Inert");
            Check(r.Scheduler.Target("A") == 70, "the display keeps its day value");
            Check(r.Scheduler.Status.IndexOf("not configured", StringComparison.Ordinal) >= 0,
                "and the status says so (got '" + r.Scheduler.Status + "')");

            r.Settings.SetMode(ScheduleMode.SetHours);
            SetWindow(r.Settings, "17:00", "17:00");
            Check(r.Scheduler.Advance().Count == 0, "explicit equal hours are inert too");
            Check(r.Scheduler.Phase == NightPhase.Inert, "same phase");
        }

        private static void StrengthChangesTakeEffectAtOnce()
        {
            Console.WriteLine("editing the strength or the times while running takes effect on the next tick");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Target("A") == 38, "23:00: holding 38");

            r.Settings.TrySetStrength("20");
            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 20, "strength 20 is written on the next tick (got " + Dump(w) + ")");

            SetWindow(r.Settings, "22:00", "23:00");
            w.Clear();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(r.Scheduler.Phase == NightPhase.FadingOut && w.Count == 0,
                "moving the window to end now starts the fade back from where the display is");
            r.Clock.At(15, 23, 31);
            w.Clear();
            foreach (NightWrite x in r.Scheduler.Advance()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 70, "half an hour later the display has its day value back (got " + Dump(w) + ")");
            Check(r.Scheduler.Phase == NightPhase.Day, "phase follows the new window");
        }

        private static void RangesArePerDisplay()
        {
            Console.WriteLine("each display is scaled into its own range, and has its own baseline");
            var clock = new NightClock();
            var settings = new ScheduleSettings();
            SetWindow(settings, "17:00", "09:00");
            settings.TrySetStrength("38");
            var s = new NightLightScheduler(settings, delegate { return clock.Now; },
                TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(1));
            s.Observe("A", 0, 100, 70);
            s.Observe("B", 20, 80, 60);
            clock.At(15, 23, 0);

            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in s.Advance()) w.Add(x);
            Check(w.Count == 2, "both displays are written (got " + Dump(w) + ")");
            Check(s.Target("A") == 38, "0-100 display: 38");
            Check(s.Target("B") == 43, "20-80 display: 43, not 38");
            Check(s.Baseline("A") == 70 && s.Baseline("B") == 60, "each keeps its own day value");
            Check(s.Target("Z") == null, "an undetected display has no target at all");
            Check(s.Baseline("Z") == 0, "and no baseline to restore");
        }

        private static void IdempotentAndQuietByDay()
        {
            Console.WriteLine("advancing twice at the same moment changes nothing");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 23, 0);
            Check(r.Scheduler.Advance().Count == 1, "23:00: one write on arrival");
            Check(r.Scheduler.Advance().Count == 0, "the very next tick has nothing to do");
            Check(r.Scheduler.Advance().Count == 0, "and neither has the one after");

            r.Clock.At(16, 12, 0);
            Check(r.Scheduler.Advance().Count == 1, "a jump from night to midday lands the day value in one write");
            Check(r.Scheduler.Target("A") == 70, "and the display is back at its own day value");
            Check(r.Scheduler.Advance().Count == 0, "midday again: untouched");
            Check(r.Scheduler.Phase == NightPhase.Day, "and the phase is Day");
        }

        private static void RestoreHandsTheDayValueBack()
        {
            Console.WriteLine("closing the app hands the day value back");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Engaged, "at night the schedule owns the display");

            List<NightWrite> w = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Restore()) w.Add(x);
            Check(w.Count == 1 && w[0].Value == 70, "Restore() writes the day value (got " + Dump(w) + ")");
            Check(!r.Scheduler.Engaged, "and lets go of the display");

            List<NightWrite> again = new List<NightWrite>();
            foreach (NightWrite x in r.Scheduler.Restore()) again.Add(x);
            Check(again.Count == 0, "a second Restore() has nothing left to do");

            var r2 = new Rig("17:00", "09:00", 30, 1, 38);
            r2.Clock.At(15, 12, 0);
            r2.Scheduler.Advance();
            Check(r2.Scheduler.Restore().Count == 0, "closing during the day writes nothing at all");
        }

        private static void StatusReportsThePhase()
        {
            Console.WriteLine("the status line names the phase - it is what the surface shows");
            var r = new Rig("17:00", "09:00", 30, 1, 38);
            r.Clock.At(15, 12, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status.IndexOf("daylight", StringComparison.Ordinal) >= 0
                && r.Scheduler.Status.IndexOf("38%", StringComparison.Ordinal) >= 0
                && r.Scheduler.Status.IndexOf("5:00 PM", StringComparison.Ordinal) >= 0,
                "day: '" + r.Scheduler.Status + "'");

            r.Clock.At(15, 17, 5);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status.IndexOf("fading to 38%", StringComparison.Ordinal) >= 0,
                "fading in: '" + r.Scheduler.Status + "'");

            r.Clock.At(15, 23, 0);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status.IndexOf("38% until 9:00 AM", StringComparison.Ordinal) >= 0,
                "night: '" + r.Scheduler.Status + "'");

            r.Clock.At(16, 9, 5);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status.IndexOf("returning to daytime", StringComparison.Ordinal) >= 0,
                "fading out: '" + r.Scheduler.Status + "'");

            r.Settings.SetEnabled(false);
            r.Scheduler.Advance();
            Check(r.Scheduler.Status == "Night light: off", "off: '" + r.Scheduler.Status + "'");
        }
    }
}
