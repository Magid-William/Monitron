// EXP05 - the hardware half of the actuator seam: DDC/CI over dxva2 for external displays,
// the WMI brightness API for internal panels.
//
// Detection follows EXP02's findings:
//   * the active rendering targets come from QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS);
//   * identity comes from the target's name/device path, never the physical-monitor handle
//     (it is NULL on this Intel setup yet dxva2 still works - NULL is not failure);
//   * an active path whose outputTechnology has the internal high bit set is a panel, and is
//     driven over WMI; everything else is external and is driven over DDC/CI (VCP 0x10).
// Writes stay off VCP 0x60/0xD6 and never call the capabilities-string API.
//
// C# 5 only. References System.Management (WMI) from the in-box framework directory.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace Monitron.App
{
    internal sealed class BrightnessController : IBrightnessController, IDisposable
    {
        // --- identity kept for writes ---
        private readonly Dictionary<string, IntPtr> _ddcHandles = new Dictionary<string, IntPtr>();   // id -> physical monitor handle
        private readonly Dictionary<string, string> _wmiInstances = new Dictionary<string, string>(); // id -> WmiMonitorBrightnessMethods InstanceName
        private readonly List<KeyValuePair<uint, IntPtr>> _allocations = new List<KeyValuePair<uint, IntPtr>>(); // (count, buffer) to destroy

        // ---------------------------------------------------------------- detect

        public IList<MonitorInfo> Detect()
        {
            var monitors = new List<MonitorInfo>();
            var ddc = DdcHandles();              // gdi device name -> (handle, allocation)
            IList<KeyValuePair<string, int>> panels = WmiPanels();   // InstanceName -> current

            uint np, nm;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0 || np == 0)
                return monitors;

            var paths = new DISPLAYCONFIG_PATH_INFO[np];
            IntPtr modes = Marshal.AllocHGlobal((int)(nm < 1 ? 64 : nm * 64));
            try
            {
                uint gotPaths = np, gotModes = nm;
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref gotPaths, paths, ref gotModes, modes, IntPtr.Zero) != 0)
                    return monitors;

                int index = 0;
                for (uint i = 0; i < gotPaths; i++)
                {
                    index++;
                    DISPLAYCONFIG_PATH_INFO path = paths[i];
                    string gdi = SourceName(path.source.adapterId, path.source.id);
                    string friendly, devicePath;
                    TargetName(path.target.adapterId, path.target.id, out friendly, out devicePath);

                    bool internalPanel = path.target.outputTechnology >= 0x80000000;
                    if (internalPanel)
                    {
                        // Match the active panel to its WMI instance (one panel: use it as-is).
                        if (panels.Count == 0) continue;
                        KeyValuePair<string, int> panel = MatchPanel(panels, devicePath, friendly);
                        string id = "WMI:" + panel.Key;
                        _wmiInstances[id] = panel.Key;
                        monitors.Add(new MonitorInfo(id, friendly, MonitorKind.Internal, 0, panel.Value, 100, index));
                    }
                    else
                    {
                        MonitorHandle h;
                        if (!ddc.TryGetValue(gdi, out h)) continue;
                        uint min, cur, max;
                        if (!GetMonitorBrightness(h.Handle, out min, out cur, out max)) continue;  // gate: only readable monitors become rows
                        string id = "DDC:" + DeviceId(devicePath, gdi);
                        _ddcHandles[id] = h.Handle;
                        monitors.Add(new MonitorInfo(id, friendly, MonitorKind.External, (int)min, (int)cur, (int)max, index));
                    }
                }
            }
            finally { Marshal.FreeHGlobal(modes); }

            return monitors;
        }

        // ---------------------------------------------------------------- read / write

        public int? Read(string id)
        {
            if (_wmiInstances.ContainsKey(id)) return WmiRead(_wmiInstances[id]);
            IntPtr handle;
            if (_ddcHandles.TryGetValue(id, out handle))
            {
                uint min, cur, max;
                if (GetMonitorBrightness(handle, out min, out cur, out max)) return (int)cur;
            }
            return null;
        }

        public bool Write(string id, int value)
        {
            if (_wmiInstances.ContainsKey(id)) return WmiWrite(_wmiInstances[id], value);
            IntPtr handle;
            if (_ddcHandles.TryGetValue(id, out handle)) return SetMonitorBrightness(handle, (uint)value);
            return false;
        }

        public void Dispose()
        {
            foreach (KeyValuePair<uint, IntPtr> a in _allocations)
            {
                try { DestroyPhysicalMonitors(a.Key, a.Value); } catch { }
                try { Marshal.FreeHGlobal(a.Value); } catch { }
            }
            _allocations.Clear();
            _ddcHandles.Clear();
        }

        // ---------------------------------------------------------------- display config

        private const uint QDC_ONLY_ACTIVE_PATHS = 2;
        private const uint GET_SOURCE_NAME = 1;
        private const uint GET_TARGET_NAME = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID { public uint Low; public int High; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RATIONAL { public uint Numerator; public uint Denominator; }
        [StructLayout(LayoutKind.Sequential)]
        private struct SOURCE_INFO { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)]
        private struct TARGET_INFO
        {
            public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology;
            public uint rotation; public uint scaling; public RATIONAL refreshRate; public uint scanLineOrdering;
            public int targetAvailable; public uint statusFlags;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO { public SOURCE_INFO source; public TARGET_INFO target; public uint flags; }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_INFO_HEADER { public uint type; public uint size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SOURCE_DEVICE_NAME
        {
            public DEVICE_INFO_HEADER header;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TARGET_DEVICE_NAME
        {
            public DEVICE_INFO_HEADER header;
            public uint flags;
            public uint outputTechnology;
            public ushort edidManufactureId;
            public ushort edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
        }

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint numPaths,
            [Out] DISPLAYCONFIG_PATH_INFO[] paths, ref uint numModes, IntPtr modes, IntPtr topology);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref SOURCE_DEVICE_NAME request);
        [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME request);

        private static string SourceName(LUID adapter, uint id)
        {
            var request = new SOURCE_DEVICE_NAME();
            request.header.type = GET_SOURCE_NAME;
            request.header.size = (uint)Marshal.SizeOf(typeof(SOURCE_DEVICE_NAME));
            request.header.adapterId = adapter;
            request.header.id = id;
            if (DisplayConfigGetDeviceInfo(ref request) == 0) return request.viewGdiDeviceName;
            return null;
        }

        private static void TargetName(LUID adapter, uint id, out string friendly, out string devicePath)
        {
            var request = new TARGET_DEVICE_NAME();
            request.header.type = GET_TARGET_NAME;
            request.header.size = (uint)Marshal.SizeOf(typeof(TARGET_DEVICE_NAME));
            request.header.adapterId = adapter;
            request.header.id = id;
            if (DisplayConfigGetDeviceInfo(ref request) == 0)
            {
                friendly = request.monitorFriendlyDeviceName;
                devicePath = request.monitorDevicePath;
                return;
            }
            friendly = null;
            devicePath = null;
        }

        private static string DeviceId(string devicePath, string gdi)
        {
            if (!string.IsNullOrEmpty(devicePath)) return devicePath;
            if (!string.IsNullOrEmpty(gdi)) return gdi;
            return "unknown";
        }

        // ---------------------------------------------------------------- dxva2 (external)

        private struct MonitorHandle { public IntPtr Handle; }

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MONITORINFOEX
        {
            public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PHYSICAL_MONITOR
        {
            public IntPtr h;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string desc;
        }
        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data);

        [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprc, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint n);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint n, IntPtr arr);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool DestroyPhysicalMonitors(uint n, IntPtr arr);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool SetMonitorBrightness(IntPtr h, uint v);

        /// <summary>Every active display monitor's GDI device name mapped to its first physical handle.</summary>
        private Dictionary<string, MonitorHandle> DdcHandles()
        {
            var map = new Dictionary<string, MonitorHandle>();
            var hmons = new List<IntPtr>();
            MonitorEnumProc cb = delegate(IntPtr h, IntPtr hdc, IntPtr r, IntPtr d) { hmons.Add(h); return true; };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero)) return map;

            int size = Marshal.SizeOf(typeof(PHYSICAL_MONITOR));
            foreach (IntPtr hmon in hmons)
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (!GetMonitorInfo(hmon, ref mi)) continue;

                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hmon, out count) || count == 0) continue;

                IntPtr buf = Marshal.AllocHGlobal(size * (int)count);
                if (!GetPhysicalMonitorsFromHMONITOR(hmon, count, buf))
                {
                    Marshal.FreeHGlobal(buf);
                    continue;
                }
                _allocations.Add(new KeyValuePair<uint, IntPtr>(count, buf));

                var ph = (PHYSICAL_MONITOR)Marshal.PtrToStructure(buf, typeof(PHYSICAL_MONITOR));
                var handle = new MonitorHandle();
                handle.Handle = ph.h;          // may be NULL on Intel - still usable (EXP02)
                map[mi.szDevice] = handle;
            }
            return map;
        }

        // ---------------------------------------------------------------- WMI (internal)

        private static IList<KeyValuePair<string, int>> WmiPanels()
        {
            var list = new List<KeyValuePair<string, int>>();
            try
            {
                var searcher = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM WmiMonitorBrightness");
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    var name = item["InstanceName"] as string;
                    object cur = item["CurrentBrightness"];
                    if (name == null || cur == null) continue;
                    list.Add(new KeyValuePair<string, int>(name, Convert.ToInt32(cur, CultureInfo.InvariantCulture)));
                }
            }
            catch { }
            return list;
        }

        private static KeyValuePair<string, int> MatchPanel(IList<KeyValuePair<string, int>> panels, string devicePath, string friendly)
        {
            // The WMI InstanceName embeds the same PnP id the target device path carries (e.g.
            // "DISPLAY\BOE0764\..."). Match on the EDID token, else take the only panel.
            if (panels.Count == 1) return panels[0];
            foreach (KeyValuePair<string, int> p in panels)
            {
                if (!string.IsNullOrEmpty(devicePath))
                {
                    string token = EdidToken(devicePath);
                    if (token != null && p.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) return p;
                }
            }
            return panels[0];
        }

        private static string EdidToken(string devicePath)
        {
            // "\\?\DISPLAY#BOE0764#5&..." -> "BOE0764"
            int a = devicePath.IndexOf('#');
            if (a < 0) return null;
            int b = devicePath.IndexOf('#', a + 1);
            if (b < 0) return null;
            return devicePath.Substring(a + 1, b - a - 1);
        }

        private static int? WmiRead(string instance)
        {
            try
            {
                var searcher = new ManagementObjectSearcher("root\\wmi",
                    "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE InstanceName='" + instance + "'");
                foreach (ManagementBaseObject item in searcher.Get())
                    return Convert.ToInt32(item["CurrentBrightness"], CultureInfo.InvariantCulture);
            }
            catch { }
            return null;
        }

        private static bool WmiWrite(string instance, int value)
        {
            try
            {
                var mo = new ManagementObject("root\\wmi",
                    "WmiMonitorBrightnessMethods.InstanceName='" + instance + "'", null);
                object[] args = new object[] { (uint)0, (byte)value };
                mo.InvokeMethod("WmiSetBrightness", args);
                return true;
            }
            catch { return false; }
        }
    }
}
