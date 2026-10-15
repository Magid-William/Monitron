// EXP08 - drives the theme model through its own rule and asserts the palettes.
//
// No test framework, no WPF, no OS: Theme.cs is pure, so this runs as a console exe (a winexe
// cannot print) built by theme-test.ps1, and deliberately NOT part of build.ps1 - it must not
// race with Program.Main.

using System;
using System.Globalization;

namespace Monitron.App
{
    internal static class ThemeTests
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
            Console.WriteLine("Theme - palettes and the resolution rule");

            Console.WriteLine("the light palette is the one the app has always drawn");
            Check(ThemePalette.Light.WindowBg == 0xFFFFFFFF, "light window is white");
            Check(ThemePalette.Light.Ink == 0xFF1F1F1F, "light ink is #1F1F1F (EXP03)");
            Check(ThemePalette.Light.Muted == 0xFF767676, "light muted is #767676");
            Check(ThemePalette.Light.Warn == 0xFFB03A2B, "light warn is #B03A2B");
            Check(ThemePalette.Light.RowFill == 0xFFF7F7F7, "light row fill is #F7F7F7");
            Check(ThemePalette.Light.RowEdge == 0xFFE4E4E4, "light row edge is #E4E4E4");
            Check(ThemePalette.Light.ControlFill == 0xFFFFFFFF, "light control fill is white");
            Check(ThemePalette.Light.ControlEdge == 0xFFD8D8D8, "light control edge is #D8D8D8");

            Console.WriteLine("dark is a different color for every role");
            uint[] light = Roles(ThemePalette.Light);
            uint[] dark = Roles(ThemePalette.Dark);
            Check(light.Length == dark.Length && light.Length == 10, "ten roles compared");
            int same = 0;
            for (int i = 0; i < light.Length; i++) { if (light[i] == dark[i]) same++; }
            Check(same == 0, "no role keeps its color across the two palettes (same: " + same + ")");
            Check(ThemePalette.For(ThemeMode.Dark).WindowBg == ThemePalette.Dark.WindowBg, "For(dark) is the dark palette");
            Check(ThemePalette.For(ThemeMode.Light).WindowBg == ThemePalette.Light.WindowBg, "For(light) is the light one");

            Console.WriteLine("hex formatting and component extraction");
            Check(ThemePalette.Hex(0xFF1F1F1F) == "#1F1F1F", "0xFF1F1F1F formats as #1F1F1F");
            Check(ThemePalette.Hex(0xFFFFFFFF) == "#FFFFFF", "white formats as #FFFFFF");
            Check(ThemePalette.Hex(0xFF0078D7) == "#0078D7", "the accent formats low component first");
            Check(ThemePalette.A(0x80402010) == 0x80 && ThemePalette.R(0x80402010) == 0x40
                && ThemePalette.G(0x80402010) == 0x20 && ThemePalette.B(0x80402010) == 0x10,
                "A/R/G/B take the right bytes");

            Console.WriteLine("auto follows the OS");
            Check(ThemeChoice.Resolve(true, ThemePreference.Auto) == ThemeMode.Light, "auto + OS light -> light");
            Check(ThemeChoice.Resolve(false, ThemePreference.Auto) == ThemeMode.Dark, "auto + OS dark -> dark");

            Console.WriteLine("a pinned preference beats the OS");
            Check(ThemeChoice.Resolve(false, ThemePreference.Light) == ThemeMode.Light, "light pinned over a dark OS");
            Check(ThemeChoice.Resolve(true, ThemePreference.Dark) == ThemeMode.Dark, "dark pinned over a light OS");
            Check(ThemeChoice.Resolve(false, ThemePreference.Light) == ThemeMode.Light, "and it is stable");
            Check(ThemeChoice.Resolve(false, ThemePreference.Light) == ThemeMode.Light, "repeated: still light");

            Console.WriteLine("preference parsing");
            ThemePreference p;
            Check(ThemeChoice.TryParsePreference("auto", out p) && p == ThemePreference.Auto, "'auto'");
            Check(ThemeChoice.TryParsePreference("LIGHT", out p) && p == ThemePreference.Light, "'LIGHT' (case)");
            Check(ThemeChoice.TryParsePreference(" dark ", out p) && p == ThemePreference.Dark, "' dark ' (trim)");
            Check(!ThemeChoice.TryParsePreference("blue", out p) && p == ThemePreference.Auto, "'blue' refused, auto left");
            Check(!ThemeChoice.TryParsePreference("", out p) && p == ThemePreference.Auto, "empty refused");
            Check(!ThemeChoice.TryParsePreference(null, out p) && p == ThemePreference.Auto, "null refused");

            Console.WriteLine("mode parsing and names");
            ThemeMode m;
            Check(ThemeChoice.TryParseMode("light", out m) && m == ThemeMode.Light, "mode 'light'");
            Check(ThemeChoice.TryParseMode("Dark", out m) && m == ThemeMode.Dark, "mode 'Dark' (case)");
            Check(!ThemeChoice.TryParseMode("auto", out m), "a mode is never 'auto'");
            Check(!ThemeChoice.TryParseMode("grey", out m), "'grey' refused");
            Check(ThemeChoice.Name(ThemeMode.Dark) == "dark" && ThemeChoice.Name(ThemeMode.Light) == "light", "mode names");
            Check(ThemeChoice.Name(ThemePreference.Auto) == "auto" && ThemeChoice.Name(ThemePreference.Dark) == "dark", "preference names");

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} passed, {1} failed", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static uint[] Roles(ThemePalette p)
        {
            return new uint[]
            {
                p.WindowBg, p.Ink, p.Muted, p.Warn, p.RowFill,
                p.RowEdge, p.ControlFill, p.ControlEdge, p.ControlHover, p.Accent,
            };
        }
    }
}
