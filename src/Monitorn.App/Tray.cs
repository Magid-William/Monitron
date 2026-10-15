// EXP11 - the tray's own state, pure: is the surface showing?
//
// The menu has exactly three lines - Show/Hide, "Run on Windows startup", Quit - and the first is the app's
// state, not the OS's: whether the surface window is visible. This holds that, and nothing else;
// the Run-entry half lives in AutoStart.cs, and the shell half in TrayIcon.cs. No WPF, no P/Invoke,
// so the headless test (TrayTests.cs, run by tray-test.ps1) can drive it. C# 5 only.

namespace Monitorn.App
{
    internal sealed class TrayModel
    {
        public TrayModel(bool visible) { Visible = visible; }

        public bool Visible { get; private set; }

        public void Show() { Visible = true; }
        public void Hide() { Visible = false; }

        /// <summary>The menu line in the user's words: what pressing it will do next.</summary>
        public string VisibilityLabel { get { return Visible ? "Hide" : "Show"; } }

        /// <summary>What pressing it means: the state to move to.</summary>
        public bool NextVisible { get { return !Visible; } }
    }
}
