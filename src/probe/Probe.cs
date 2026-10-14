// EXP02 probe — .NET Framework 4.8, built with the in-box csc.exe (0 MB download).
// Throwaway diagnostic code, not product code.
//
// Reads active display topology (QueryDisplayConfig) and DDC/CI brightness/contrast
// (dxva2.dll, VCP 0x10/0x12). Read-only unless --write is passed, in which case it
// does one reversible write (cur - delta, read back, restore) and only if the
// physical monitor handle is non-null (a trusted read).

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Monitorn.Probe
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            bool write = false;
            bool force = false;
            int delta = 20;
            int set = -1;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--write") write = true;
                else if (args[i] == "--force") force = true;
                else if (args[i] == "--delta" && i + 1 < args.Length) delta = int.Parse(args[++i]);
                else if (args[i] == "--set" && i + 1 < args.Length) set = int.Parse(args[++i]);
            }
            Ddc.Force = force;
            Ddc.Set = set;

            Console.WriteLine("Monitorn EXP02 probe  clr=" + Environment.Version + "  64bit=" + (IntPtr.Size == 8));
            Console.WriteLine();
            Console.WriteLine("=== Active display topology (QueryDisplayConfig) ===");
            Topology.Dump();
            Console.WriteLine();
            Console.WriteLine("=== DDC/CI (dxva2) ===");
            Ddc.Dump(write, delta);
            return 0;
        }
    }

    internal static class Topology
    {
        [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Lo; public int Hi; }
        [StructLayout(LayoutKind.Sequential)] private struct RATIONAL { public uint Num; public uint Den; }
        [StructLayout(LayoutKind.Sequential)] private struct REGION2D { public uint cx; public uint cy; }
        [StructLayout(LayoutKind.Sequential)] private struct SIGNAL { public ulong pixelRate; public RATIONAL h; public RATIONAL v; public REGION2D a; public REGION2D t; public uint vs; public uint slo; }
        [StructLayout(LayoutKind.Sequential)] private struct SRC { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct TGT { public LUID adapterId; public uint id; public uint modeInfoIdx; public uint outputTechnology; public uint rotation; public uint scaling; public RATIONAL refreshRate; public uint scanLineOrdering; public int targetAvailable; public uint statusFlags; }
        [StructLayout(LayoutKind.Sequential)] private struct PATH { public SRC src; public TGT tgt; public uint flags; }

        [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint np, out uint nm);
        [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH[] paths, ref uint nm, IntPtr modes, IntPtr topo);

        public static void Dump()
        {
            uint np, nm;
            int rc = GetDisplayConfigBufferSizes(2, out np, out nm); // QDC_ONLY_ACTIVE_PATHS
            if (rc != 0) { Console.WriteLine("  GetDisplayConfigBufferSizes failed rc=" + rc); return; }
            Console.WriteLine("  active paths=" + np + " modeInfos=" + nm + " pathSize=" + Marshal.SizeOf(typeof(PATH)));

            var paths = new PATH[np];
            IntPtr modes = Marshal.AllocHGlobal((int)((nm < 1 ? 1 : nm) * 64));
            try
            {
                rc = QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero);
                if (rc != 0) { Console.WriteLine("  QueryDisplayConfig failed rc=" + rc); return; }
                for (uint i = 0; i < np; i++)
                {
                    Console.WriteLine("  path[" + i + "] srcId=" + paths[i].src.id
                        + " targetId=" + paths[i].tgt.id
                        + " outputTech=0x" + paths[i].tgt.outputTechnology.ToString("X8")
                        + " available=" + paths[i].tgt.targetAvailable);
                }
            }
            finally { Marshal.FreeHGlobal(modes); }
        }
    }

    internal static class Ddc
    {
        public static bool Force;
        public static int Set = -1;
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
        [DllImport("dxva2.dll", SetLastError = true)] private static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint cur, out uint max);

        public static void Dump(bool write, int delta)
        {
            var handles = new List<IntPtr>();
            MonitorEnumProc cb = delegate(IntPtr h, IntPtr hdc, IntPtr r, IntPtr d) { handles.Add(h); return true; };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero))
            { Console.WriteLine("  EnumDisplayMonitors failed err=" + Marshal.GetLastWin32Error()); return; }
            Console.WriteLine("  display monitors: " + handles.Count);

            int size = Marshal.SizeOf(typeof(PHYSICAL_MONITOR));
            int i = 0;
            foreach (IntPtr hMon in handles)
            {
                i++;
                var mi = new MONITORINFOEX(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                string dev = "?";
                if (GetMonitorInfo(hMon, ref mi)) dev = mi.szDevice;
                Console.WriteLine("  monitor[" + i + "] device=" + dev);

                uint count;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) || count == 0)
                { Console.WriteLine("    physical=0 err=" + Marshal.GetLastWin32Error()); continue; }

                IntPtr buf = Marshal.AllocHGlobal(size * (int)count);
                try
                {
                    if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, buf))
                    { Console.WriteLine("    GetPhysicalMonitors failed err=" + Marshal.GetLastWin32Error()); continue; }

                    for (uint p = 0; p < count; p++)
                    {
                        var ph = (PHYSICAL_MONITOR)Marshal.PtrToStructure(IntPtr.Add(buf, (int)(p * (uint)size)), typeof(PHYSICAL_MONITOR));
                        Console.WriteLine("    physical[" + p + "] desc='" + ph.desc + "' handle=0x" + ph.h.ToInt64().ToString("X"));

                        uint bmin, bcur, bmax;
                        bool ok = GetMonitorBrightness(ph.h, out bmin, out bcur, out bmax);
                        Console.WriteLine("      brightness " + (ok ? ("cur=" + bcur + " min=" + bmin + " max=" + bmax) : ("UNREADABLE err=" + Marshal.GetLastWin32Error())));
                        if (ph.h == IntPtr.Zero) Console.WriteLine("      !! NULL handle: value served without a real monitor handle");

                        uint cmin, ccur, cmax;
                        bool okc = GetMonitorContrast(ph.h, out cmin, out ccur, out cmax);
                        Console.WriteLine("      contrast   " + (okc ? ("cur=" + ccur + " min=" + cmin + " max=" + cmax) : ("unreadable err=" + Marshal.GetLastWin32Error())));

                        if (write)
                        {
                            if (!ok) { Console.WriteLine("      WRITE SKIPPED (no readable brightness)"); continue; }
                            if (ph.h == IntPtr.Zero && !Force) { Console.WriteLine("      WRITE SKIPPED (null handle; pass --force to test anyway)"); continue; }

                            if (Set >= 0)
                            {
                                long sv = Set; if (sv < bmin) sv = bmin; if (sv > bmax) sv = bmax;
                                Console.WriteLine("      SET " + bcur + " -> " + sv + (sv == Set ? "" : " (clamped)"));
                                bool sw = SetMonitorBrightness(ph.h, (uint)sv);
                                System.Threading.Thread.Sleep(700);
                                uint smin, scur, smax; bool sok = GetMonitorBrightness(ph.h, out smin, out scur, out smax);
                                Console.WriteLine("      set ok=" + sw + " readback=" + (sok ? scur.ToString() : "fail"));
                                continue;
                            }

                            long target = (long)bcur - delta; if (target < bmin) target = bmin; if (target > bmax) target = bmax;
                            Console.WriteLine("      WRITE " + bcur + " -> " + target);
                            bool w = SetMonitorBrightness(ph.h, (uint)target); int werr = Marshal.GetLastWin32Error();
                            System.Threading.Thread.Sleep(700);
                            uint rmin, rcur, rmax; bool rok = GetMonitorBrightness(ph.h, out rmin, out rcur, out rmax);
                            Console.WriteLine("      write ok=" + w + " err=" + werr + " readback=" + (rok ? rcur.ToString() : "fail"));
                            bool r = SetMonitorBrightness(ph.h, bcur);
                            System.Threading.Thread.Sleep(700);
                            uint fmin, fcur, fmax; bool fok = GetMonitorBrightness(ph.h, out fmin, out fcur, out fmax);
                            Console.WriteLine("      RESTORE " + bcur + " ok=" + r + " readback=" + (fok ? fcur.ToString() : "fail"));
                        }
                    }
                }
                finally { DestroyPhysicalMonitors(count, buf); Marshal.FreeHGlobal(buf); }
            }
        }
    }
}
