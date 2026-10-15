# EXP05 verification helper - drive the real surface and prove a slider reaches the monitor.
# EXP10 - relaxed to N displays: every row is driven, each against its own outside witness.
#
# It reads each display independently (so the check does not rest on the app agreeing with itself):
# the read-only EXP02 probe for an external display over DDC/CI, and a read-only WMI query for an
# internal panel (which the probe cannot see). The window's rows are found by the display id the app
# puts on each row's slider as its UI Automation id ("DDC:<device path>" / "WMI:<InstanceName>"), so
# each drive is asserted against the witness of the display that row drives - not against whichever
# row happens to show the same number. Screenshots of each state are written for the visual record.
# Exit code is non-zero if any assertion fails.
#
#   .\verify-control.ps1                     # full scenario, shots into %TEMP%\monitron-exp10
#   .\verify-control.ps1 -ShotDir .\out      # keep the evidence somewhere else
#   .\verify-control.ps1 -SkipInternal       # leave the internal panel's row undriven
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [string]$AppArgs = '',
    [switch]$SkipInternal,
    [string]$ShotDir = (Join-Path $env:TEMP 'monitron-exp10'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitron-exp10\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }

# A temp settings file, and no schedule: this pass drives sliders, it does not test the night.
# (Without the temp file the user's own persisted settings would be loaded and edited.)
$RealSettings = Join-Path $env:APPDATA 'Monitron\settings.ini'
$realExisted = Test-Path $RealSettings
$realHash = if ($realExisted) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }
$AppArgs = "--no-schedule --no-tray --settings-file `"$SettingsFile`" $AppArgs"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
// Named per-experiment on purpose: a PowerShell session keeps types once added, so two verifier
// scripts sharing a class name collide in the second one's Add-Type.
public static class WinExp05 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
}
"@

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

$script:passed = 0
$script:failed = 0

function Check($condition, [string]$what) {
    if ($condition) { $script:passed++; Write-Host "  ok   $what" }
    else { $script:failed++; Write-Host "  FAIL $what" }
}

# --- the independent witnesses ---

# The EXP02 probe's own output, one entry per display it could read over DDC/CI, each next to the
# device it came from. Parsed per monitor block, so "which display" is answerable and not just
# "some display read 57".
function ProbeMonitors {
    $out = & $Probe 2>&1 | Out-String
    $list = New-Object System.Collections.ArrayList
    $device = '?'
    foreach ($line in ($out -split "`n")) {
        $d = [regex]::Match($line, 'monitor\[\d+\] device=(\S+)')
        if ($d.Success) { $device = $d.Groups[1].Value; continue }
        $b = [regex]::Match($line, 'brightness cur=(\d+)')
        if ($b.Success) { [void]$list.Add([pscustomobject]@{ Device = $device; Value = [int]$b.Groups[1].Value }) }
    }
    return @($list)
}

function ActivePaths {
    $out = & $Probe 2>&1 | Out-String
    $m = [regex]::Match($out, 'active paths=(\d+)')
    if (-not $m.Success) { return -1 }
    return [int]$m.Groups[1].Value
}

# The internal panel's own brightness, read the way the app writes it but from this process: an
# outside witness for a display the DDC probe cannot see.
function WmiRead([string]$instance) {
    try {
        $v = Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction Stop |
            Where-Object { $_.InstanceName -eq $instance } | Select-Object -First 1
        if ($null -eq $v) { return -1 }
        return [int]$v.CurrentBrightness
    } catch { return -1 }
}

function ProbeValues { return @(ProbeMonitors | ForEach-Object { $_.Value }) }

# The positions at which two readings differ - the probe's order is the OS's own and stable.
function ChangedPositions($before, $after) {
    $n = [Math]::Min($before.Count, $after.Count)
    $out = New-Object System.Collections.ArrayList
    for ($i = 0; $i -lt $n; $i++) { if ($before[$i] -ne $after[$i]) { [void]$out.Add($i) } }
    return @($out)
}

# --- window helpers ---

function Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][WinExp05]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object WinExp05+RECT
    [void][WinExp05]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ("{0}x{1}" -f ($r.Right - $r.Left), ($r.Bottom - $r.Top))
}

function Content([IntPtr]$h) {
    $r = New-Object WinExp05+RECT
    [void][WinExp05]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $base = $bmp.GetPixel(3, 3)
    $n = 0
    for ($y = 0; $y -lt $bmp.Height; $y += 3) {
        for ($x = 0; $x -lt $bmp.Width; $x += 3) {
            $p = $bmp.GetPixel($x, $y)
            if ([Math]::Abs($p.R - $base.R) -gt 10 -or [Math]::Abs($p.G - $base.G) -gt 10 -or [Math]::Abs($p.B - $base.B) -gt 10) { $n++ }
        }
    }
    $bmp.Dispose()
    return $n
}

function ShotGood([IntPtr]$h, [string]$path) {
    for ($try = 1; $try -le 4; $try++) {
        if ($try -gt 1) { Start-Sleep -Milliseconds 600 }
        $where = Shot $h $path
        $c = Content $h
        if ($c -gt 150) { Write-Host ("  shot $where content=$c -> $path"); return $true }
        Write-Host ("  (frame $try looks blank: content=$c - retrying)")
    }
    Write-Host "  !! giving up: last frame still looks blank"
    return $false
}

function HwndByTitle([int]$targetPid, [string]$title) {
    $hits = New-Object System.Collections.ArrayList
    $cb = [WinExp05+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp05]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp05]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp05]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

function CloseAll([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [WinExp05+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp05]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp05]::IsWindowVisible($h)) {
            $r = New-Object WinExp05+RECT
            [void][WinExp05]::GetWindowRect($h, [ref]$r)
            if (($r.Right - $r.Left) -gt 0 -and ($r.Bottom - $r.Top) -gt 0) { [void]$found.Add($h) }
        }
        return $true
    }
    [void][WinExp05]::EnumWindows($cb, [IntPtr]::Zero)
    foreach ($w in $found) { [void][WinExp05]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    return $found.Count
}

# --- UI Automation helpers ---

function FindType($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $AE::ControlTypeProperty, $controlType
    return @($root.FindAll($TS::Descendants, $cond))
}

function Texts($root) {
    return @((FindType $root ([System.Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name })
}

function ValueOf($slider) {
    return [int]([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Value
}

function MaximumOf($slider) {
    return ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Maximum
}

function SetSlider($slider, [double]$v) {
    ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).SetValue($v)
}

# One entry per row, keyed by the display id the row carries (EXP10): which display it drives, and
# - for an internal panel - the WMI instance whose value witnesses it.
function Rows($win) {
    $rows = New-Object System.Collections.ArrayList
    foreach ($s in @(FindType $win ([System.Windows.Automation.ControlType]::Slider))) {
        $id = $s.Current.AutomationId
        $kind = 'unknown'
        $instance = ''
        if ($id -like 'DDC:*') { $kind = 'ddc' }
        elseif ($id -like 'WMI:*') { $kind = 'wmi'; $instance = $id.Substring(4) }
        [void]$rows.Add([pscustomobject]@{ Slider = $s; Id = $id; Kind = $kind; Instance = $instance; Value = (ValueOf $s) })
    }
    return @($rows)
}

# ============================================================================

$targets = ActivePaths
$probe0 = @(ProbeMonitors)
Write-Host "`n== the displays as the witnesses see them =="
Write-Host ("  active targets (probe): {0}" -f $targets)
foreach ($m in $probe0) { Write-Host ("  DDC {0}: {1}" -f $m.Device, $m.Value) }
if ($probe0.Count -eq 0) { throw "the probe could not read any display over DDC/CI; nothing to witness against" }

$p = Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
    if ($p.HasExited) { throw "process exited early with code $($p.ExitCode)" }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp05]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }

[void][WinExp05]::BringWindowToTop($h)
for ($i = 0; $i -lt 20; $i++) {
    [void][WinExp05]::SetForegroundWindow($h)
    if ([WinExp05]::GetForegroundWindow() -eq $h) { break }
    if ($i -eq 5) { [System.Windows.Forms.SendKeys]::SendWait('%') }
    Start-Sleep -Milliseconds 200
}
Start-Sleep -Milliseconds 1600   # let it paint and settle

$winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win = $AE::RootElement.FindFirst($TS::Children, $winCond)
if ($null -eq $win) { $p.Kill(); throw "UI Automation could not find the window" }

Write-Host "`n== the surface reflects the real displays =="
Check ((($win.Current.Name)) -eq 'Monitron') "window is titled 'Monitron' (got '$($win.Current.Name)')"

$rows = @(Rows $win)
$ids = ($rows | ForEach-Object { $_.Id }) -join ' | '
Check ($rows.Count -eq $targets) "one row per active display: expected $targets slider(s) for $targets active target(s) (got $($rows.Count))"
Check ($rows.Count -ge 1) "the surface has rows to drive"
Check (@($rows | Where-Object { $_.Kind -eq 'unknown' }).Count -eq 0) "every row carries its display id (got '$ids')"

$ddcRows = @($rows | Where-Object { $_.Kind -eq 'ddc' })
$wmiRows = @($rows | Where-Object { $_.Kind -eq 'wmi' })
Check ($ddcRows.Count -eq $probe0.Count) "each display the probe can read has its own row ($($ddcRows.Count) row(s) vs $($probe0.Count) readable display(s))"

$gears = @((FindType $win ([System.Windows.Automation.ControlType]::Button)) | Where-Object { $_.Current.Name -like 'Settings for *' })
Check ($gears.Count -eq $rows.Count) "one gear per row ($($gears.Count) gear(s) for $($rows.Count) row(s))"
foreach ($row in $rows) {
    $g = @($gears | Where-Object { $_.Current.AutomationId -eq $row.Id })
    Check ($g.Count -eq 1) "row '$($row.Id)' has its own gear (named '$($g[0].Current.Name)')"
}

foreach ($row in $wmiRows) {
    $w = WmiRead $row.Instance
    Check ($w -eq $row.Value) "the panel row shows the panel's real brightness ($($row.Value); independent WMI read saw $w)"
}
foreach ($m in $probe0) {
    Check (@($rows | Where-Object { $_.Value -eq $m.Value }).Count -ge 1) "a row shows $($m.Device)'s real brightness ($($m.Value))"
}
[void](ShotGood $h (Join-Path $ShotDir '01-before.png'))

# --- drive every row away from its own value and back, each against its own witness ---

$driven = 0
for ($i = 0; $i -lt $rows.Count; $i++) {
    $row = $rows[$i]
    $orig = $row.Value
    $max = MaximumOf $row.Slider

    if ($row.Kind -eq 'unknown') {
        Write-Host "`n== row '$($row.Id)': no witness for this kind; not driven =="
        Check $false "row '$($row.Id)' is a display kind this verifier can witness"
        continue
    }
    if ($row.Kind -eq 'wmi' -and $SkipInternal) {
        Write-Host "`n== row '$($row.Id)': internal panel, skipped (-SkipInternal) =="
        continue
    }

    # Move away from the original, clamped, so the target always differs.
    $away = if ($orig -ge 10) { $orig - 10 } else { [Math]::Min($orig + 10, [int]$max) }
    if ($away -eq $orig) { $away = [Math]::Max(0, $orig - 1) }

    if ($row.Kind -eq 'ddc') {
        Write-Host "`n== external row '$($row.Id)' ($orig): drive it to $away =="
        $before = ProbeValues
        SetSlider $row.Slider $away
        Start-Sleep -Milliseconds 400
        Check ((ValueOf $row.Slider) -eq $away) "the slider took the value ($(ValueOf $row.Slider))"
        Start-Sleep -Milliseconds 1600     # debounce (250 ms) + DDC/CI write latency
        $after = ProbeValues
        $changed = @(ChangedPositions $before $after)
        Write-Host ("  the probe saw: " + ($before -join ',') + " -> " + ($after -join ','))
        Check ($changed.Count -eq 1) "exactly one display the probe can read moved"
        if ($changed.Count -eq 1) {
            Check ($before[$changed[0]] -eq $orig) "and it was the one showing this row's value ($orig)"
            Check ($after[$changed[0]] -eq $away) "the display itself now reads $away (probe saw $($after[$changed[0]]))"
        }
        Check ((ValueOf $row.Slider) -eq $away) "the row still shows the value it wrote ($away)"
        [void](ShotGood $h (Join-Path $ShotDir ("02-row{0}-lowered.png" -f ($i + 1))))

        Write-Host "`n== drive the external row back to $orig =="
        SetSlider $row.Slider $orig
        Start-Sleep -Milliseconds 400
        Start-Sleep -Milliseconds 1600
        $back = ProbeValues
        Check ((($back -join ',') -eq ($before -join ','))) "the display is restored to $orig (probe saw $($back -join ','))"
        Check ((ValueOf $row.Slider) -eq $orig) "the row shows the restored value ($orig)"
        [void](ShotGood $h (Join-Path $ShotDir ("03-row{0}-restored.png" -f ($i + 1))))
    }
    else {
        Write-Host "`n== internal row '$($row.Id)' ($orig): drive it to $away =="
        $probeBefore = ProbeValues
        $wmiBefore = WmiRead $row.Instance
        Check ($wmiBefore -eq $orig) "the panel's own WMI value equals the row's before the drive ($wmiBefore)"
        SetSlider $row.Slider $away
        Start-Sleep -Milliseconds 400
        Check ((ValueOf $row.Slider) -eq $away) "the slider took the value ($(ValueOf $row.Slider))"
        Start-Sleep -Milliseconds 2000     # debounce + the WMI write
        $wmiAfter = WmiRead $row.Instance
        Check ($wmiAfter -eq $away) "the internal panel itself now reads $away (independent WMI read saw $wmiAfter)"
        Check ((ValueOf $row.Slider) -eq $away) "the row still shows the value it wrote ($away)"
        Check (((ProbeValues) -join ',') -eq ($probeBefore -join ',')) "and the external display was not touched"
        [void](ShotGood $h (Join-Path $ShotDir ("02-row{0}-lowered.png" -f ($i + 1))))

        Write-Host "`n== drive the internal row back to $orig =="
        SetSlider $row.Slider $orig
        Start-Sleep -Milliseconds 400
        Start-Sleep -Milliseconds 2000
        $wmiBack = WmiRead $row.Instance
        Check ($wmiBack -eq $orig) "the panel is restored to $orig (independent WMI read saw $wmiBack)"
        [void](ShotGood $h (Join-Path $ShotDir ("03-row{0}-restored.png" -f ($i + 1))))
    }
    $driven++
}
Write-Host ""
Write-Host ("rows driven: {0} of {1}" -f $driven, $rows.Count)

if ($rows.Count -eq 0) {
    Write-Host "`n!! cannot drive anything without a row; stopping here"
    $n = CloseAll $p.Id
    if (-not $p.WaitForExit(5000)) { $p.Kill() }
    Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
    if ($script:failed -gt 0) { exit 1 }
    return
}

Write-Host "`n== the gear still opens the settings window =="
$gear = @($gears)[0]
if ($null -ne $gear) {
    ([System.Windows.Automation.InvokePattern]$gear.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 700
    $sh = [IntPtr]::Zero
    for ($i = 0; $i -lt 20 -and $sh -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $sh = HwndByTitle $p.Id 'Monitron settings'
    }
    Check ($sh -ne [IntPtr]::Zero) "invoking the gear opens 'Monitron settings'"
    if ($sh -ne [IntPtr]::Zero) {
        $sw = $AE::FromHandle($sh)
        $swTexts = Texts $sw
        Check ($swTexts -contains 'Schedule night light') "and it is the night-light schedule"
        [void][WinExp05]::PostMessage($sh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
        Start-Sleep -Milliseconds 700
    }
} else {
    Check $false "the gear button is present to test"
}

Write-Host "`n== close =="
$n = CloseAll $p.Id
Write-Host "asked $n window(s) to close"
if (-not $p.WaitForExit(5000)) { $p.Kill(); Write-Host "!! did not exit (killed)"; $script:failed++ }
else { Write-Host ("closed cleanly: exitCode=" + $p.ExitCode) }

Write-Host "`n== the escalation budget: every display left where it was =="
$probeEnd = @(ProbeValues)
Check ((($probeEnd -join ',') -eq (($probe0 | ForEach-Object { $_.Value }) -join ','))) `
    "every display the probe can read is back at its original value ($($probeEnd -join ','))"
foreach ($row in $wmiRows) {
    $w = WmiRead $row.Instance
    Check ($w -eq $row.Value) "the internal panel is back at its original value ($($row.Value); WMI read saw $w)"
}
$nowHash = if ((Test-Path $RealSettings)) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
Check ((Test-Path $RealSettings) -eq $realExisted -and $nowHash -eq $realHash) `
    "the user's real %APPDATA% settings file is untouched (this run used `"$SettingsFile`")"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
