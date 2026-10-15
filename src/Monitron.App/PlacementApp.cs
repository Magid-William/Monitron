// EXP09 - the window half of the placement: put a window in the flyout corner, and keep it there.
//
// Two things make this more than an assignment. SizeToContent means the window's height is unknown
// until it has been laid out, so placing it once at construction leaves it growing down over the
// taskbar; and the work area can change under a running window (taskbar moved or resized, auto-hide,
// a display change, a DPI change), so the position has to be re-derived, not remembered.

using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Monitron.App
{
    /// <summary>Places a window at the notification area's corner of the monitor it is on.</summary>
    public static class Place
    {
        public const double DefaultMargin = 8;

        private const int WM_SETTINGCHANGE = 0x001A;
        private const int WM_DISPLAYCHANGE = 0x007E;
        private const int WM_DPICHANGED = 0x02E0;

        /// <summary>
        /// Attach the flyout placement to a window. <paramref name="read"/> is the OS reading, injectable
        /// so a verifier can drive a different taskbar edge, DPI or work area without moving the user's
        /// taskbar. <paramref name="reportDir"/>, when set, gets the reading and the computed rectangle.
        /// Returns the placement itself, so a scripted change can re-run exactly what a message does.
        /// </summary>
        public static Action Attach(Window window, Func<IntPtr, PlacementReading> read, double margin, string reportDir)
        {
            // Manual is what makes Left/Top ours: the WPF default would centre or cascade over them.
            window.WindowStartupLocation = WindowStartupLocation.Manual;

            // Counted so a verifier can prove a message really re-placed it (--place-report's
            // placements=), the way --display-report's detections= proves a WM_DISPLAYCHANGE was seen.
            int[] placements = new int[1];
            Action apply = delegate { Apply(window, read, margin, reportDir, placements); };
            window.SourceInitialized += delegate { apply(); };
            window.Loaded += delegate { apply(); };
            window.SizeChanged += delegate { apply(); };   // SizeToContent: the height arrives late, and a resize must not walk off the corner
            Follow(window, apply);
            return apply;
        }

        /// <summary>Keep a window inside its monitor's work area without resizing it (the settings window).</summary>
        public static void Clamp(Window window, Func<IntPtr, PlacementReading> read)
        {
            Action clamp = delegate
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return;
                double width = window.ActualWidth, height = window.ActualHeight;
                if (width <= 0 || height <= 0) return;

                PlacementReading reading = Reading(read, hwnd);
                if (reading == null) return;
                PlacementTarget target = PlacementTarget.From(reading.Monitor, reading.WorkArea, reading.Taskbar, reading.Dpi, null);

                var box = new PlacementBox(window.Left, window.Top, window.Left + width, window.Top + height);
                if (target.WorkArea.Contains(box, 1.0)) return;   // where it already is is fine
                Move(window, PlacementGeometry.Clamp(box, target.WorkArea));
            };

            window.Loaded += delegate { clamp(); };
            window.SizeChanged += delegate { clamp(); };
        }

        private static void Apply(Window window, Func<IntPtr, PlacementReading> read, double margin, string reportDir,
            int[] placements)
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            PlacementReading reading = Reading(read, hwnd);
            if (reading == null) return;

            double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            double height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0) return;   // not laid out yet; Loaded will come back

            PlacementTarget target = PlacementTarget.From(reading.Monitor, reading.WorkArea, reading.Taskbar,
                reading.Dpi, reading.ForcedEdge);
            PlacementBox box = PlacementGeometry.For(target, width, height, margin);
            Move(window, box);

            if (placements != null) placements[0]++;
            if (reportDir != null)
                PlacementReport.Write(reportDir, "surface", reading, target, box, window, margin, placements != null ? placements[0] : 0);
        }

        /// <summary>
        /// Re-place on the messages that mean the desktop moved under us. The hook is added on the spot,
        /// through WindowHook - NOT on SourceInitialized: the surface's handle already exists
        /// (Theming.Bind calls EnsureHandle in its constructor), so a SourceInitialized subscription
        /// added here would silently never run. That is the trap EXP09's own re-place hook fell into;
        /// EXP14 found it and this experiment puts the hook where it actually fires.
        /// </summary>
        private static void Follow(Window window, Action apply)
        {
            WindowHook.Add(window, delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == WM_SETTINGCHANGE || message == WM_DISPLAYCHANGE || message == WM_DPICHANGED)
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, apply);   // let WPF finish its own response first
                return IntPtr.Zero;
            });
        }

        private static PlacementReading Reading(Func<IntPtr, PlacementReading> read, IntPtr hwnd)
        {
            // A failed read must not take the app down: no placement is better than no window.
            try { return read(hwnd); }
            catch { return null; }
        }

        private static void Move(Window window, PlacementBox box)
        {
            // A half-pixel tolerance: WPF round-trips Left/Top through the window's own coordinates.
            if (double.IsNaN(window.Left) || Math.Abs(window.Left - box.Left) > 0.5) window.Left = box.Left;
            if (double.IsNaN(window.Top) || Math.Abs(window.Top - box.Top) > 0.5) window.Top = box.Top;
        }
    }

    /// <summary>Writes what the placement decided, so a verifier can compare it with the window itself.</summary>
    public static class PlacementReport
    {
        public static void Write(string directory, string name, PlacementReading reading, PlacementTarget target,
            PlacementBox computed, Window window, double margin, int placements)
        {
            try
            {
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                var text = new System.Text.StringBuilder();
                text.Append(reading.Describe());
                text.AppendLine("placements=" + placements.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("margin=" + margin.ToString("0.##", CultureInfo.InvariantCulture));
                text.AppendLine("windowDip=" + window.ActualWidth.ToString("0.##", CultureInfo.InvariantCulture) + "x"
                    + window.ActualHeight.ToString("0.##", CultureInfo.InvariantCulture));
                text.AppendLine("computed=" + computed.Describe());
                text.AppendLine("workAreaDip=" + target.WorkArea.Describe());
                text.AppendLine("corner=" + PlacementGeometry.Name(target.Edge));
                text.AppendLine("window=" + new PlacementBox(window.Left, window.Top,
                    window.Left + window.ActualWidth, window.Top + window.ActualHeight).Describe());

                string path = Path.Combine(directory, name + ".txt");
                string temp = path + ".tmp";
                File.WriteAllText(temp, text.ToString());
                File.Copy(temp, path, true);   // atomic replace: a reader never sees half a report
                File.Delete(temp);
            }
            catch { /* a report is evidence, never a reason to fail */ }
        }
    }
}
