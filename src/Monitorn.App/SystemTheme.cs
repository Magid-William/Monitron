// EXP08 - what Windows says about its own theme, and the chrome Windows (not WPF) draws.
//
//   * ReadAppThemeLight - the OS "app mode" setting, under a key this experiment only ever reads.
//   * ApplyTitleBar / IsTitleBarDark - the non-client title bar, which DWM paints; WPF cannot
//     reach it, so it is asked for through DWMWA_USE_IMMERSIVE_DARK_MODE (20, 19 before 20H1) and
//     read back to prove the call landed.
//   * Follow - the live path: WM_SETTINGCHANGE carrying "ImmersiveColorSet", the message Windows
//     broadcasts when the user flips the theme.
//   * ThemeState - the one object that resolves all of it, and the single hook a window needs.
//
// C# 5 only - the in-box csc.exe is what builds this.

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace Monitorn.App
{
    internal static class SystemTheme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string AppsUseLightValue = "AppsUseLightTheme";

        private const int WmSettingChange = 0x001A;
        private const int DwmwaUseImmersiveDarkMode = 20;          // Windows 10 20H1 and later
        private const int DwmwaUseImmersiveDarkModeBefore = 19;    // Windows 10 18985..19041

        /// <summary>True when Windows is set to light app mode, false for dark, null when unknown.</summary>
        public static bool? ReadAppThemeLight()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    if (key == null) return null;
                    object value = key.GetValue(AppsUseLightValue);
                    if (value == null) return null;
                    return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) != 0;
                }
            }
            catch
            {
                return null;   // an unreadable key must not stop the app from starting
            }
        }

        // --- the non-client title bar ---

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        /// <summary>Asks DWM for a dark title bar. False when this OS will not do it.</summary>
        public static bool ApplyTitleBar(Window window, bool dark)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                int value = dark ? 1 : 0;
                int result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
                if (result != 0)
                    result = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore, ref value, sizeof(int));
                return result == 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>What DWM reports, or null when the attribute cannot be read on this build.</summary>
        public static bool? IsTitleBarDark(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                int value;
                if (DwmGetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, out value, sizeof(int)) != 0)
                    return null;
                return value != 0;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Runs <paramref name="changed"/> when Windows broadcasts a color-set change.</summary>
        public static void Follow(Window window, Action changed)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                HwndSource source = HwndSource.FromHwnd(hwnd);
                if (source == null) return;

                // The hook runs on the UI thread, so the re-resolve needs no marshalling.
                source.AddHook(delegate(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
                {
                    if (msg == WmSettingChange && l != IntPtr.Zero)
                    {
                        string what = Marshal.PtrToStringUni(l);
                        if (string.Equals(what, "ImmersiveColorSet", StringComparison.OrdinalIgnoreCase))
                            changed();
                    }
                    return IntPtr.Zero;
                });
            }
            catch
            {
                // no hook, no live follow: the theme still applies at launch
            }
        }
    }

    /// <summary>
    /// The one place the palette is decided: what Windows says, with what the command line asked
    /// for on top. Every window draws from <see cref="Brushes"/>, so a switch is "recolour the
    /// shared brushes and re-apply the chrome" - never a rebuild.
    /// </summary>
    internal sealed class ThemeState
    {
        private readonly Func<bool> _osLight;
        private readonly ThemePreference _preference;

        public ThemeState(ThemePreference preference, Func<bool> osLight, bool templates)
        {
            _preference = preference;
            _osLight = osLight;
            Templates = templates;
            Brushes = new ThemeBrushes(ThemePalette.Light);
            Mode = ThemeChoice.Resolve(osLight(), preference);
            Brushes.Equip(ThemePalette.For(Mode));
        }

        public ThemePreference Preference { get { return _preference; } }

        /// <summary>Whether the OS-styled controls get templates of ours (--theme-no-templates off).</summary>
        public bool Templates { get; private set; }

        public ThemeMode Mode { get; private set; }

        public ThemeBrushes Brushes { get; private set; }

        /// <summary>The OS reading this state was built with, as the app last saw it.</summary>
        public bool OsLight { get { return _osLight(); } }

        /// <summary>Raised only when the resolved mode changed - the one hook a window needs.</summary>
        public event Action Changed;

        /// <summary>Re-reads the OS and re-resolves. False (and silent) when nothing changed.</summary>
        public bool Refresh()
        {
            ThemeMode mode = ThemeChoice.Resolve(_osLight(), _preference);
            if (mode == Mode) return false;

            Mode = mode;
            Brushes.Equip(ThemePalette.For(mode));
            if (Changed != null) Changed();
            return true;
        }
    }
}
