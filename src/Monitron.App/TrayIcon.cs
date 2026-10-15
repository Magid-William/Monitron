// EXP11 - the notification-area icon and its menu, as the shell sees it.
//
// Shell_NotifyIcon is fed a NOTIFYICONDATA that names a window to post the tray's mouse messages to.
// That window is the surface's own HWND: it exists even while the surface is hidden, so the icon
// survives a Hide, and it is gone exactly when the app really exits - which is when the icon is
// removed. The icon is the app's own asset since EXP12 (IconAssets.cs: the generated Monitron.ico,
// embedded in the exe) at the size the shell draws it, and the same asset is the window's icon.
//
// Left click toggles the surface; right click opens a WPF ContextMenu at the cursor. A WPF menu is
// used rather than a Win32 one so it is painted from the app's own palette (Theming.StyleMenu)
// instead of the OS light default, and so its checkable line is a real MenuItem whose state UI
// Automation can read and drive.
//
// The report (--tray-report) is the app's own reading: whether the surface is up, what the Run entry
// says, and - through Shell_NotifyIconGetRect - where the shell actually put the icon. C# 5 only.

using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Monitron.App
{
    internal sealed class TrayIcon
    {
        private const int NIM_ADD = 0x00000000;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int WM_APP = 0x8000;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private const uint IconId = 1;
        private const int TipLength = 128;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = TipLength)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NOTIFYICONIDENTIFIER
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT iconLocation);

        // The callback message the shell posts the icon's mouse events with. In the same WM_APP range
        // the rest of the app uses for private messages.
        private static readonly int TrayMessage = WM_APP + 1;

        private readonly Window _anchor;
        private readonly ThemeState _theme;
        private readonly TrayModel _model;
        private readonly IStartupStore _startup;
        private readonly string _startupName;
        private readonly string _exePath;
        private readonly Action _onToggleVisibility;
        private readonly Action _onToggleStartup;
        private readonly Action _quit;
        private readonly string _reportDir;

        private readonly ContextMenu _menu;
        private readonly MenuItem _visibility;
        private readonly MenuItem _startupLine;
        private readonly MenuItem _quitLine;

        private HwndSource _source;
        private System.Drawing.Icon _trayIcon;
        private IntPtr _hwnd;
        private bool _syncing;
        private bool _added;

        /// <summary>Whether the shell accepted the icon. False means the tray is not usable.</summary>
        public bool Added { get { return _added; } }

        private TrayIcon(Window anchor, ThemeState theme, TrayModel model, IStartupStore startup,
            string startupName, string exePath, Action onToggleVisibility, Action onToggleStartup,
            Action quit, string reportDir)
        {
            _anchor = anchor;
            _theme = theme;
            _model = model;
            _startup = startup;
            _startupName = startupName;
            _exePath = exePath;
            _onToggleVisibility = onToggleVisibility;
            _onToggleStartup = onToggleStartup;
            _quit = quit;
            _reportDir = reportDir;

            _visibility = new MenuItem();
            _visibility.Click += delegate { if (_onToggleVisibility != null) _onToggleVisibility(); };
            // A stable id, unlike the header, which is "Hide" or "Show" as the state flips.
            AutomationProperties.SetAutomationId(_visibility, "TrayShowHide");

            _startupLine = new MenuItem { Header = "Run on Windows startup", IsCheckable = true };
            // A real click and UI Automation's TogglePattern both flip IsChecked, and both raise
            // Checked/Unchecked - but TogglePattern does NOT raise Click, so the handler must hang off
            // the state change, not the click. _syncing guards the assignment Refresh makes.
            _startupLine.Checked += delegate { if (!_syncing) StartupChanged(); };
            _startupLine.Unchecked += delegate { if (!_syncing) StartupChanged(); };
            AutomationProperties.SetAutomationId(_startupLine, "TrayStartup");

            _quitLine = new MenuItem { Header = "Quit" };
            _quitLine.Click += delegate { if (_quit != null) _quit(); };
            AutomationProperties.SetAutomationId(_quitLine, "TrayQuit");

            _menu = new ContextMenu();
            _menu.Items.Add(_visibility);
            _menu.Items.Add(_startupLine);
            _menu.Items.Add(new Separator());
            _menu.Items.Add(_quitLine);

            _trayIcon = IconAssets.Tray();
        }

        /// <summary>
        /// Puts the icon in the notification area and wires its menu to the app's actions. The anchor
        /// window's handle is created if it does not exist yet, so the icon works before a first Show.
        /// </summary>
        public static TrayIcon Attach(Window anchor, ThemeState theme, TrayModel model,
            IStartupStore startup, string startupName, string exePath, Action onToggleVisibility,
            Action onToggleStartup, Action quit, string reportDir)
        {
            var tray = new TrayIcon(anchor, theme, model, startup, startupName, exePath,
                onToggleVisibility, onToggleStartup, quit, reportDir);
            tray.Start();
            return tray;
        }

        private void Start()
        {
            _hwnd = new WindowInteropHelper(_anchor).EnsureHandle();
            _source = HwndSource.FromHwnd(_hwnd);
            if (_source != null) _source.AddHook(WndProc);

            var data = new NOTIFYICONDATA();
            data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = _hwnd;
            data.uID = IconId;
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = (uint)TrayMessage;
            data.hIcon = _trayIcon.Handle;
            data.szTip = "Monitron";

            if (Shell_NotifyIcon(NIM_ADD, ref data))
            {
                _added = true;
            }
            WriteReport();
        }

        /// <summary>Opens the menu at the cursor, as a right click would. Also the --tray-menu seam.</summary>
        public void ShowMenu()
        {
            Theming.StyleMenu(_menu, _theme);
            Refresh();
            _menu.Placement = PlacementMode.MousePoint;
            _menu.IsOpen = true;
            WriteReport();
        }

        /// <summary>Removes the icon and gives its GDI handles back. Safe to call more than once.</summary>
        public void Remove()
        {
            if (_hwnd != IntPtr.Zero)
            {
                var data = new NOTIFYICONDATA();
                data.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA));
                data.hWnd = _hwnd;
                data.uID = IconId;
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _hwnd = IntPtr.Zero;
            }
            if (_source != null) { _source.RemoveHook(WndProc); _source = null; }
            if (_trayIcon != null) { _trayIcon.Dispose(); _trayIcon = null; }
        }

        /// <summary>The menu's own reading of the world: what the label says and what is checked.</summary>
        private void Refresh()
        {
            _visibility.Header = _model.VisibilityLabel;
            _syncing = true;
            try { _startupLine.IsChecked = AutoStart.IsEnabled(_startup, _startupName, _exePath); }
            finally { _syncing = false; }
        }

        /// <summary>The checkable line changed - by a click or by TogglePattern - so act on it.</summary>
        private void StartupChanged()
        {
            if (_onToggleStartup != null) _onToggleStartup();
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != TrayMessage) return IntPtr.Zero;

            int mouse = lParam.ToInt32();
            if (mouse == WM_LBUTTONUP || mouse == WM_LBUTTONDBLCLK)
            {
                if (_onToggleVisibility != null) _onToggleVisibility();
                handled = true;
            }
            else if (mouse == WM_RBUTTONUP || mouse == WM_CONTEXTMENU)
            {
                ShowMenu();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // --- the icon itself lives in IconAssets.cs: the generated Monitron.ico, embedded ---

        // --- the app's own reading (--tray-report) ---

        private void WriteReport()
        {
            if (string.IsNullOrEmpty(_reportDir)) return;

            int left, top, right, bottom;
            bool placed = IconRect(out left, out top, out right, out bottom);
            string value = _startup.Read(_startupName);

            StringBuilder text = new StringBuilder();
            text.Append("tray.report=1").Append('\n');
            text.Append("visible=").Append(_model.Visible ? "true" : "false").Append('\n');
            text.Append("startupName=").Append(_startupName).Append('\n');
            text.Append("startupEnabled=").Append(AutoStart.IsEnabled(_startup, _startupName, _exePath) ? "true" : "false").Append('\n');
            text.Append("startupValue=").Append(value == null ? "" : value).Append('\n');
            text.Append("exe=").Append(_exePath).Append('\n');
            text.Append("iconRect=").Append(placed
                ? string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3}", left, top, right, bottom)
                : "none").Append('\n');

            try
            {
                Directory.CreateDirectory(_reportDir);
                string path = Path.Combine(_reportDir, "tray.txt");
                string temp = path + ".tmp";
                File.WriteAllText(temp, text.ToString());
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch { /* a report is evidence, never a reason to fail */ }
        }

        private bool IconRect(out int left, out int top, out int right, out int bottom)
        {
            left = top = right = bottom = 0;
            if (_hwnd == IntPtr.Zero) return false;

            var identifier = new NOTIFYICONIDENTIFIER();
            identifier.cbSize = Marshal.SizeOf(typeof(NOTIFYICONIDENTIFIER));
            identifier.hWnd = _hwnd;
            identifier.uID = IconId;

            RECT rect;
            try
            {
                if (Shell_NotifyIconGetRect(ref identifier, out rect) != 0) return false;
            }
            catch { return false; }

            left = rect.Left; top = rect.Top; right = rect.Right; bottom = rect.Bottom;
            return true;
        }
    }
}
