// EXP04 - drives ScheduleSettings through its transitions and asserts the exposed state.
//
// No test framework and no clock: the model is advanced by invoking its own transitions, which
// is the AGENTS rule for machine logic. Built as a console exe by model-test.ps1 (a winexe
// cannot print), and deliberately NOT part of build.ps1 - it must not race with Program.Main.

using System;
using System.Globalization;

namespace Monitorn.App
{
    internal static class ScheduleSettingsTests
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
            Console.WriteLine("ScheduleSettings - transitions driven, state asserted");

            var s = new ScheduleSettings();
            Check(s.Enabled && s.Strength == 38 && s.GradualFade && s.Mode == ScheduleMode.SetHours,
                "defaults: on, 38 %, gradual fade, Set hours");
            Check(s.TurnOnText == "5:00 PM" && s.TurnOffText == "9:00 AM",
                "defaults: turn on 5:00 PM, turn off 9:00 AM");

            Console.WriteLine("mode coupling");
            Check(s.BodyEnabled && s.SetHoursFieldsEnabled, "Set hours: body and time fields live");
            s.SetMode(ScheduleMode.SunsetToSunrise);
            Check(s.BodyEnabled && !s.SetHoursFieldsEnabled, "Sunset: body live, time fields off");
            Check(s.TurnOnText == "5:00 PM" && s.TurnOffText == "9:00 AM", "Sunset: times retained");
            Check(s.SunsetToSunriseLabel == "Sunset to sunrise (12:00 AM - 12:00 AM)",
                "Sunset: label carries the dummy range");
            s.SetMode(ScheduleMode.SetHours);
            Check(s.SetHoursFieldsEnabled, "back to Set hours: time fields live again");

            Console.WriteLine("the enable toggle gates the body");
            s.SetEnabled(false);
            Check(!s.BodyEnabled, "off: body off");
            Check(!s.SetHoursFieldsEnabled, "off: time fields off even in Set hours");
            s.SetEnabled(true);
            Check(s.BodyEnabled && s.SetHoursFieldsEnabled, "on again: body and fields live");
            s.ToggleEnabled();
            Check(!s.Enabled, "toggle flips the feature off");
            s.ToggleEnabled();
            Check(s.Enabled, "toggle flips it back on");

            Console.WriteLine("strength");
            Check(s.TrySetStrength("55") && s.Strength == 55, "55 accepted");
            Check(s.TrySetStrength(" 7 ") && s.Strength == 7, "whitespace trimmed");
            Check(s.TrySetStrength("100") && s.Strength == 100, "100 accepted (top of range)");
            Check(s.TrySetStrength("0") && s.Strength == 0, "0 accepted (bottom of range)");
            Check(!s.TrySetStrength("101") && s.Strength == 0, "101 refused, 0 kept");
            Check(!s.TrySetStrength("") && s.Strength == 0, "empty refused, 0 kept");
            Check(!s.TrySetStrength("-3") && s.Strength == 0, "-3 refused, 0 kept");
            Check(!s.TrySetStrength("abc") && s.Strength == 0, "non-numeric refused, 0 kept");
            Check(!s.TrySetStrength(null) && s.Strength == 0, "null refused, 0 kept");
            Check(s.TrySetStrength("38") && s.Strength == 38, "back to the sketch's 38");

            Console.WriteLine("times: 12-hour clock, midnight and noon included");
            Check(s.TrySetTurnOn(5, 0, true) && s.TurnOnText == "5:00 PM", "5:00 PM from parts");
            Check(s.TrySetTurnOff(9, 0, false) && s.TurnOffText == "9:00 AM", "9:00 AM from parts");
            Check(s.TrySetTurnOff(12, 0, false) && s.TurnOffText == "12:00 AM", "12 AM is midnight");
            Check(s.TrySetTurnOff(12, 0, true) && s.TurnOffText == "12:00 PM", "12 PM is noon");
            Check(s.TrySetTurnOff(1, 5, true) && s.TurnOffText == "1:05 PM", "minutes are two digits");
            Check(!s.TrySetTurnOff(13, 0, true) && s.TurnOffText == "1:05 PM", "hour 13 refused, kept");
            Check(!s.TrySetTurnOff(0, 0, true) && s.TurnOffText == "1:05 PM", "hour 0 refused, kept");
            Check(!s.TrySetTurnOff(12, 60, true) && s.TurnOffText == "1:05 PM", "minute 60 refused, kept");
            Check(!s.TrySetTurnOff(12, -1, true) && s.TurnOffText == "1:05 PM", "minute -1 refused, kept");
            Check(s.TrySetTurnOff(9, 0, false) && s.TurnOffText == "9:00 AM", "restored 9:00 AM");

            Console.WriteLine("round trip: format -> parts -> format");
            var t = new TimeSpan(23, 59, 0);
            int hour12, minute; bool pm;
            ScheduleSettings.ToParts(t, out hour12, out minute, out pm);
            Check(hour12 == 11 && minute == 59 && pm, "23:59 -> 11:59 PM");
            Check(ScheduleSettings.FormatTime(t) == "11:59 PM", "23:59 formats as 11:59 PM");
            ScheduleSettings.ToParts(TimeSpan.Zero, out hour12, out minute, out pm);
            Check(hour12 == 12 && minute == 0 && !pm, "00:00 -> 12:00 AM");

            Console.WriteLine("gradual fade");
            Check(s.GradualFade, "starts on");
            s.ToggleGradualFade();
            Check(!s.GradualFade, "off");
            s.ToggleGradualFade();
            Check(s.GradualFade, "on again");

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }
    }
}
