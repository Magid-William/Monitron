# EXP06 verification helper - drive a whole night against the real displays in about a minute.
# EXP10 - relaxed to N displays: the night is witnessed on every row, over DDC/CI and over WMI.
#
# The app is launched with a scaled clock (--now/--rate) and a short schedule, so the same machine
# the headless test drives crosses turn-on, the fade, the night, a manual override and turn-off in
# seconds of real time. Everything is asserted against an outside witness, so the claim does not rest
# on the app agreeing with itself: the read-only EXP02 probe for the displays it can read over
# DDC/CI, and an independent read-only WMI query for an internal panel (which the probe cannot see).
# The rows are found by the display id the app puts on each row's slider as its UI automation id.
#
#   day value          -> every display reads what it read before (nothing is scheduled yet)
#   fade down          -> the probe follows the staircase down, monotonically, to the night level
#   manual override    -> the user's value stands on the display moved, the others stay held
#   turn-off           -> nothing is written over the override, and every display keeps its value
#   close              -> clean exit, and every display is back at its own original value
#
# The internal panel is witnessed at the night level and at the restore, but its staircase is not
# sampled: a WMI read is slower than a fade step (see the boundary in experiments/EXP10/README.md).
#
# Exit code is non-zero if any assertion fails.
#
#   .\verify-schedule.ps1                 # full scenario, shots into %TEMP%\monitorn-exp10
#   .\verify-schedule.ps1 -Rate 15        # a slower simulated clock (more margin per phase)
#   .\verify-schedule.ps1 -SkipInternal   # leave the internal panel out of the assertions
#
# EXP08: this runs on a temp settings file and deletes it first. It used to run with no
# --settings-file at all, which was only safe while no real settings file existed - once the user
# has one, its persisted "gradual fade" is inherited and the fade phase under test does not happen.
# (Same correction EXP07 made to verify-settings.ps1, and the reason this script started failing
# without any theme change.)
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitorn.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [double]$Rate = 30,
    [switch]$SkipInternal,
    [string]$ShotDir = (Join-Path $env:TEMP 'monitorn-exp10'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitorn-exp10\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }

# The defaults this scenario is written against (in particular gradual fade ON), and no writes to
# the user's real file.
$RealSettings = Join-Path $env:APPDATA 'Monitorn\settings.ini'
$realExisted = Test-Path $RealSettings
$realHash = if ($realExisted) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }

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
public static class WinExp06 {
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
# device it came from. Parsed per monitor block, so "which display" is answerable.
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

# The strength as a value in a display's own range - the same rule Schedule.cs uses.
function NightLevel([int]$min, [int]$max, [int]$strength) {
    if ($max -le $min) { return $min }
    return [int][Math]::Round($min + ($max - $min) * ($strength / 100.0), [MidpointRounding]::AwayFromZero)
}

# --- window helpers ---

function Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][WinExp06]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object WinExp06+RECT
    [void][WinExp06]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ("{0}x{1}" -f ($r.Right - $r.Left), ($r.Bottom - $r.Top))
}

function Content([IntPtr]$h) {
    $r = New-Object WinExp06+RECT
    [void][WinExp06]::GetWindowRect($h, [ref]$r)
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
    $cb = [WinExp06+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp06]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp06]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp06]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

function CloseAll([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [WinExp06+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp06]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp06]::IsWindowVisible($h)) {
            $r = New-Object WinExp06+RECT
            [void][WinExp06]::GetWindowRect($h, [ref]$r)
            if (($r.Right - $r.Left) -gt 0 -and ($r.Bottom - $r.Top) -gt 0) { [void]$found.Add($h) }
        }
        return $true
    }
    [void][WinExp06]::EnumWindows($cb, [IntPtr]::Zero)
    foreach ($w in $found) { [void][WinExp06]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
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

function RangeOf($slider) {
    $rv = [System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    return [pscustomobject]@{ Min = [int]$rv.Current.Minimum; Max = [int]$rv.Current.Maximum }
}

function SetSlider($slider, [double]$v) {
    ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).SetValue($v)
}

# One entry per row, keyed by the display id the row carries (EXP10): which display it drives, its
# range, and - for an internal panel - the WMI instance whose value witnesses it.
function Rows($win) {
    $rows = New-Object System.Collections.ArrayList
    foreach ($s in @(FindType $win ([System.Windows.Automation.ControlType]::Slider))) {
        $id = $s.Current.AutomationId
        $kind = 'unknown'
        $instance = ''
        if ($id -like 'DDC:*') { $kind = 'ddc' }
        elseif ($id -like 'WMI:*') { $kind = 'wmi'; $instance = $id.Substring(4) }
        $r = RangeOf $s
        [void]$rows.Add([pscustomobject]@{ Slider = $s; Id = $id; Kind = $kind; Instance = $instance;
            Min = $r.Min; Max = $r.Max; Value = (ValueOf $s) })
    }
    return @($rows)
}

# The status line is found by its automation id and read by its UIA name, which is its text.
function StatusText($root) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'NightLightStatus')
    $el = $root.FindFirst($TS::Descendants, $cond)
    if ($null -eq $el) { return '' }
    return $el.Current.Name
}

function WaitStatus($root, [string]$like, [int]$seconds) {
    for ($i = 0; $i -lt ($seconds * 4); $i++) {
        $t = StatusText $root
        if ($t -like $like) { return $t }
        Start-Sleep -Milliseconds 250
    }
    return $null
}

# ============================================================================

$targets = ActivePaths
$probe0 = @(ProbeMonitors)
Write-Host "`n== the displays as the witnesses see them =="
Write-Host ("  active targets (probe): {0}" -f $targets)
foreach ($m in $probe0) { Write-Host ("  DDC {0}: {1}" -f $m.Device, $m.Value) }
if ($probe0.Count -eq 0) { throw "the probe could not read any display over DDC/CI; nothing to witness against" }
$day = $probe0[0].Value

# The night level has to differ from today's value, in whichever direction is safe.
$strength = if ($day -ge 15) { $day - 10 } else { [Math]::Min(100, $day + 10) }
if ($strength -eq $day) { $strength = [Math]::Max(0, $day - 1) }
$descending = $strength -lt $day

# A scripted night: start at 16:00, turn on 16:05, fade 2 minutes in 10-second steps, turn off 16:22.
# At --rate 30 one simulated minute is two real seconds, so the whole night is under a minute.
$today = (Get-Date).ToString('yyyy-MM-dd')
$args = "--now `"$today 16:00:00`" --rate $Rate --on 16:05 --off 16:22 --strength $strength --fade-seconds 120 --step-seconds 10 --no-tray --settings-file `"$SettingsFile`""
Write-Host "`n== launch a scripted night =="
Write-Host ("  $Exe $args")
Write-Host ("  strength $strength (from $day), fade 120 s in 10 s steps")

$p = Start-Process -FilePath $Exe -ArgumentList $args -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
    if ($p.HasExited) { throw "process exited early with code $($p.ExitCode)" }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp06]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }

[void][WinExp06]::BringWindowToTop($h)
for ($i = 0; $i -lt 20; $i++) {
    [void][WinExp06]::SetForegroundWindow($h)
    if ([WinExp06]::GetForegroundWindow() -eq $h) { break }
    if ($i -eq 5) { [System.Windows.Forms.SendKeys]::SendWait('%') }
    Start-Sleep -Milliseconds 200
}

$winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win = $AE::RootElement.FindFirst($TS::Children, $winCond)
if ($null -eq $win) { $p.Kill(); throw "UI Automation could not find the window" }

$script:closedClean = $false

try {
    Write-Host "`n== before turn-on: the day is untouched =="
    Check ((($win.Current.Name)) -eq 'Monitorn') "window is titled 'Monitorn' (got '$($win.Current.Name)')"
    $rows = @(Rows $win)
    $ids = ($rows | ForEach-Object { $_.Id }) -join ' | '
    Check ($rows.Count -eq $targets) "one row per active display: expected $targets slider(s) for $targets active target(s) (got $($rows.Count))"
    Check ($rows.Count -ge 1) "the surface has rows to drive"
    Check (@($rows | Where-Object { $_.Kind -eq 'unknown' }).Count -eq 0) "every row carries its display id (got '$ids')"

    $ddcRows = @($rows | Where-Object { $_.Kind -eq 'ddc' })
    $wmiRows = @($rows | Where-Object { $_.Kind -eq 'wmi' })
    Check ($ddcRows.Count -eq $probe0.Count) "each display the probe can read has its own row ($($ddcRows.Count) row(s) vs $($probe0.Count) readable display(s))"
    foreach ($r in $wmiRows) {
        $w = WmiRead $r.Instance
        Check ($w -eq $r.Value) "the panel row shows the panel's real brightness ($($r.Value); independent WMI read saw $w)"
    }
    # The row the override is driven on: the externally witnessed one, whose own value the probe
    # reads. With more than one such row the probe's device-to-row mapping is not derivable, so the
    # override is asserted as "exactly one readable display now holds the user's value" below.
    $driveRow = $ddcRows[0]
    Check (@($probe0 | Where-Object { $_.Value -eq $driveRow.Value }).Count -ge 1) `
        "the row to override shows a value the probe can see ($($driveRow.Value))"

    Check ((StatusText $win) -like '*daylight*') "the status says it is daylight (got '$(StatusText $win)')"
    Check ((StatusText $win) -like "*dims to $strength%*") "and it names the strength and the time it will dim ($strength%)"
    Check (((ProbeValues) -join ',') -eq (($probe0 | ForEach-Object { $_.Value }) -join ',')) "every display the probe can read still reads its own value - nothing is scheduled yet"
    [void](ShotGood $h (Join-Path $ShotDir '01-day.png'))

    if ($rows.Count -eq 0) {
        Write-Host "`n!! cannot drive the schedule checks without a row; stopping here"
    } else {
        Write-Host "`n== turn-on: the fade down, watched with the probe =="
        $seen = WaitStatus $win '*fading to*' 40
        Check ($null -ne $seen) "the app reports the fade starting ('fading to', got '$(StatusText $win)')"

        # The probe cannot read as fast as a step, so the staircase claim is per readable display.
        $samples = New-Object System.Collections.ArrayList
        for ($i = 0; $i -lt 6; $i++) {
            [void]$samples.Add(@(ProbeValues))
            Start-Sleep -Milliseconds 700
        }
        $width = ($samples | ForEach-Object { $_.Count } | Measure-Object -Minimum).Minimum
        for ($di = 0; $di -lt $width; $di++) {
            $series = @($samples | ForEach-Object { $_[$di] })
            Write-Host ("  display $($di + 1) as the probe saw it: " + ($series -join ' -> '))
            $lo = [Math]::Min($day, $strength)
            $hi = [Math]::Max($day, $strength)
            Check (@($series | Where-Object { $_ -lt $lo -or $_ -gt $hi }).Count -eq 0) "every sample of display $($di + 1) lies between the day value and the night level"
            $rising = $false
            for ($i = 1; $i -lt $series.Count; $i++) { if (($descending -and $series[$i] -gt $series[$i - 1]) -or (-not $descending -and $series[$i] -lt $series[$i - 1])) { $rising = $true } }
            Check (-not $rising) "display $($di + 1) moved in one direction only while it faded"
            Check ((@($series | Select-Object -Unique).Count) -gt 1) "more than one value was seen on display $($di + 1): the fade is a staircase on the real display too"
        }

        Write-Host "`n== the night itself =="
        $night = WaitStatus $win '*until*' 45
        Check ($null -ne $night) "the app reports the night ('until', got '$(StatusText $win)')"
        Check ($night -like "*$strength% until 4:22 PM*") "it holds $strength% until the turn-off time"
        Start-Sleep -Milliseconds 1200
        $heldProbe = @(ProbeValues)
        Check ((@($heldProbe | Where-Object { $_ -ne $strength }).Count -eq 0)) "every display the probe can read is at the night level ($strength, probe saw $($heldProbe -join ','))"
        foreach ($r in $rows) {
            $want = NightLevel $r.Min $r.Max $strength
            Check ((ValueOf $r.Slider) -eq $want) "row '$($r.Id)' shows the level the schedule holds ($want)"
        }
        foreach ($r in $wmiRows) {
            if ($SkipInternal) { continue }
            $w = WmiRead $r.Instance
            $want = NightLevel $r.Min $r.Max $strength
            Check ($w -eq $want) "the internal panel itself is at the night level ($want; independent WMI read saw $w)"
        }
        [void](ShotGood $h (Join-Path $ShotDir '02-night.png'))

        Write-Host "`n== a manual move ends the schedule for the night, on that display only =="
        SetSlider $driveRow.Slider $driveRow.Value
        Start-Sleep -Milliseconds 2500
        $afterMove = @(ProbeValues)
        Check ((@($afterMove | Where-Object { $_ -eq $driveRow.Value }).Count) -ge 1) "the user's own $($driveRow.Value) reached the display the slider drives (probe saw $($afterMove -join ','))"
        Check ((@($afterMove | Where-Object { $_ -eq $strength }).Count) -eq ($probe0.Count - 1)) "the other externally driven display(s) are still held at $strength - the override is per display"
        Check ((StatusText $win) -like '*until*') "the night is still on, but the schedule is not fighting the user"
        foreach ($r in $rows) {
            if ($r.Id -eq $driveRow.Id) { continue }
            $want = NightLevel $r.Min $r.Max $strength
            if ($r.Kind -eq 'wmi') {
                if (-not $SkipInternal) { Check ((WmiRead $r.Instance) -eq $want) "and the panel is still held at $want" }
            } else {
                Check ((ValueOf $r.Slider) -eq $want) "and row '$($r.Id)' is still held at $want"
            }
        }
        [void](ShotGood $h (Join-Path $ShotDir '03-override.png'))

        Write-Host "`n== turn-off: the override is left alone =="
        $ended = $null
        for ($i = 0; $i -lt 160; $i++) {
            if ((StatusText $win) -notlike '*until*') { $ended = StatusText $win; break }
            Start-Sleep -Milliseconds 250
        }
        Check ($null -ne $ended) "the night ended (status now '$(StatusText $win)')"
        Check (($ended -like '*daylight*') -or ($ended -like '*returning*')) "and the app says so"
        Start-Sleep -Milliseconds 1500
        Check (((ProbeValues) -join ',') -eq (($probe0 | ForEach-Object { $_.Value }) -join ',')) "every display the probe can read keeps its value - no schedule write over an override"
        # The panel is not overridden, so turn-off ramps it back up its own staircase: wait for the
        # ramp to finish rather than sampling it at an arbitrary instant mid-fade.
        foreach ($r in $wmiRows) {
            if ($SkipInternal) { continue }
            $first = -1
            $back = -1
            for ($i = 0; $i -lt 40; $i++) {
                $back = WmiRead $r.Instance
                if ($first -lt 0) { $first = $back }
                if ($back -eq $r.Value) { break }
                Start-Sleep -Milliseconds 250
            }
            Check ($back -eq $r.Value) "and the panel is back at its own $($r.Value) (independent WMI read saw $back, first saw $first after turn-off)"
        }
        [void](ShotGood $h (Join-Path $ShotDir '04-day-after.png'))

        Write-Host "`n== the settings window edits the same schedule the night ran on =="
        $gear = @((FindType $win ([System.Windows.Automation.ControlType]::Button)) | Where-Object { $_.Current.Name -like 'Settings for *' })[0]
        if ($null -ne $gear) {
            ([System.Windows.Automation.InvokePattern]$gear.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
            $sh = [IntPtr]::Zero
            for ($i = 0; $i -lt 20 -and $sh -eq [IntPtr]::Zero; $i++) {
                Start-Sleep -Milliseconds 200
                $sh = HwndByTitle $p.Id 'Monitorn settings'
            }
            Check ($sh -ne [IntPtr]::Zero) "the gear opens 'Monitorn settings'"
            if ($sh -ne [IntPtr]::Zero) {
                $sw = $AE::FromHandle($sh)
                $edits = @((FindType $sw ([System.Windows.Automation.ControlType]::Edit)))
                $values = @($edits | ForEach-Object { ([System.Windows.Automation.ValuePattern]$_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value })
                Check (($values -join ',') -eq "$strength,4,05,4,22") "it shows the running schedule: strength $strength, turn on 4:05, turn off 4:22 (got $($values -join ','))"
                Check (@((FindType $sw ([System.Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name }) -contains 'Schedule night light') "and it is the night-light schedule"
                [void][WinExp06]::PostMessage($sh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
                Start-Sleep -Milliseconds 700
            }
        } else {
            Check $false "the gear button is present to test"
        }
    }
} finally {
    Write-Host "`n== close =="
    $n = CloseAll $p.Id
    Write-Host "asked $n window(s) to close"
    if (-not $p.WaitForExit(8000)) { $p.Kill(); Write-Host "!! did not exit (killed)"; $script:failed++ }
    else {
        Write-Host ("closed cleanly: exitCode=" + $p.ExitCode)
        $script:closedClean = $true
    }
}

Check ($script:closedClean) "the app exited by itself"
Check (((ProbeValues) -join ',') -eq (($probe0 | ForEach-Object { $_.Value }) -join ',')) "and every display the probe can read is left at its original value (the escalation budget)"
if ($null -ne $rows) {
    foreach ($r in @($rows | Where-Object { $_.Kind -eq 'wmi' })) {
        if ($SkipInternal) { continue }
        Check ((WmiRead $r.Instance) -eq $r.Value) "and the internal panel is left at its own original $($r.Value)"
    }
}
$nowHash = if ((Test-Path $RealSettings)) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
Check ((Test-Path $RealSettings) -eq $realExisted -and $nowHash -eq $realHash) `
    "the user's real %APPDATA% settings file is untouched (this run used `"$SettingsFile`")"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
