// EXP08 - the theme as a pure model: which palette, and which mode that is.
//
// Two ideas, kept apart on purpose:
//   * ThemePalette - the colors, as plain 0xAARRGGBB numbers, so a test can assert them with no
//     WPF in the room. Light is *exactly* the palette the app has drawn since EXP03.
//   * ThemeChoice - the rule that turns "what Windows says" plus "what the user asked for" into a
//     mode. Auto follows the OS; Light and Dark pin it.
//
// C# 5 only - the in-box csc.exe (.NET Framework 4.0 compiler) is what builds this. No WPF here.

using System;
using System.Globalization;

namespace Monitron.App
{
    internal enum ThemeMode
    {
        Light,
        Dark,
    }

    internal enum ThemePreference
    {
        Auto,
        Light,
        Dark,
    }

    /// <summary>
    /// The app's colors, as 0xAARRGGBB. Mutable fields on purpose: they are what a test compares,
    /// not something the app hands around (the app draws from ThemeBrushes in Theming.cs).
    /// </summary>
    internal sealed class ThemePalette
    {
        public uint WindowBg;
        public uint Ink;          // primary text
        public uint Muted;        // secondary text
        public uint Warn;         // an error line under a row
        public uint RowFill;      // a monitor row's card
        public uint RowEdge;
        public uint ControlFill;  // a button's face, a text field's face, a check box's box
        public uint ControlEdge;  // a button's or field's border, a slider's unfilled track
        public uint ControlHover;
        public uint Accent;       // the slider's filled track and thumb, a check/radio mark, a focus border

        /// <summary>The palette the app has always drawn: EXP03's surface, verbatim.</summary>
        public static readonly ThemePalette Light = new ThemePalette
        {
            WindowBg = 0xFFFFFFFF,
            Ink = 0xFF1F1F1F,
            Muted = 0xFF767676,
            Warn = 0xFFB03A2B,
            RowFill = 0xFFF7F7F7,
            RowEdge = 0xFFE4E4E4,
            ControlFill = 0xFFFFFFFF,
            ControlEdge = 0xFFD8D8D8,
            ControlHover = 0xFFEDEDED,
            Accent = 0xFF0078D7,
        };

        /// <summary>The dark counterpart: the same roles, a step off black, Windows' dark chrome.</summary>
        public static readonly ThemePalette Dark = new ThemePalette
        {
            WindowBg = 0xFF1E1E1E,
            Ink = 0xFFF0F0F0,
            Muted = 0xFFA6A6A6,
            Warn = 0xFFFF8A7A,
            RowFill = 0xFF282828,
            RowEdge = 0xFF3A3A3A,
            ControlFill = 0xFF313131,
            ControlEdge = 0xFF4A4A4A,
            ControlHover = 0xFF3E3E3E,
            Accent = 0xFF4CC2FF,
        };

        public static ThemePalette For(ThemeMode mode)
        {
            return mode == ThemeMode.Dark ? Dark : Light;
        }

        // --- the components, so the hex the report writes is the same arithmetic the tests assert ---

        public static byte A(uint argb) { return (byte)(argb >> 24); }
        public static byte R(uint argb) { return (byte)(argb >> 16); }
        public static byte G(uint argb) { return (byte)(argb >> 8); }
        public static byte B(uint argb) { return (byte)argb; }

        public static string Hex(uint argb)
        {
            return "#" + Component(R(argb)) + Component(G(argb)) + Component(B(argb));
        }

        private static string Component(byte value)
        {
            return value.ToString("X2", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The decision, with no OS and no window in it: os-light plus a preference, nothing else.</summary>
    internal static class ThemeChoice
    {
        /// <summary>Auto follows the OS; a pinned preference wins over the OS.</summary>
        public static ThemeMode Resolve(bool osLight, ThemePreference preference)
        {
            if (preference == ThemePreference.Light) return ThemeMode.Light;
            if (preference == ThemePreference.Dark) return ThemeMode.Dark;
            return osLight ? ThemeMode.Light : ThemeMode.Dark;
        }

        /// <summary>"auto" | "light" | "dark". Anything else is refused, not coerced.</summary>
        public static bool TryParsePreference(string text, out ThemePreference preference)
        {
            preference = ThemePreference.Auto;
            if (text == null) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "auto": preference = ThemePreference.Auto; return true;
                case "light": preference = ThemePreference.Light; return true;
                case "dark": preference = ThemePreference.Dark; return true;
                default: return false;
            }
        }

        /// <summary>"light" | "dark", the vocabulary the report and the seams speak.</summary>
        public static bool TryParseMode(string text, out ThemeMode mode)
        {
            mode = ThemeMode.Light;
            if (text == null) return false;
            switch (text.Trim().ToLowerInvariant())
            {
                case "light": mode = ThemeMode.Light; return true;
                case "dark": mode = ThemeMode.Dark; return true;
                default: return false;
            }
        }

        public static string Name(ThemeMode mode) { return mode == ThemeMode.Dark ? "dark" : "light"; }

        public static string Name(ThemePreference preference)
        {
            if (preference == ThemePreference.Light) return "light";
            if (preference == ThemePreference.Dark) return "dark";
            return "auto";
        }
    }
}
