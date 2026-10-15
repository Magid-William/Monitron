// EXP11 - drives the tray's decisions and asserts the state they expose.
//
// No registry and no shell: TrayModel is advanced through its own transitions, and AutoStart is run
// against an in-memory IStartupStore. That is the AGENTS rule for machine logic. Built as a console
// exe by tray-test.ps1 (a winexe cannot print), and deliberately NOT part of build.ps1.

using System;
using System.Collections.Generic;

namespace Monitron.App
{
    internal static class TrayTests
    {
        private static int _passed;
        private static int _failed;

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; Console.WriteLine("  ok   " + what); }
            else { _failed++; Console.WriteLine("  FAIL " + what); }
        }

        /// <summary>A Run key in memory, so the app's decisions can be asserted with no registry.</summary>
        private sealed class FakeStartup : IStartupStore
        {
            private readonly Dictionary<string, string> _values = new Dictionary<string, string>();
            public bool FailWrites;

            public string Read(string name)
            {
                string value;
                return _values.TryGetValue(name, out value) ? value : null;
            }

            public bool Write(string name, string value)
            {
                if (FailWrites) return false;
                _values[name] = value;
                return true;
            }

            public bool Delete(string name)
            {
                if (FailWrites) return false;
                _values.Remove(name);
                return true;
            }
        }

        private static int Main()
        {
            Console.WriteLine("Tray - Show/Hide driven, the Run entry decided, state asserted");

            Console.WriteLine("the Show/Hide line");
            var shown = new TrayModel(true);
            Check(shown.Visible && shown.VisibilityLabel == "Hide", "visible: the line offers Hide");
            Check(!shown.NextVisible, "visible: pressing it moves to hidden");
            shown.Hide();
            Check(!shown.Visible && shown.VisibilityLabel == "Show", "hidden: the line offers Show");
            Check(shown.NextVisible, "hidden: pressing it moves to visible");
            shown.Show();
            Check(shown.Visible, "and back again");

            var hidden = new TrayModel(false);
            Check(hidden.VisibilityLabel == "Show", "a tray-only start offers Show");

            Console.WriteLine("the Run command");
            string exe = @"C:\Program Files\Monitron\Monitron.App.exe";
            Check(StartupEntry.Command(exe) == "\"" + exe + "\" --hidden",
                "the command quotes the exe and starts it tray-only");
            Check(StartupEntry.Command(@"C:\Monitron.exe") == "\"C:\\Monitron.exe\" --hidden",
                "the same shape for a path without spaces");

            Console.WriteLine("reading a command line back");
            Check(StartupEntry.ProgramOf("\"" + exe + "\" --hidden") == exe, "a quoted program is unquoted");
            Check(StartupEntry.ProgramOf(@"C:\Monitron.exe --hidden") == @"C:\Monitron.exe",
                "an unquoted program ends at the first space");
            Check(StartupEntry.ProgramOf("") == "", "an empty command has no program");
            Check(StartupEntry.ProgramOf(null) == "", "a missing command has no program");

            Console.WriteLine("is an existing value ours?");
            Check(StartupEntry.IsOurs("\"" + exe + "\" --hidden", exe), "our command is ours");
            Check(StartupEntry.IsOurs("\"" + exe + "\" --some other flag", exe),
                "edited arguments are still us - the program is what matters");
            Check(StartupEntry.IsOurs("\"" + exe.ToUpperInvariant() + "\" --hidden", exe),
                "the comparison ignores case, as Windows paths do");
            Check(!StartupEntry.IsOurs("\"C:\\Other\\thing.exe\"", exe), "another program is not ours");
            Check(!StartupEntry.IsOurs(null, exe), "no value is not ours");
            Check(!StartupEntry.IsOurs("", @"D:\x.exe"), "an empty value is not ours");
            Check(!StartupEntry.IsOurs(exe, exe), "an unquoted bare path is compared as one token");
            Check(!StartupEntry.IsOurs("\"" + exe + "\"", ""), "an empty exe path is never ours");

            Console.WriteLine("enabling and disabling over a store");
            var store = new FakeStartup();
            string name = StartupEntry.DefaultName;
            Check(AutoStart.IsEnabled(store, name, exe) == false, "absent: it starts off");
            Check(AutoStart.Enable(store, name, exe), "enabling succeeds");
            Check(store.Read(name) == "\"" + exe + "\" --hidden", "the store holds exactly the command");
            Check(AutoStart.IsEnabled(store, name, exe), "and it now reads as on");
            Check(AutoStart.Disable(store, name), "disabling succeeds");
            Check(store.Read(name) == null, "the value is gone");
            Check(AutoStart.IsEnabled(store, name, exe) == false, "and it reads as off");

            store.Write(name, "\"C:\\SomeoneElse\\app.exe\" --hidden");
            Check(AutoStart.IsEnabled(store, name, exe) == false, "a foreign entry does not read as ours");
            AutoStart.Enable(store, name, exe);
            Check(AutoStart.IsEnabled(store, name, exe), "enabling overwrites it with ours");

            Console.WriteLine("a store that refuses");
            var broken = new FakeStartup { FailWrites = true };
            Check(AutoStart.Enable(broken, name, exe) == false, "a refused write reports failure");
            Check(AutoStart.IsEnabled(broken, name, exe) == false, "and nothing turned on");
            Check(AutoStart.Disable(broken, name) == false, "a refused delete reports failure");

            Console.WriteLine("a custom name is honoured (the verifier's throwaway)");
            var named = new FakeStartup();
            Check(AutoStart.Enable(named, "MonitronExp11", exe), "enabling under the custom name");
            Check(named.Read("MonitronExp11") != null, "which is the value it wrote");
            Check(named.Read(StartupEntry.DefaultName) == null, "and not the default one");

            Console.WriteLine();
            Console.WriteLine("  " + _passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }
    }
}
