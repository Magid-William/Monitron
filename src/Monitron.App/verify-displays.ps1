# EXP14 verification helper - prove the surface follows a display change, from outside.
#
# Builds nothing. It runs the already-built Monitron.App.exe and measures it with witnesses that do not
# go through the app: UI Automation reads the row set (each row's slider carries its display's id) and
# the app's own --display-report must agree with it. The read-only EXP02 probe witnesses that no
# monitor value changed.
#
# A display change is injected, never made: --monitors scripts the display set and --display-change-after
# switches it, so the user's real displays are untouched. A real WM_DISPLAYCHANGE is also posted to the
# window to prove the hook re-reads the actual desktop.
#
#   .\verify-displays.ps1                 # the whole scenario into %TEMP%\monitron-exp14
#   .\verify-displays.ps1 -ShotDir .\out  # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [string]$ShotDir = (Join-Path $env:TEMP 'monitron-exp14'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitron-exp14\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }

$ReportRoot = Join-Path $ShotDir 'reports'
if (Test-Path $ReportRoot) { Remove-Item $ReportRoot -Recurse -Force }

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
// Named per-experiment on purpose: a PowerShell session keeps types once added, so two verifier
// scripts sharing a class name collide in the second one's Add-Type.
public static class WinExp14 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct MONITORINFOEX {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string szDevice;
    }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    public delegate bool MonitorProc(IntPtr m, IntPtr hdc, ref RECT r, IntPtr d);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorProc cb, IntPtr data);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT p, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFOEX mi);
    [DllImport("user32.dll")] public static extern bool GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr m, int kind, out uint x, out uint y);
}
"@

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

$script:passed = 0
$script:failed = 0

function Check($condition, [string]$what) {
    if ($condition) { $script:passed++; Write-Host "  ok   $what" }
    else { $script:failed++; Write-Host "  FAIL $what" }
}
function Note([string]$what) { Write-Host "  .... $what" }

function Box([double]$l, [double]$t, [double]$r, [double]$b) {
    return [pscustomobject]@{ L = $l; T = $t; R = $r; B = $b; W = ($r - $l); H = ($b - $t) }
}
function Str($box) { if ($null -eq $box) { return '(none)' } return ("({0},{1})-({2},{3})" -f [int]$box.L, [int]$box.T, [int]$box.R, [int]$box.B) }
function ParseBox([string]$text) {
    if (-not $text) { return $null }
    $n = @($text.Split(',') | ForEach-Object { [double]$_ })
    if ($n.Count -ne 4) { return $null }
    return Box $n[0] $n[1] $n[2] $n[3]
}
function SameBox($a, $b, [double]$tolerance) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    return ([Math]::Abs($a.L - $b.L) -le $tolerance) -and ([Math]::Abs($a.T - $b.T) -le $tolerance) `
       -and ([Math]::Abs($a.R - $b.R) -le $tolerance) -and ([Math]::Abs($a.B - $b.B) -le $tolerance)
}
function Inside($inner, $outer, [double]$tolerance) {
    if ($null -eq $inner -or $null -eq $outer) { return $false }
    return ($inner.L -ge $outer.L - $tolerance) -and ($inner.T -ge $outer.T - $tolerance) `
       -and ($inner.R -le $outer.R + $tolerance) -and ($inner.B -le $outer.B + $tolerance)
}
function SamePoints($a, $b) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    $x = @($a | Sort-Object); $y = @($b | Sort-Object)
    if ($x.Count -ne $y.Count) { return $false }
    for ($i = 0; $i -lt $x.Count; $i++) { if ($x[$i] -ne $y[$i]) { return $false } }
    return $true
}

function W32Rect([IntPtr]$h) {
    $r = New-Object WinExp14+RECT
    if (-not [WinExp14]::GetWindowRect($h, [ref]$r)) { return $null }
    return Box $r.Left $r.Top $r.Right $r.Bottom
}
function MonitorRead([IntPtr]$h) {
    $mi = New-Object WinExp14+MONITORINFOEX
    $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($mi)
    $m = [WinExp14]::MonitorFromWindow($h, 2)
    if ($m -eq [IntPtr]::Zero -or -not [WinExp14]::GetMonitorInfo($m, [ref]$mi)) { throw "no monitor for the window" }
    $x = [uint32]0; $y = [uint32]0
    [void][WinExp14]::GetDpiForMonitor($m, 0, [ref]$x, [ref]$y)
    return [pscustomobject]@{
        Handle = $m; Device = $mi.szDevice
        Full = Box $mi.rcMonitor.Left $mi.rcMonitor.Top $mi.rcMonitor.Right $mi.rcMonitor.Bottom
        Work = Box $mi.rcWork.Left $mi.rcWork.Top $mi.rcWork.Right $mi.rcWork.Bottom
        Dpi = [double]$x
    }
}
function Monitors() {
    $script:monitorList = New-Object System.Collections.ArrayList
    $cb = [WinExp14+MonitorProc] {
        param($m, $hdc, $r, $d)
        $mi = New-Object WinExp14+MONITORINFOEX
        $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($mi)
        if ([WinExp14]::GetMonitorInfo($m, [ref]$mi)) {
            $x = [uint32]0; $y = [uint32]0
            [void][WinExp14]::GetDpiForMonitor($m, 0, [ref]$x, [ref]$y)
            [void]$script:monitorList.Add([pscustomobject]@{
                Handle = $m; Device = $mi.szDevice
                Full = Box $mi.rcMonitor.Left $mi.rcMonitor.Top $mi.rcMonitor.Right $mi.rcMonitor.Bottom
                Work = Box $mi.rcWork.Left $mi.rcWork.Top $mi.rcWork.Right $mi.rcWork.Bottom
                Dpi = [double]$x })
        }
        return $true
    }
    [void][WinExp14]::EnumDisplayMonitors([IntPtr]::Zero, [IntPtr]::Zero, $cb, [IntPtr]::Zero)
    return $script:monitorList
}
# A point inside a monitor's work area, safely away from its edges.
function APoint($mon) {
    $p = New-Object WinExp14+POINT
    $p.X = [int](($mon.Work.L + $mon.Work.R) / 2)
    $p.Y = [int](($mon.Work.T + $mon.Work.B) / 2)
    return $p
}

# --- UI Automation reads ---
function Element([IntPtr]$h) { return $AE::FromHandle($h) }
function UiaRect($el) {
    if ($null -eq $el) { return $null }
    try { $r = $el.Current.BoundingRectangle } catch { return $null }
    return Box $r.Left $r.Top $r.Right $r.Bottom
}
function FindType($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    return @($root.FindAll($TS::Descendants, $cond))
}
# Every row's slider carries its display's id as its automation id - the row set, as UI Automation sees it.
function SliderIds([IntPtr]$h) {
    return @(FindType (Element $h) $CT::Slider | ForEach-Object { $_.Current.AutomationId })
}

function Launch([string]$label, [string]$extra) {
    $argline = "--no-tray --no-schedule --settings-file `"$SettingsFile`" $extra"
    Write-Host "`n== $label =="
    Note "app: Monitron.App.exe $argline"
    $p = Start-Process -FilePath $Exe -ArgumentList $argline -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        if ($p.HasExited) { throw "the app exited early with code $($p.ExitCode)" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp14]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
    }
    if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }
    [void][WinExp14]::BringWindowToTop($h)
    [void][WinExp14]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 1600
    return [pscustomobject]@{ Proc = $p; Hwnd = $h }
}
function StopApp($app) {
    [void][WinExp14]::PostMessage($app.Hwnd, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
    if (-not $app.Proc.WaitForExit(5000)) { $app.Proc.Kill(); Write-Host "  !! did not exit (killed)"; $script:failed++ }
}
function ReadReport([string]$dir, [string]$name) {
    $path = Join-Path $dir $name
    if (-not (Test-Path $path)) { return $null }
    $map = @{}
    foreach ($line in Get-Content $path) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { $map[$line.Substring(0, $i)] = $line.Substring($i + 1) }
    }
    return $map
}
function ShotWindow([string]$path, [IntPtr]$h) {
    $box = W32Rect $h
    if ($null -eq $box) { return }
    $w = [int]$box.W; $ht = [int]$box.H
    if ($w -le 0 -or $ht -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$box.L, [int]$box.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function ProbeRead {
    if (-not (Test-Path $Probe)) { return $null }
    try {
        $out = & $Probe 2>&1 | Out-String
        $m = [regex]::Match($out, 'brightness cur=(\d+)')
        if ($m.Success) { return [int]$m.Groups[1].Value }
    } catch { }
    return $null
}

# ============================================================================

$realSettings = Join-Path $env:APPDATA 'Monitron\settings.ini'
$realBefore = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
$probeBefore = ProbeRead

$all = Monitors
Note "this machine has $($all.Count) display(s): $((@($all | ForEach-Object { $_.Device }) -join ', '))"
$all | ForEach-Object { Note "  $($_.Device)  full $(Str $_.Full)  work $(Str $_.Work)  dpi $($_.Dpi)" }

# ---------------------------------------------------------------------------
Write-Host "`n== a display that goes away loses its row (--monitors, --display-change-after) =="

$two = "DDC:A|Alpha|30;DDC:B|Beta|40"
$d = Launch "two displays, then one is removed" "--monitors `"$two`" --display-change-after 4 `"DDC:B|Beta|40`" --display-report `"$ReportRoot\shrink`""
$before = SliderIds $d.Hwnd
Note "rows at first: $($before -join ', ')"
Check ((SamePoints $before @('DDC:A', 'DDC:B'))) "both rows are on screen at the start"
Start-Sleep -Milliseconds 4200
$afterIds = SliderIds $d.Hwnd
Note "rows after the change: $($afterIds -join ', ')"
Check ((SamePoints $afterIds @('DDC:B'))) "the display that went away lost its row"
$reportD = ReadReport (Join-Path $ReportRoot 'shrink') 'display.txt'
Check ($null -ne $reportD) "the app wrote its display report"
if ($reportD) {
    Note "report: detections=$($reportD['detections']) detected=$($reportD['detected']) rows=$($reportD['rows'])"
    Check ([int]$reportD['detections'] -ge 2) "the displays were read again"
    Check ($reportD['rows'] -eq 'DDC:B') "and the report's rows agree with UI Automation"
}
[void](ShotWindow (Join-Path $ShotDir '03-shrink.png') $d.Hwnd)
StopApp $d

# ---------------------------------------------------------------------------
Write-Host "`n== a display that appears gains a row =="

$e = Launch "one display, then another appears" "--monitors `"DDC:B|Beta|40`" --display-change-after 4 `"DDC:B|Beta|40;DDC:C|Gamma|55`" --display-report `"$ReportRoot\grow`""
Check ((SamePoints (SliderIds $e.Hwnd) @('DDC:B'))) "one row at the start"
Start-Sleep -Milliseconds 4200
$grew = SliderIds $e.Hwnd
Note "rows after the change: $($grew -join ', ')"
Check ((SamePoints $grew @('DDC:B', 'DDC:C'))) "the arriving display gained a row"
$reportE = ReadReport (Join-Path $ReportRoot 'grow') 'display.txt'
if ($reportE) { Check ($reportE['rows'] -eq 'DDC:B;DDC:C') "and the report agrees" }
StopApp $e

# ---------------------------------------------------------------------------
Write-Host "`n== a real WM_DISPLAYCHANGE re-reads the real desktop (the hook) =="

$f = Launch "the real displays, then a posted WM_DISPLAYCHANGE" "--display-report `"$ReportRoot\realDisplays`""
$realBeforeRows = SliderIds $f.Hwnd
Note "real rows: $($realBeforeRows -join ', ')"
Check ($realBeforeRows.Count -ge 1) "the real detection put at least one row on screen"
[void][WinExp14]::PostMessage($f.Hwnd, 0x007E, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_DISPLAYCHANGE
Start-Sleep -Milliseconds 2200
$realAfterRows = SliderIds $f.Hwnd
$realReport = ReadReport (Join-Path $ReportRoot 'realDisplays') 'display.txt'
Check (-not $f.Proc.HasExited) "the app survived the message"
Check ((SamePoints $realBeforeRows $realAfterRows)) "the rows are the same: this desktop did not change, and it was read again"
if ($realReport) {
    Note "report: detections=$($realReport['detections']) detected=$($realReport['detected'])"
    Check ([int]$realReport['detections'] -ge 2) "the displays were read a second time"
}
StopApp $f

# ---------------------------------------------------------------------------
Write-Host "`n== nothing else moved =="

$probeAfter = ProbeRead
if ($null -ne $probeBefore -and $null -ne $probeAfter) {
    Check ($probeAfter -eq $probeBefore) "no monitor value changed across every run ($probeBefore -> $probeAfter)"
} else {
    Note "no read-only probe available, so the monitor value was not checked"
}
$realAfter = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
Check ($realAfter -eq $realBefore) "the user's real %APPDATA%\Monitron\settings.ini is untouched"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("reports: $ReportRoot")
Write-Host ("shots:   $ShotDir")
if ($script:failed -gt 0) { exit 1 }
