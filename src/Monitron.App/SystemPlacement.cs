// EXP09 - the OS half of the placement: what Windows says about the monitor the window is on.
//
// Deliberately WPF-free so the reading can be reasoned about (and tested) without a window, and so
// PlacementApp.cs is the only file that needs WPF. Everything here is a read: no monitor is touched,
// no taskbar is moved.

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Monitron.App
{
    /// <summary>One reading of the desktop, in pixels, as the OS reports it.</summary>
    public sealed class PlacementReading
    {
        public string MonitorDevice = "";
        public PlacementBox Monitor;    // the full monitor rectangle
        public PlacementBox WorkArea;   // the monitor minus its taskbar
        public PlacementBox Taskbar;    // the taskbar's own rectangle, empty when there is none
        public double Dpi = Dips.DefaultDpi;
        public TaskbarEdge? ForcedEdge;  // the verification seam: as if the taskbar were docked elsewhere

        public bool HasTaskbar { get { return !Taskbar.IsEmpty; } }

        public PlacementReading Copy()
        {
            return new PlacementReading
            {
                MonitorDevice = MonitorDevice, Monitor = Monitor, WorkArea = WorkArea, Taskbar = Taskbar,
                Dpi = Dpi, ForcedEdge = ForcedEdge,
            };
        }

        /// <summary>The reading as the report writes it, one key=value per line.</summary>
        public string Describe()
        {
            var lines = new StringBuilder();
            lines.AppendLine("monitor=" + MonitorDevice);
            lines.AppendLine("monitorRect=" + Monitor.Describe());
            lines.AppendLine("workArea=" + WorkArea.Describe());
            lines.AppendLine("taskbar=" + (HasTaskbar ? Taskbar.Describe() : "none"));
            lines.AppendLine("dpi=" + Dpi.ToString("0.##", CultureInfo.InvariantCulture));
            lines.AppendLine("edge=" + PlacementGeometry.Name(ForcedEdge ?? PlacementGeometry.EdgeOf(Monitor, WorkArea, Taskbar)));
            lines.AppendLine("forced=" + (ForcedEdge.HasValue ? PlacementGeometry.Name(ForcedEdge.Value) : "no"));
            return lines.ToString();
        }
    }

    /// <summary>Stands in for part of the OS reading, the way --os-theme stands in for the theme and --now for the clock.</summary>
    public sealed class PlacementScript
    {
        public TaskbarEdge? Edge;
        public double? Dpi;
        public PlacementBox? WorkArea;   // pixels

        public bool Any { get { return Edge.HasValue || Dpi.HasValue || WorkArea.HasValue; } }

        public PlacementReading Apply(PlacementReading real)
        {
            PlacementReading clone = real.Copy();
            if (Dpi.HasValue) clone.Dpi = Dpi.Value;
            if (WorkArea.HasValue) clone.WorkArea = WorkArea.Value;
            clone.ForcedEdge = Edge;
            return clone;
        }
    }

    /// <summary>Reads the desktop for a window. Never writes anything.</summary>
    public static class SystemPlacement
    {
        public static PlacementReading Read(IntPtr window)
        {
            var reading = new PlacementReading();

            // The window's own monitor - the one it was launched on, as today - not the primary and not
            // the taskbar's, so the flyout appears where the window already was.
            IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                reading.MonitorDevice = info.szDevice ?? "";
                reading.Monitor = new PlacementBox(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right, info.rcMonitor.Bottom);
                reading.WorkArea = new PlacementBox(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right, info.rcWork.Bottom);
            }

            reading.Dpi = ReadDpi(monitor, window);
            reading.Taskbar = ReadTaskbar(monitor);
            return reading;
        }

        private static double ReadDpi(IntPtr monitor, IntPtr window)
        {
            uint x = 0, y = 0;
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, MdtEffectiveDpi, out x, out y) == 0 && x > 0) return x;
            if (window != IntPtr.Zero)
            {
                uint dpi = GetDpiForWindow(window);
                if (dpi > 0) return dpi;
            }
            return Dips.DefaultDpi;
        }

        /// <summary>
        /// The taskbar that belongs to this monitor: Shell_TrayWnd when it is the primary's, otherwise
        /// the Shell_SecondaryTrayWnd on that monitor (Windows 11 puts one on every display).
        /// </summary>
        public static PlacementBox ReadTaskbar(IntPtr monitor)
        {
            IntPtr bar = TaskbarHandleFor(monitor);
            if (bar == IntPtr.Zero) return PlacementBox.Empty;
            RECT rect;
            if (!GetWindowRect(bar, out rect)) return PlacementBox.Empty;
            var box = new PlacementBox(rect.Left, rect.Top, rect.Right, rect.Bottom);
            return box.IsEmpty ? PlacementBox.Empty : box;
        }

        private static IntPtr TaskbarHandleFor(IntPtr monitor)
        {
            IntPtr primary = FindWindow("Shell_TrayWnd", null);
            if (primary != IntPtr.Zero && (monitor == IntPtr.Zero || MonitorFromWindow(primary, MonitorDefaultToNearest) == monitor))
                return primary;

            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (found != IntPtr.Zero) return false;
                var name = new StringBuilder(64);
                if (GetClassName(h, name, name.Capacity) == 0) return true;
                if (name.ToString() != "Shell_SecondaryTrayWnd") return true;
                if (monitor != IntPtr.Zero && MonitorFromWindow(h, MonitorDefaultToNearest) != monitor) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        // --- Win32 ---

        private const uint MonitorDefaultToNearest = 2;
        private const int MdtEffectiveDpi = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        private delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out uint x, out uint y);
    }
}
