// EXP07 - persist the schedule settings the window edits and the scheduler reads.
// EXP17 - and the seventh: `daylight`, the value the night light returns screens to (an old file
//         without the key keeps the built-in default).
//
// Two layers, on purpose:
//   * SettingsCodec - pure text <-> ScheduleSettings, no I/O at all, so the round trip and every
//     corrupt-file case can be asserted headlessly (SettingsPersistenceTests.cs, run by
//     persistence-test.ps1). It writes *through the model's own transitions*, so an out-of-range
//     `strength=999` is refused in the one place that already knows the rule and the default stays.
//   * SettingsStore - the thin file shell: resolve the per-user path, read tolerantly, write
//     atomically. Nothing here throws at its caller: a settings file that cannot be read or written
//     must never stop the app from starting.
//
// Format is a hand-rolled `key=value` document (no library may be downloaded, and the in-box
// compiler has no serializer). Unknown keys are ignored; a missing, partial or unknown-version file
// loads best-effort. C# 5 only - the in-box csc.exe is what builds this.

using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Monitron.App
{
    /// <summary>The settings document, as text. Pure: no clock, no I/O, no UI.</summary>
    internal static class SettingsCodec
    {
        public const int Version = 1;

        public static string Serialize(ScheduleSettings settings)
        {
            var sb = new StringBuilder();
            sb.Append("; Monitron - night-light schedule settings").Append(Environment.NewLine);
            sb.Append("version=").Append(Version.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
            sb.Append("enabled=").Append(Bool(settings.Enabled)).Append(Environment.NewLine);
            sb.Append("strength=").Append(settings.Strength.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
            sb.Append("daylight=").Append(settings.Daylight.ToString(CultureInfo.InvariantCulture)).Append(Environment.NewLine);
            sb.Append("gradualFade=").Append(Bool(settings.GradualFade)).Append(Environment.NewLine);
            sb.Append("mode=").Append(settings.Mode == ScheduleMode.SunsetToSunrise ? "SunsetToSunrise" : "SetHours").Append(Environment.NewLine);
            sb.Append("turnOn=").Append(Clock(settings.TurnOn)).Append(Environment.NewLine);
            sb.Append("turnOff=").Append(Clock(settings.TurnOff)).Append(Environment.NewLine);
            return sb.ToString();
        }

        /// <summary>
        /// Applies whatever the text says onto the model and returns how many *recognized* keys were
        /// found (0 for something that is not a settings document at all). An unrecognized value for a
        /// recognized key leaves that field alone - a corrupt line cannot wipe the rest of the file.
        /// </summary>
        public static int Parse(string text, ScheduleSettings settings)
        {
            if (text == null) return 0;

            int known = 0;
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == ';' || line[0] == '#') continue;

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "version":
                        known++;   // recorded, not enforced: an unknown version loads best-effort
                        break;

                    case "enabled":
                        known++;
                        bool enabled;
                        if (bool.TryParse(value, out enabled)) settings.SetEnabled(enabled);
                        break;

                    case "strength":
                        known++;
                        settings.TrySetStrength(value);   // out of range: refused, the held value stands
                        break;

                    case "daylight":
                        known++;
                        settings.TrySetDaylight(value);   // same rule as strength (EXP17)
                        break;

                    case "gradualfade":
                        known++;
                        bool fade;
                        if (bool.TryParse(value, out fade) && fade != settings.GradualFade) settings.ToggleGradualFade();
                        break;

                    case "mode":
                        known++;
                        ScheduleMode mode;
                        if (TryMode(value, out mode)) settings.SetMode(mode);
                        break;

                    case "turnon":
                        known++;
                        ApplyClock(value, true, settings);
                        break;

                    case "turnoff":
                        known++;
                        ApplyClock(value, false, settings);
                        break;
                }
            }
            return known;
        }

        /// <summary>24-hour "HH:mm", the same text the verification seams speak.</summary>
        public static string Clock(TimeSpan time)
        {
            return time.Hours.ToString("00", CultureInfo.InvariantCulture) + ":" +
                time.Minutes.ToString("00", CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value) { return value ? "true" : "false"; }

        private static bool TryMode(string value, out ScheduleMode mode)
        {
            mode = ScheduleMode.SetHours;
            if (string.Equals(value, "SetHours", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "SunsetToSunrise", StringComparison.OrdinalIgnoreCase))
            {
                mode = ScheduleMode.SunsetToSunrise;
                return true;
            }
            return false;   // an unrecognized mode is left as it is, not coerced
        }

        private static void ApplyClock(string value, bool isOn, ScheduleSettings settings)
        {
            int hour12, minute; bool pm;
            if (!ScheduleSettings.TryParseClock(value, out hour12, out minute, out pm)) return;
            if (isOn) settings.TrySetTurnOn(hour12, minute, pm);
            else settings.TrySetTurnOff(hour12, minute, pm);
        }
    }

    /// <summary>
    /// Where the settings live and how they get there. The path is per user
    /// (`%APPDATA%\Monitron\settings.ini`) unless a verification run overrides it. A missing file
    /// yields the model's own defaults; a write goes through a temp file and a replace so an
    /// interrupted write cannot leave a half-written settings file behind.
    /// </summary>
    internal sealed class SettingsStore
    {
        public const string FolderName = "Monitron";
        public const string FileName = "settings.ini";

        private readonly string _path;

        /// <summary><paramref name="path"/> null/empty means the real per-user location.</summary>
        public SettingsStore(string path)
        {
            _path = string.IsNullOrEmpty(path) ? DefaultPath() : path;
        }

        public string Path { get { return _path; } }

        /// <summary>The last read/write failure, or null. Never thrown at the caller.</summary>
        public string LastError { get; private set; }

        public static string DefaultPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return System.IO.Path.Combine(System.IO.Path.Combine(appData, FolderName), FileName);
        }

        /// <summary>The stored settings, or the built-in defaults on a missing/unreadable file.</summary>
        public ScheduleSettings Load()
        {
            LastError = null;
            var settings = new ScheduleSettings();
            try
            {
                if (!File.Exists(_path)) return settings;
                SettingsCodec.Parse(File.ReadAllText(_path), settings);
            }
            catch (Exception ex)
            {
                LastError = ex.Message;   // a settings file we cannot read must not stop the app
            }
            return settings;
        }

        /// <summary>Writes the settings atomically. False (with <see cref="LastError"/>) on failure.</summary>
        public bool Save(ScheduleSettings settings)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string text = SettingsCodec.Serialize(settings);
                string temp = _path + ".tmp";
                File.WriteAllText(temp, text);
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);

                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return false;
            }
        }
    }
}
