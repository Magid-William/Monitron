// EXP07 - drives SettingsCodec and SettingsStore and asserts what comes back.
//
// The codec is pure, so the round trip and every corrupt-file case are string-in/state-out. The
// store is exercised on a real temp file (that is the point of it): a missing file, a write/replace,
// and a path that cannot be written. No test framework, no clock - built as a console exe by
// persistence-test.ps1 (a winexe cannot print), and deliberately NOT part of build.ps1.

using System;
using System.IO;

namespace Monitron.App
{
    internal static class SettingsPersistenceTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        private static string _dir;

        private static int Main()
        {
            Console.WriteLine("SettingsPersistence - codec round trips and store reads/writes");

            _dir = Path.Combine(Path.GetTempPath(), "monitron-exp07-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            try
            {
                Codec();
                Store();
                ChangedEvents();
            }
            finally
            {
                try { Directory.Delete(_dir, true); } catch { }
            }

            Console.WriteLine();
            Console.WriteLine("{0} passed, {1} failed", _passed, _failed);
            return _failed == 0 ? 0 : 1;
        }

        // --- the document, as text ---

        private static void Codec()
        {
            Console.WriteLine("the document shape");
            var d = new ScheduleSettings();
            string text = SettingsCodec.Serialize(d);
            Check(text.Contains("version=1"), "carries version=1");
            Check(text.Contains("enabled=true") && text.Contains("strength=38") && text.Contains("daylight=80")
                && text.Contains("gradualFade=true") && text.Contains("mode=SetHours")
                && text.Contains("turnOn=17:00") && text.Contains("turnOff=09:00"),
                "defaults spelled out as key=value lines");
            Check(SettingsCodec.Clock(new TimeSpan(0, 0, 0)) == "00:00"
                && SettingsCodec.Clock(new TimeSpan(23, 59, 0)) == "23:59",
                "clock is 24-hour " + "\"HH:mm\"" + ", midnight to 23:59");

            Console.WriteLine("round trip: defaults");
            var a = new ScheduleSettings();
            var b = new ScheduleSettings();
            int known = SettingsCodec.Parse(SettingsCodec.Serialize(a), b);
            Check(known == 8, "eight recognized keys (got " + known + ")");
            Check(Same(a, b), "parsing a serialized default model reproduces it exactly");

            Console.WriteLine("round trip: every field moved");
            var moved = new ScheduleSettings();
            moved.SetEnabled(false);
            moved.TrySetStrength("55");
            moved.TrySetDaylight("65");
            moved.ToggleGradualFade();
            moved.SetMode(ScheduleMode.SunsetToSunrise);
            moved.TrySetTurnOn(6, 5, false);     // 6:05 AM
            moved.TrySetTurnOff(11, 30, true);   // 11:30 PM
            var restored = new ScheduleSettings();
            SettingsCodec.Parse(SettingsCodec.Serialize(moved), restored);
            Check(Same(moved, restored), "off, 55, 65 daylight, no fade, sunset, 6:05 AM -> 11:30 PM all survive");

            Console.WriteLine("a corrupt line cannot wipe the rest");
            var c = new ScheduleSettings();
            SettingsCodec.Parse("strength=55\nbanana=ripe\nturnOn=99:99\n; c\n# c\n\n  turnOff = 06:30  ", c);
            Check(c.Strength == 55, "the one good strength line applied");
            Check(c.TurnOffText == "6:30 AM", "a padded turnOff line applied");
            Check(c.Enabled && c.GradualFade && c.Mode == ScheduleMode.SetHours,
                "the untouched fields kept their defaults");
            Check(c.TurnOnText == "5:00 PM", "an unparsable clock line (99:99) left turn-on alone");

            Console.WriteLine("refused and unknown values");
            var e = new ScheduleSettings();
            SettingsCodec.Parse("strength=999", e);
            Check(e.Strength == 38, "strength=999 is refused through the model's own rule, default kept");
            SettingsCodec.Parse("daylight=999", e);
            Check(e.Daylight == 80, "daylight=999 is refused the same way, default 80 kept");
            SettingsCodec.Parse("daylight=64", e);
            Check(e.Daylight == 64, "and a good daylight line applies");
            Check(SettingsCodec.Parse("banana=1\nhello\n", new ScheduleSettings()) == 0,
                "a document with no recognized key reports 0");
            Check(SettingsCodec.Parse(null, new ScheduleSettings()) == 0, "null text is 0, not a crash");

            var sun = new ScheduleSettings();
            sun.SetMode(ScheduleMode.SunsetToSunrise);
            SettingsCodec.Parse("mode=banana", sun);
            Check(sun.Mode == ScheduleMode.SunsetToSunrise,
                "an unrecognized mode is left as it is, not coerced");
        }

        // --- the file ---

        private static void Store()
        {
            string path = Path.Combine(_dir, "settings.ini");

            Console.WriteLine("a missing file is the built-in defaults");
            var store = new SettingsStore(path);
            Check(!File.Exists(path), "the file really does not exist yet");
            ScheduleSettings fresh = store.Load();
            var defaults = new ScheduleSettings();
            Check(Same(fresh, defaults), "Load() on a missing file yields the defaults");
            Check(store.LastError == null, "and reports no error");

            Console.WriteLine("save, then load it back");
            var saved = new ScheduleSettings();
            saved.TrySetStrength("55");
            saved.ToggleGradualFade();
            Check(store.Save(saved), "Save() succeeded");
            Check(File.Exists(path), "the file is there");
            Check(!File.Exists(path + ".tmp"), "no .tmp left behind (the write was atomic)");
            Check(Same(store.Load(), saved), "Load() gives back exactly what was saved");

            Console.WriteLine("a second save replaces the first");
            var second = new ScheduleSettings();
            second.TrySetStrength("7");
            Check(store.Save(second), "the replacing Save() succeeded");
            Check(Same(store.Load(), second), "and the file now holds the second set");
            Check(!File.Exists(path + ".tmp"), "still no .tmp");

            Console.WriteLine("partial and corrupt files on disk");
            File.WriteAllText(path, "strength=23\n");
            ScheduleSettings partial = store.Load();
            Check(partial.Strength == 23, "a one-line file applies that line");
            Check(partial.Mode == ScheduleMode.SetHours && partial.Enabled,
                "and leaves the rest at the defaults");

            File.WriteAllText(path, "this is not a settings file at all\n");
            ScheduleSettings garbage = store.Load();
            Check(Same(garbage, defaults), "a document with nothing recognizable loads as defaults");
            Check(store.LastError == null, "and is not an error - it simply had nothing to say");

            Console.WriteLine("a path that cannot be written");
            string blocker = Path.Combine(_dir, "blocker");
            File.WriteAllText(blocker, "a file, not a folder");
            var bad = new SettingsStore(Path.Combine(blocker, "settings.ini"));
            Check(!bad.Save(saved), "Save() reports failure instead of throwing");
            Check(bad.LastError != null, "and records why");

            Console.WriteLine("the real location");
            string real = SettingsStore.DefaultPath();
            Check(real.EndsWith(Path.Combine("Monitron", "settings.ini")),
                "DefaultPath ends in Monitron\\settings.ini (got " + real + ")");
        }

        // --- the hook the app saves on ---

        private static void ChangedEvents()
        {
            Console.WriteLine("Changed fires for a real transition, and only then");

            var s = new ScheduleSettings();
            int fired = 0;
            s.Changed += delegate { fired++; };

            s.TrySetStrength("55");
            Check(fired == 1, "an accepted strength fires once");
            s.TrySetStrength("55");
            Check(fired == 1, "setting the value it already holds fires nothing");
            s.TrySetStrength("999");
            Check(fired == 1, "a refused strength fires nothing");

            s.TrySetDaylight("64");
            Check(fired == 2, "an accepted daylight fires once");
            s.TrySetDaylight("64");
            Check(fired == 2, "setting the same daylight fires nothing");
            s.TrySetDaylight("999");
            Check(fired == 2, "a refused daylight fires nothing");

            s.ToggleGradualFade();
            Check(fired == 3, "toggling the fade fires");
            s.SetEnabled(false);
            Check(fired == 4, "turning the feature off fires");
            s.SetEnabled(false);
            Check(fired == 4, "a no-op set fires nothing");
            s.SetMode(ScheduleMode.SunsetToSunrise);
            Check(fired == 5, "changing the mode fires");
            s.SetMode(ScheduleMode.SunsetToSunrise);
            Check(fired == 5, "the same mode again fires nothing");
            s.TrySetTurnOn(6, 5, false);
            Check(fired == 6, "a changed turn-on fires");
            s.TrySetTurnOn(6, 5, false);
            Check(fired == 6, "the same turn-on fires nothing");
            s.TrySetTurnOff(12, 60, true);
            Check(fired == 6, "a refused clock fires nothing");
        }

        private static bool Same(ScheduleSettings x, ScheduleSettings y)
        {
            return x.Enabled == y.Enabled
                && x.Strength == y.Strength
                && x.Daylight == y.Daylight
                && x.GradualFade == y.GradualFade
                && x.Mode == y.Mode
                && x.TurnOn == y.TurnOn
                && x.TurnOff == y.TurnOff;
        }
    }
}
