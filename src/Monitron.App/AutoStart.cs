// EXP11 - "Run on Windows startup", as a decision and a thin registry shell.
//
// Two layers, like SettingsPersistence.cs:
//   * StartupEntry - pure: the Run value's name and its command string, and the rule that decides
//     whether an existing value is ours. No registry, so it can be asserted headlessly.
//   * IStartupStore / RegistryStartup - the thin HKCU Run shell. Nothing here throws at its caller:
//     a registry that cannot be read or written means "off", it must never stop the app starting.
//
// The registry entry is the single source of truth - there is deliberately no settings.ini key, so
// a removal made outside the app (Task Manager, Windows Settings) is reported honestly the next
// time the menu opens. C# 5 only - the in-box csc.exe is what builds this.

using System;
using Microsoft.Win32;

namespace Monitron.App
{
    /// <summary>The Run entry's naming and command, with no registry in it.</summary>
    internal static class StartupEntry
    {
        public const string DefaultName = "Monitron";

        /// <summary>The command a Run value runs: this exe, quoted, started tray-only.</summary>
        public static string Command(string exePath)
        {
            return "\"" + exePath + "\" --hidden";
        }

        /// <summary>
        /// Whether a Run value is ours: its command's program is this exe. The arguments are not
        /// compared - the user may have edited them, and it is still this app that starts.
        /// </summary>
        public static bool IsOurs(string command, string exePath)
        {
            if (string.IsNullOrEmpty(command) || string.IsNullOrEmpty(exePath)) return false;
            return string.Equals(ProgramOf(command), exePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The executable part of a command line: the first token, with quotes removed.</summary>
        public static string ProgramOf(string command)
        {
            if (command == null) return "";
            string text = command.Trim();
            if (text.Length == 0) return "";
            if (text[0] == '"')
            {
                int end = text.IndexOf('"', 1);
                return end < 0 ? text.Substring(1) : text.Substring(1, end - 1);
            }
            int space = text.IndexOf(' ');
            return space < 0 ? text : text.Substring(0, space);
        }
    }

    /// <summary>The Run key, as the app needs it. A fake is what the headless test drives.</summary>
    internal interface IStartupStore
    {
        /// <summary>The value, or null when there is none or it cannot be read.</summary>
        string Read(string name);

        /// <summary>True on success. Never throws.</summary>
        bool Write(string name, string value);

        /// <summary>True on success (or when there was nothing to remove). Never throws.</summary>
        bool Delete(string name);
    }

    /// <summary>The real thing: HKCU\Software\Microsoft\Windows\CurrentVersion\Run.</summary>
    internal sealed class RegistryStartup : IStartupStore
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>The last read/write failure, or null. Never thrown at the caller.</summary>
        public string LastError { get; private set; }

        public string Read(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    return key == null ? null : key.GetValue(name) as string;
                }
            }
            catch (Exception ex) { LastError = ex.Message; return null; }
        }

        public bool Write(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (key == null) { LastError = "the Run key could not be opened"; return false; }
                    key.SetValue(name, value, RegistryValueKind.String);
                }
                LastError = null;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        public bool Delete(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return true;
                    if (key.GetValue(name) != null) key.DeleteValue(name, false);
                }
                LastError = null;
                return true;
            }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }
    }

    /// <summary>The pure decision over a store: is it on, turn it on, turn it off.</summary>
    internal static class AutoStart
    {
        public static bool IsEnabled(IStartupStore store, string name, string exePath)
        {
            return StartupEntry.IsOurs(store.Read(name), exePath);
        }

        public static bool Enable(IStartupStore store, string name, string exePath)
        {
            return store.Write(name, StartupEntry.Command(exePath));
        }

        public static bool Disable(IStartupStore store, string name)
        {
            return store.Delete(name);
        }
    }
}
