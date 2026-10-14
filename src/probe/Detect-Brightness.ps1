#requires -Version 7
<#
  EXP02 probe — detect connected monitors and read/write brightness.

  External monitors: DDC/CI via dxva2.dll (VCP 0x10 brightness, 0x12 contrast).
  Internal panels:   WMI root\wmi WmiMonitorBrightness(Methods).

  READ-ONLY BY DEFAULT. Pass -Write to run the controlled write test
  (current -Delta, read back, then restore). VCP 0x10 only; never touches
  input source (0x60), power (0xD6), or the capabilities-string API.

  Throwaway diagnostic code for EXP02. Not product code.
#>
param(
    [switch]$Write,
    [int]$Delta = 20
)

$ErrorActionPreference = 'Stop'

$cs = @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class DdcProbe
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprc, MonitorEnumProc cb, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX mi);
    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint n);
    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint n, IntPtr arr);
    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool DestroyPhysicalMonitors(uint n, IntPtr arr);
    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetMonitorBrightness(IntPtr h, out uint min, out uint cur, out uint max);
    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool SetMonitorBrightness(IntPtr h, uint v);
    [DllImport("dxva2.dll", SetLastError = true)]
    static extern bool GetMonitorContrast(IntPtr h, out uint min, out uint cur, out uint max);

    public static string[] Detect(bool write, int delta)
    {
        var lines = new List<string>();
        var handles = new List<IntPtr>();
        MonitorEnumProc cb = (h, hdc, r, d) => { handles.Add(h); return true; };
        if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero))
        {
            lines.Add("EnumDisplayMonitors failed: " + Marshal.GetLastWin32Error());
            return lines.ToArray();
        }
        lines.Add("Display monitors found: " + handles.Count);

        int structSize = Marshal.SizeOf(typeof(PHYSICAL_MONITOR));
        int idx = 0;
        foreach (var hMon in handles)
        {
            idx++;
            var mi = new MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
            string dev = "?";
            bool primary = false;
            if (GetMonitorInfo(hMon, ref mi)) { dev = mi.szDevice; primary = (mi.dwFlags & 1) != 0; }
            lines.Add("");
            lines.Add("Monitor " + idx + "  device=" + dev + "  primary=" + primary +
                      "  rect=" + mi.rcMonitor.Left + "," + mi.rcMonitor.Top + ".." + mi.rcMonitor.Right + "," + mi.rcMonitor.Bottom);

            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) || count == 0)
            {
                lines.Add("  physical monitors: 0 (err=" + Marshal.GetLastWin32Error() + ") - no DDC/CI handle");
                continue;
            }

            IntPtr buf = Marshal.AllocHGlobal(structSize * (int)count);
            try
            {
                if (!GetPhysicalMonitorsFromHMONITOR(hMon, count, buf))
                {
                    lines.Add("  GetPhysicalMonitorsFromHMONITOR failed: " + Marshal.GetLastWin32Error());
                    continue;
                }

                for (uint p = 0; p < count; p++)
                {
                    var ph = Marshal.PtrToStructure<PHYSICAL_MONITOR>(IntPtr.Add(buf, (int)(p * (uint)structSize)));
                    lines.Add("  physical[" + p + "] desc='" + ph.szPhysicalMonitorDescription +
                              "' handle=0x" + ph.hPhysicalMonitor.ToInt64().ToString("X"));

                    uint bmin, bcur, bmax;
                    bool haveB = GetMonitorBrightness(ph.hPhysicalMonitor, out bmin, out bcur, out bmax);
                    int berr = Marshal.GetLastWin32Error();
                    lines.Add(haveB
                        ? "    brightness: cur=" + bcur + " min=" + bmin + " max=" + bmax + " (raw)"
                        : "    brightness: UNREADABLE err=" + berr);

                    uint cmin, ccur, cmax;
                    bool haveC = GetMonitorContrast(ph.hPhysicalMonitor, out cmin, out ccur, out cmax);
                    lines.Add(haveC
                        ? "    contrast:   cur=" + ccur + " min=" + cmin + " max=" + cmax + " (raw)"
                        : "    contrast:   unreadable err=" + Marshal.GetLastWin32Error());

                    if (write && haveB)
                    {
                        long target = (long)bcur - delta;
                        if (target < bmin) target = bmin;
                        if (target > bmax) target = bmax;
                        lines.Add("    WRITE: " + bcur + " -> " + target);
                        bool ok = SetMonitorBrightness(ph.hPhysicalMonitor, (uint)target);
                        int werr = Marshal.GetLastWin32Error();
                        System.Threading.Thread.Sleep(700);
                        uint rmin, rcur, rmax;
                        bool rok = GetMonitorBrightness(ph.hPhysicalMonitor, out rmin, out rcur, out rmax);
                        lines.Add("    write ok=" + ok + " err=" + werr + "  readback=" + (rok ? rcur.ToString() : "fail"));
                        bool rest = SetMonitorBrightness(ph.hPhysicalMonitor, bcur);
                        System.Threading.Thread.Sleep(700);
                        uint fmin, fcur, fmax;
                        bool fok = GetMonitorBrightness(ph.hPhysicalMonitor, out fmin, out fcur, out fmax);
                        lines.Add("    RESTORE to " + bcur + ": ok=" + rest + "  readback=" + (fok ? fcur.ToString() : "fail"));
                    }
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, buf);
                Marshal.FreeHGlobal(buf);
            }
        }
        return lines.ToArray();
    }
}
'@

Add-Type -TypeDefinition $cs -Language CSharp

"=== DDC/CI probe (dxva2) - write=$($Write.IsPresent) delta=$Delta ==="
[DdcProbe]::Detect($Write.IsPresent, $Delta) | ForEach-Object { $_ }

""
"=== Internal panel (WMI) ==="
try {
    $brightness = Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction Stop
    foreach ($m in $brightness) { "  $($m.InstanceName)  current=$($m.CurrentBrightness)" }

    if ($Write) {
        $methods = Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightnessMethods -ErrorAction Stop
        foreach ($m in $methods) {
            $inst = $m.InstanceName
            $cur = ($brightness | Where-Object InstanceName -eq $inst).CurrentBrightness
            $target = [Math]::Max(0, [int]$cur - $Delta)
            "  WRITE $inst : $cur -> $target"
            $r = Invoke-CimMethod -InputObject $m -MethodName WmiSetBrightness -Arguments @{ Timeout = 0; Brightness = [byte]$target }
            Start-Sleep -Milliseconds 700
            $rb = (Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness | Where-Object InstanceName -eq $inst).CurrentBrightness
            "    result=$($r.ReturnValue) readback=$rb"
            $r2 = Invoke-CimMethod -InputObject $m -MethodName WmiSetBrightness -Arguments @{ Timeout = 0; Brightness = [byte]$cur }
            Start-Sleep -Milliseconds 700
            $rb2 = (Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness | Where-Object InstanceName -eq $inst).CurrentBrightness
            "    RESTORE $cur : result=$($r2.ReturnValue) readback=$rb2"
        }
    }
}
catch { "  WMI error: $($_.Exception.Message)" }
