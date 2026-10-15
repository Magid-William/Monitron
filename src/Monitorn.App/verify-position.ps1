# EXP09 verification helper - prove where the surface lands, from outside the app.
#
# Builds nothing. It runs the already-built Monitorn.App.exe and then measures the window with two
# witnesses that do not go through the app at all: UI Automation's BoundingRectangle for the window
# and for the taskbar's clock, and Win32 (GetWindowRect / GetMonitorInfo) for the work area and the
# taskbar. The app's own --place-report is the third: it must agree with what the witnesses see.
#
# Nothing here writes a monitor value and nothing moves the user's taskbar: every run is
# --no-schedule on a temp settings file, and the alternate edges / DPIs / work areas are injected
# through the app's seams rather than by changing the desktop.
#
#   .\verify-position.ps1                  # full scenario, reports+shots into %TEMP%\monitorn-exp09
#   .\verify-position.ps1 -ShotDir .\out   # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitorn.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [string]$ShotDir = (Join-Path $env:TEMP 'monitorn-exp09'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitorn-exp09\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }

# The reports of the previous run are not this run's evidence.
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
public static class WinExp09 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    public struct MONITORINFOEX {
        public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string szDevice;
    }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string win);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFOEX mi);
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

# --- rectangles as plain objects, so the assertions read like arithmetic ---

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

function Overlaps($a, $b, [double]$tolerance) {
    if ($null -eq $a -or $null -eq $b) { return $false }
    $apart = ($b.L -ge $a.R - $tolerance) -or ($b.R -le $a.L + $tolerance) `
          -or ($b.T -ge $a.B - $tolerance) -or ($b.B -le $a.T + $tolerance)
    return -not $apart
}

function Scaled($box, [double]$dpi) {   # DIPs -> the monitor's physical pixels
    if ($null -eq $box) { return $null }
    $s = $dpi / 96.0
    return Box ($box.L * $s) ($box.T * $s) ($box.R * $s) ($box.B * $s)
}

# The model's clamp, mirrored here so "the app clamped it" can be asserted as a position.
function ClampBox($box, $area) {
    $l = [Math]::Max($area.L, [Math]::Min($box.L, [Math]::Max($area.L, $area.R - $box.W)))
    $t = [Math]::Max($area.T, [Math]::Min($box.T, [Math]::Max($area.T, $area.B - $box.H)))
    return Box $l $t ($l + $box.W) ($t + $box.H)
}

# --- Win32 reads ---

function W32Rect([IntPtr]$h) {
    $r = New-Object WinExp09+RECT
    if (-not [WinExp09]::GetWindowRect($h, [ref]$r)) { return $null }
    return Box $r.Left $r.Top $r.Right $r.Bottom
}

function MonitorRead([IntPtr]$h) {
    $mi = New-Object WinExp09+MONITORINFOEX
    $mi.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($mi)
    $m = [WinExp09]::MonitorFromWindow($h, 2)
    if ($m -eq [IntPtr]::Zero -or -not [WinExp09]::GetMonitorInfo($m, [ref]$mi)) { throw "no monitor for the window" }
    $x = [uint32]0; $y = [uint32]0
    [void][WinExp09]::GetDpiForMonitor($m, 0, [ref]$x, [ref]$y)
    return [pscustomobject]@{
        Handle = $m
        Device = $mi.szDevice
        Full   = Box $mi.rcMonitor.Left $mi.rcMonitor.Top $mi.rcMonitor.Right $mi.rcMonitor.Bottom
        Work   = Box $mi.rcWork.Left $mi.rcWork.Top $mi.rcWork.Right $mi.rcWork.Bottom
        Dpi    = [double]$x
    }
}

function Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][WinExp09]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function ClassName([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 128
    [void][WinExp09]::GetClassName($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

# The taskbar that belongs to a monitor: Shell_TrayWnd when it is the primary's, otherwise the
# Shell_SecondaryTrayWnd on that monitor (Windows 11 puts one on every display).
function TaskbarFor([IntPtr]$monitor) {
    $primary = [WinExp09]::FindWindow('Shell_TrayWnd', $null)
    if ($primary -ne [IntPtr]::Zero -and [WinExp09]::MonitorFromWindow($primary, 2) -eq $monitor) { return $primary }
    $found = [IntPtr]::Zero
    $cb = [WinExp09+EnumProc] {
        param($h, $l)
        if (ClassName $h -eq 'Shell_SecondaryTrayWnd' -and [WinExp09]::MonitorFromWindow($h, 2) -eq $monitor) {
            $script:foundBar = $h
            return $false
        }
        return $true
    }
    $script:foundBar = [IntPtr]::Zero
    [void][WinExp09]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:foundBar
}

function HwndByTitle([int]$targetPid, [string]$title) {
    $hits = New-Object System.Collections.ArrayList
    $cb = [WinExp09+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp09]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp09]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp09]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

function CloseAll([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [WinExp09+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp09]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp09]::IsWindowVisible($h)) { [void]$found.Add($h) }
        return $true
    }
    [void][WinExp09]::EnumWindows($cb, [IntPtr]::Zero)
    foreach ($w in $found) { [void][WinExp09]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    return $found.Count
}

# --- UI Automation reads ---

function Element([IntPtr]$h) { return $AE::FromHandle($h) }

function UiaRect($el) {
    if ($null -eq $el) { return $null }
    try { $r = $el.Current.BoundingRectangle } catch { return $null }
    return Box $r.Left $r.Top $r.Right $r.Bottom
}

function FindClass($root, [string]$className) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, $className)
    return $root.FindFirst($TS::Descendants, $cond)
}

function FindType($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)
    return @($root.FindAll($TS::Descendants, $cond))
}

# The clock and the notification area, read from the taskbar's own UI Automation tree. On this build
# (Windows 11 25H2) the clock is not a TrayClockWClass child window, and it is not inside
# TrayNotifyWnd either (that pane is a childless leaf): it is a taskbar button with ClassName
# SystemTray.OmniButtonLeft whose Name is localized.
function TrayParts([IntPtr]$barHandle) {
    $bar = Element $barHandle
    if ($null -eq $bar) { return $null }
    $notify = FindClass $bar 'TrayNotifyWnd'
    $clock = FindClass $bar 'SystemTray.OmniButtonLeft'
    if ($null -eq $clock) {
        # Fallback that depends on no name and no single class spelling: the leftmost tray button at
        # or right of the notification area's left edge is the clock column on Windows 11.
        $right = if ($null -ne $notify) { $notify.Current.BoundingRectangle.Left } else { 0 }
        $buttons = @(FindType $bar $CT::Button
            | Where-Object { $_.Current.ClassName -like 'SystemTray.*' -and $_.Current.BoundingRectangle.Left -ge $right }
            | Sort-Object { $_.Current.BoundingRectangle.Left })
        if ($buttons.Count -gt 0) { $clock = $buttons[0] }
    }
    return [pscustomobject]@{ Bar = (UiaRect $bar); Notify = (UiaRect $notify); Clock = (UiaRect $clock) }
}

# --- launching and closing ---

function Launch([string]$label, [string]$extra) {
    $argline = "--no-schedule --settings-file `"$SettingsFile`" $extra"
    Write-Host "`n== $label =="
    Note "app: Monitorn.App.exe $argline"
    $p = Start-Process -FilePath $Exe -ArgumentList $argline -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        if ($p.HasExited) { throw "the app exited early with code $($p.ExitCode)" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp09]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
    }
    if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }
    [void][WinExp09]::BringWindowToTop($h)
    [void][WinExp09]::SetForegroundWindow($h)
    Start-Sleep -Milliseconds 1600   # let it paint and settle: SizeToContent sizes it late and the placement follows
    return [pscustomobject]@{ Proc = $p; Hwnd = $h }
}

function StopApp($app) {
    $n = CloseAll $app.Proc.Id
    $null = $n
    if (-not $app.Proc.WaitForExit(5000)) { $app.Proc.Kill(); Write-Host "  !! did not exit (killed)"; $script:failed++ }
}

function ReadReport([string]$dir) {
    $path = Join-Path $dir 'surface.txt'
    if (-not (Test-Path $path)) { return $null }
    $map = @{}
    foreach ($line in Get-Content $path) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { $map[$line.Substring(0, $i)] = $line.Substring($i + 1) }
    }
    return $map
}

# A section that cannot do its arithmetic without the report says so, loudly and once.
function Require([string]$dir, [string]$label) {
    $report = ReadReport $dir
    if ($null -eq $report) { throw "$label`: the app wrote no placement report in $dir" }
    return $report
}

function ShotRegion([string]$path, $box) {
    $w = [int]$box.W; $h = [int]$box.H
    if ($w -le 0 -or $h -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$box.L, [int]$box.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ("{0}x{1}" -f $w, $h)
}

# --- the independent witness for "nothing was touched" ---

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

$realSettings = Join-Path $env:APPDATA 'Monitorn\settings.ini'
$realBefore = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
$probeBefore = ProbeRead

# ---------------------------------------------------------------------------
Write-Host "`n== the desktop as this verifier reads it (the baseline the app must agree with) =="

$seed = Launch 'the reading' "--place-report `"$ReportRoot\seed`""
$mon = MonitorRead $seed.Hwnd
$bar = TaskbarFor $mon.Handle
$barRect = if ($bar -ne [IntPtr]::Zero) { W32Rect $bar } else { $null }
$tray = if ($bar -ne [IntPtr]::Zero) { TrayParts $bar } else { $null }
Note "monitor      $($mon.Device)  full $(Str $mon.Full)  work $(Str $mon.Work)  dpi $($mon.Dpi)"
Note "taskbar      $(if ($barRect) { Str $barRect } else { 'not found' })  (class $(ClassName $bar))"
if ($tray) { Note "notification area $(Str $tray.Notify)   clock $(Str $tray.Clock)" }

Check ($null -ne $barRect) "a taskbar was found on the window's monitor"
if ($barRect) {
    # The verifier's own baseline: UI Automation and Win32 must agree about the taskbar, otherwise
    # one of them is reporting coordinates in some other space and nothing below means anything.
    Check (SameBox $tray.Bar $barRect 1) "UI Automation and Win32 agree on the taskbar rectangle (physical pixels)"
    Check ($mon.Work.B -le $barRect.T + 1) "the work area ends where the taskbar starts ($($mon.Work.B) <= $($barRect.T))"
} else {
    Check $false "a taskbar was found on the window's monitor"
    Check $false "the work area and the taskbar are consistent"
}
Check ($tray.Clock -ne $null -and $tray.Clock.W -gt 0) "the tray clock is readable through UI Automation ($(if ($tray.Clock) { Str $tray.Clock }))"

# ---------------------------------------------------------------------------
Write-Host "`n== where the window is (the claim) =="

$placed = UiaRect (Element $seed.Hwnd)
$placedW32 = W32Rect $seed.Hwnd
$report = Require (Join-Path $ReportRoot 'seed') 'the default placement'
Check ($null -ne $report) "the app wrote its placement report"
Check ($null -ne $placed) "UI Automation sees the surface window"

$dpi = [double]$report['dpi']
$workDip = ParseBox $report['workAreaDip']
$computed = ParseBox $report['computed']
$windowDip = ParseBox $report['window']
$computedPx = Scaled $computed $dpi
$marginPx = [double]$report['margin'] * ($dpi / 96.0)

Check ($report['monitor'] -eq $mon.Device) "the app placed against the monitor the window is on ($($report['monitor']))"
Check (SameBox (ParseBox $report['workArea']) $mon.Work 0.5) "and against the work area this verifier reads ($(Str $mon.Work))"
Check ([Math]::Abs($dpi - $mon.Dpi) -lt 0.01) "at the dpi this verifier reads ($($mon.Dpi))"
Check ($report['corner'] -eq 'bottom') "it chose the bottom corner, where the taskbar is"
Check ($report['forced'] -eq 'no') "with no seam in play"
Check (SameBox $windowDip $computed 1.0) "the window ended up where it computed ($(Str $computed))"
Check (SameBox $placedW32 $computedPx 12) "and Win32 sees the window there: $(Str $placedW32) vs $(Str $computedPx)"
Check (SameBox $placed $computedPx 12) "UI Automation too: $(Str $placed) vs $(Str $computedPx)"

# The claims that do not depend on the app's own arithmetic at all: the visible frame, against the
# work area and the taskbar that this verifier read for itself.
Check (Inside $placed $mon.Work 1) "the surface is inside the work area ($(Str $placed) in $(Str $mon.Work))"
if ($barRect) {
    Check (-not (Overlaps $placed $barRect 1)) "it does not overlap the taskbar"
    Check ($placed.B -le $barRect.T + 1) "its bottom edge is at or above the taskbar's top ($($placed.B) <= $($barRect.T))"
    Check ($placed.B -ge $barRect.T - 40) "and it is as low as it can be - not floating in the middle of the screen"
}
if ($tray) {
    Check ($placed.R -ge $tray.Notify.L) "its right edge is inside the notification area's column ($($placed.R) >= $($tray.Notify.L))"
    $toClock = $placed.R - $tray.Clock.R
    Note "the window's right edge is $toClock px right of the clock's right edge (the Show Desktop strip lies between them)"
    Check ($toClock -ge -1) "so it reaches at least the clock's column"
    Check ($toClock -le 60) "and no further right than the Show Desktop strip"
}
$fromRight = $mon.Work.R - $placed.R
$fromBottom = $mon.Work.B - $placed.B
Note "clearance: $fromRight px from the work area's right edge, $fromBottom px above its bottom (= the 8 dip margin at $dpi dpi)"
Check ($fromRight -le 30 -and $fromBottom -le 30) "both clearances are the 8 dip margin, not a coincidence"

[void](ShotRegion (Join-Path $ShotDir '01-placed.png') `
    (Box ($mon.Full.R - 780) ($mon.Full.B - 720) $mon.Full.R $mon.Full.B))

# ---------------------------------------------------------------------------
Write-Host "`n== the settings window the gear opens stays on screen too =="

$gear = @(FindType (Element $seed.Hwnd) $CT::Button | Where-Object { $_.Current.Name -like 'Settings for *' })
Check ($gear.Count -ge 1) "the surface still has its gear"
if ($gear.Count -ge 1) {
    ([System.Windows.Automation.InvokePattern]$gear[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $sh = [IntPtr]::Zero
    for ($i = 0; $i -lt 25 -and $sh -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $sh = HwndByTitle $seed.Proc.Id 'Monitorn settings'
    }
    Check ($sh -ne [IntPtr]::Zero) "the gear opened 'Monitorn settings'"
    if ($sh -ne [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 700
        $settings = W32Rect $sh
        Note "the settings window is $(Str $settings) on a 380 dip wide window"
        Check (Inside $settings $mon.Work 1) "it is fully inside the work area, not hanging off the bottom-right"
        # What centring on the owner would have produced, so the clamp is a measurement, not a claim.
        $owner = UiaRect (Element $seed.Hwnd)
        $wouldBeTop = (($owner.T + $owner.B) / 2) - ($settings.H / 2)
        $wouldBeBox = Box $settings.L $wouldBeTop $settings.R ($wouldBeTop + $settings.H)
        Note ("owner-centred it would have been " + (Str $wouldBeBox) + " (bottom " + [int]$wouldBeBox.B + " vs the work area's " + [int]$mon.Work.B + ")")
        if (-not (Inside $wouldBeBox $mon.Work 0)) {
            Check (-not (SameBox $settings $wouldBeBox 1)) "which is not where centring on the owner would have put it, so the clamp did the work"
        } else {
            Note "centring on the owner happened to fit, so the clamp had nothing to do here"
        }
        [void](ShotRegion (Join-Path $ShotDir '02-settings.png') $mon.Full)
        [void][WinExp09]::PostMessage($sh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
        Start-Sleep -Milliseconds 600
    }
}

StopApp $seed

# ---------------------------------------------------------------------------
Write-Host "`n== the baseline: without the placement, Windows decides =="

$before = Launch 'baseline (--no-place)' '--no-place'
$cascade = UiaRect (Element $before.Hwnd)
Note "unplaced, the window is $(Str $cascade) instead of $(Str $placed)"
Check ($null -ne $cascade) "the unplaced window is readable"
Check ([Math]::Abs($cascade.L - $placed.L) -gt 50 -or [Math]::Abs($cascade.T - $placed.T) -gt 50) "it sits somewhere else - the placement changes something"
Check (([Math]::Abs($cascade.R - $mon.Work.R) -gt 100) -or ([Math]::Abs($cascade.B - $mon.Work.B) -gt 100)) "and it is not in the flyout corner"
[void](ShotRegion (Join-Path $ShotDir '03-baseline.png') $mon.Full)
StopApp $before

# ---------------------------------------------------------------------------
Write-Host "`n== a work area that changes under the window re-places it =="

$shorter = Launch 'work area 120 px shorter (--place-work)' "--place-work 0,0,$([int]$mon.Full.R),$([int]$mon.Work.B - 120) --place-report `"$ReportRoot\shorter`""
$shorterRect = UiaRect (Element $shorter.Hwnd)
$shorterReport = Require (Join-Path $ReportRoot 'shorter') 'the shorter work area'
$expectedBottom = [double]$shorterReport['computed'].Split(',')[3]
Note "reported bottom $(Str (ParseBox $shorterReport['computed'])) -> $(Str (Scaled (ParseBox $shorterReport['computed']) $dpi)) in pixels"
Check ([Math]::Abs($expectedBottom - (($mon.Work.B - 120) / ($dpi / 96.0) - [double]$shorterReport['margin'])) -lt 0.6) `
    "the model put the bottom exactly one margin above the new work area edge"
Check ($shorterRect.B -lt $placed.B - 80) "the window physically moved up with it ($($shorterRect.B) vs $($placed.B))"
Check ($shorterRect.B -le $mon.Work.B - 100) "and it is now well clear of the real taskbar"
StopApp $shorter

# ---------------------------------------------------------------------------
Write-Host "`n== the margin is the one that was asked for =="

$wide = Launch 'a 40 dip margin (--place-margin)' "--place-margin 40 --place-report `"$ReportRoot\margin`""
$wideRect = UiaRect (Element $wide.Hwnd)
$wideReport = Require (Join-Path $ReportRoot 'margin') 'the 40 dip margin'
$wideComputed = Scaled (ParseBox $wideReport['computed']) $dpi
Note "computed $(Str $wideComputed), read $(Str $wideRect)"
Check ([Math]::Abs(($mon.Work.R - $wideComputed.R) - 50) -lt 0.6) "40 dip is 50 px: the model's right edge is 50 px in"
Check ([Math]::Abs(($placed.R - $wideRect.R) - 40) -lt 4) "the window physically moved left by about 40 px more, from the same 8 dip run"
Check (Inside $wideRect $mon.Work 1) "and it is still inside the work area"
StopApp $wide

# ---------------------------------------------------------------------------
Write-Host "`n== another taskbar edge puts it in another corner =="

$top = Launch 'as if the taskbar were on top (--place-edge top)' "--place-edge top --place-report `"$ReportRoot\top`""
$topRect = UiaRect (Element $top.Hwnd)
$topReport = Require (Join-Path $ReportRoot 'top') 'the top taskbar edge'
$topComputed = Scaled (ParseBox $topReport['computed']) $dpi
Note "computed $(Str $topComputed), read $(Str $topRect)"
Check ($topReport['corner'] -eq 'top' -and $topReport['forced'] -eq 'top') "the seam was in play and read as the top edge"
Check ($topRect.T -lt $placed.T - 100) "the window moved to the top of the work area ($($topRect.T) vs $($placed.T))"
Check ($topRect.T -le $mon.Work.T + 30) "it is at the work area's top edge (within a frame)"
Check ($topRect.R -ge $mon.Work.R - 40) "and still right-aligned"
StopApp $top

# ---------------------------------------------------------------------------
Write-Host "`n== the conversion is what put it there (--place-dpi 96) =="

$dpi96 = Launch 'as if the monitor were at 96 dpi' "--place-dpi 96 --place-report `"$ReportRoot\dpi96`""
$dpi96Report = Require (Join-Path $ReportRoot 'dpi96') 'the scripted dpi'
$dpi96Dip = ParseBox $dpi96Report['workAreaDip']
$dpi96Computed = ParseBox $dpi96Report['computed']
$dpi96Rect = UiaRect (Element $dpi96.Hwnd)
Note "work area in dip $(Str $dpi96Dip), computed $(Str $dpi96Computed), read $(Str $dpi96Rect)"
Check ($dpi96Report['dpi'] -eq '96') "the scripted dpi is in the report"
Check (SameBox $dpi96Dip (Box 0 0 $mon.Full.R $mon.Work.B) 0.5) "at 96 dpi the same pixels are 1:1 dips, not scaled down"
Check ([Math]::Abs($dpi96Computed.R - ($mon.Work.R - 8)) -lt 0.6) "so its right edge is 8 units from the work area's, in the unscaled space"
Check ($dpi96Rect.L -gt $placed.L + 100) "and the window physically landed somewhere else ($($dpi96Rect.L) vs $($placed.L)) - the conversion is real"
StopApp $dpi96

# ---------------------------------------------------------------------------
Write-Host "`n== a settings window wider than the surface is pulled back on screen =="

# A 320 dip surface is narrower than the 380 dip settings window, so centring on the owner puts the
# settings window's right edge past the work area's - which is what the clamp is for.
$narrow = Launch 'a narrow surface, then its gear (--width 320)' "--width 320 --place-report `"$ReportRoot\narrow`""
$narrowOwner = UiaRect (Element $narrow.Hwnd)
$narrowGear = @(FindType (Element $narrow.Hwnd) $CT::Button | Where-Object { $_.Current.Name -like 'Settings for *' })
Check ($narrowGear.Count -ge 1) "the narrow surface has its gear"
if ($narrowGear.Count -ge 1) {
    ([System.Windows.Automation.InvokePattern]$narrowGear[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $nh = [IntPtr]::Zero
    for ($i = 0; $i -lt 25 -and $nh -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $nh = HwndByTitle $narrow.Proc.Id 'Monitorn settings'
    }
    Check ($nh -ne [IntPtr]::Zero) "its settings window opened"
    if ($nh -ne [IntPtr]::Zero) {
        Start-Sleep -Milliseconds 700
        $narrowSettings = W32Rect $nh
        $centred = Box (($narrowOwner.L + $narrowOwner.R) / 2 - $narrowSettings.W / 2) (($narrowOwner.T + $narrowOwner.B) / 2 - $narrowSettings.H / 2) `
                       (($narrowOwner.L + $narrowOwner.R) / 2 + $narrowSettings.W / 2) (($narrowOwner.T + $narrowOwner.B) / 2 + $narrowSettings.H / 2)
        Note "owner $(Str $narrowOwner), centred it would be $(Str $centred), it is $(Str $narrowSettings)"
        Check ($centred.R -gt $mon.Work.R + 1) "centring on the narrow owner would hang $( [int]($centred.R - $mon.Work.R) ) px off the right edge"
        Check (Inside $narrowSettings $mon.Work 1) "the window is inside the work area anyway"
        Check (SameBox $narrowSettings (ClampBox $centred $mon.Work) 2) "and it is exactly where the clamp puts it, not where centring did"
        [void](ShotRegion (Join-Path $ShotDir '04-narrow-settings.png') $mon.Full)
        [void][WinExp09]::PostMessage($nh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
        Start-Sleep -Milliseconds 600
    }
}
StopApp $narrow

# ---------------------------------------------------------------------------
Write-Host "`n== nothing else moved =="

$probeAfter = ProbeRead
if ($null -ne $probeBefore -and $null -ne $probeAfter) {
    Check ($probeAfter -eq $probeBefore) "no monitor value changed across every run ($probeBefore -> $probeAfter)"
} else {
    Note "no read-only probe available, so the monitor value was not checked"
}
$realAfter = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
Check ($realAfter -eq $realBefore) "the user's real %APPDATA%\Monitorn\settings.ini is untouched"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("reports: $ReportRoot")
Write-Host ("shots:   $ShotDir")
if ($script:failed -gt 0) { exit 1 }
