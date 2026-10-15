// EXP14 - the message that says the desktop's displays changed, turned into one re-read.
//
// A projection change arrives as a burst: a WM_DISPLAYCHANGE per mode, then settings messages. So the
// hook does not re-read on the first one - it waits for the burst to stop, then re-reads once on the
// dispatcher, the same re-read the tray's Show calls. The debounce is the only part of the display
// path not driven headlessly; the diff it feeds is (DisplayRegistry, display-test.ps1).
//
// The hook is added with EnsureHandle, like SystemTheme.Follow, and NOT on SourceInitialized: the
// surface's handle is already created by the time this is called (Theming.Bind ensures it in the
// SurfaceView constructor), so a SourceInitialized subscription would never fire - which is exactly
// the trap EXP09's own re-place hook fell into.
//
// C# 5 only - the in-box csc.exe is what builds this.

using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Monitron.App
{
    internal static class DisplayWatcher
    {
        private const int WM_DISPLAYCHANGE = 0x007E;

        /// <summary>How long the messages must stop before the displays are read again.</summary>
        internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(700);

        public static void Attach(Window window, Action changed)
        {
            Attach(window, changed, Settle);
        }

        public static void Attach(Window window, Action changed, TimeSpan settle)
        {
            DispatcherTimer debounce = null;
            WindowHook.Add(window, delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                // WM_DISPLAYCHANGE alone. A monitor attached or removed moves the desktop geometry,
                // so this same message arrives; WM_DEVICECHANGE would need a registered monitor
                // device interface to carry those events and otherwise only reports other devices.
                if (message != WM_DISPLAYCHANGE) return IntPtr.Zero;

                if (debounce != null) debounce.Stop();
                debounce = new DispatcherTimer { Interval = settle };
                debounce.Tick += delegate
                {
                    debounce.Stop();
                    changed();
                };
                debounce.Start();
                return IntPtr.Zero;
            });
        }
    }

    /// <summary>
    /// A window's message hook, added on the spot rather than on SourceInitialized - a window whose
    /// handle already exists (one Theming.Bind has touched) has already raised that event, so a
    /// subscription added afterwards would silently never run. EnsureHandle is idempotent.
    /// </summary>
    internal static class WindowHook
    {
        public delegate IntPtr Proc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled);

        public static void Add(Window window, Proc proc)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                HwndSource source = HwndSource.FromHwnd(hwnd);
                if (source == null) return;
                source.AddHook(new HwndSourceHook(proc));
            }
            catch { /* a hook that cannot be added is not a reason to fail the app */ }
        }
    }
}
