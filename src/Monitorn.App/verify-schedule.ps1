# EXP06 verification helper - drive a whole night against the real display in about a minute.
#
# The app is launched with a scaled clock (--now/--rate) and a short schedule, so the same machine
# the headless test drives crosses turn-on, the fade, the night, a manual override and turn-off in
# seconds of real time. Everything is asserted against the read-only EXP02 probe, so the claim does
# not rest on the app agreeing with itself:
#
#   day value          -> the display reads what it read before (nothing is scheduled yet)
#   fade down          -> the probe follows the staircase down, monotonically, to the strength
#   manual override    -> the user's value stands; the schedule does not put it back
#   turn-off           -> nothing is written over the override, and the display keeps its value
#   close              -> clean exit, and the display still reads its original value
#
# Exit code is non-zero if any assertion fails.
#
#   .\verify-schedule.ps1                 # full scenario, shots into %TEMP%\monitorn-exp06
#   .\verify-schedule.ps1 -Rate 15        # a slower simulated clock (more margin per phase)
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
    [string]$ShotDir = (Join-Path $env:TEMP 'monitorn-exp06'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitorn-exp06\state\settings.ini')
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

# --- the independent witness: read a display's brightness with the EXP02 probe ---

function ProbeRead {
    $out = & $Probe 2>&1 | Out-String
    $m = [regex]::Match($out, 'brightness cur=(\d+)')
    if (-not $m.Success) { return -1 }
    return [int]$m.Groups[1].Value
}

function ActiveTargets {
    $out = & $Probe 2>&1 | Out-String
    $m = [regex]::Match($out, 'active paths=(\d+)')
    if (-not $m.Success) { return -1 }
    return [int]$m.Groups[1].Value
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
    return ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Value
}

function SetSlider($slider, [double]$v) {
    ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).SetValue($v)
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

$targets = ActiveTargets
Write-Host "`n== the monitor as the probe sees it =="
$orig = ProbeRead
if ($orig -lt 0) { throw "the probe could not read a brightness; nothing to verify against" }
Write-Host ("  active targets: {0}   brightness now: {1}" -f $targets, $orig)

# The night level has to differ from today's value, in whichever direction is safe.
$strength = if ($orig -ge 15) { $orig - 10 } else { [Math]::Min(100, $orig + 10) }
if ($strength -eq $orig) { $strength = [Math]::Max(0, $orig - 1) }
$descending = $strength -lt $orig

# A scripted night: start at 16:00, turn on 16:05, fade 2 minutes in 10-second steps, turn off 16:22.
# At --rate 30 one simulated minute is two real seconds, so the whole night is under a minute.
$today = (Get-Date).ToString('yyyy-MM-dd')
$args = "--now `"$today 16:00:00`" --rate $Rate --on 16:05 --off 16:22 --strength $strength --fade-seconds 120 --step-seconds 10 --settings-file `"$SettingsFile`""
Write-Host "`n== launch a scripted night =="
Write-Host ("  $Exe $args")
Write-Host ("  strength $strength (from $orig), fade 120 s in 10 s steps")

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
    $sliders = FindType $win ([System.Windows.Automation.ControlType]::Slider)
    Check ($sliders.Count -eq 1) "one row per active display: expected 1 slider for $targets active target(s) (got $($sliders.Count))"
    Check ((StatusText $win) -like '*daylight*') "the status says it is daylight (got '$(StatusText $win)')"
    Check ((StatusText $win) -like "*dims to $strength%*") "and it names the strength and the time it will dim ($strength%)"
    Check ((ProbeRead) -eq $orig) "the display still reads its own $orig - nothing is scheduled yet"
    [void](ShotGood $h (Join-Path $ShotDir '01-day.png'))

    if ($sliders.Count -ne 1) {
        Write-Host "`n!! cannot drive the schedule checks without exactly one row; stopping here"
    } else {
        Write-Host "`n== turn-on: the fade down, watched with the probe =="
        $seen = WaitStatus $win '*fading to*' 40
        Check ($null -ne $seen) "the app reports the fade starting ('fading to', got '$(StatusText $win)')"

        $samples = New-Object System.Collections.ArrayList
        for ($i = 0; $i -lt 6; $i++) {
            [void]$samples.Add((ProbeRead))
            Start-Sleep -Milliseconds 700
        }
        Write-Host ("  the probe saw: " + ($samples -join ' -> '))
        $lo = [Math]::Min($orig, $strength)
        $hi = [Math]::Max($orig, $strength)
        Check (@($samples | Where-Object { $_ -lt $lo -or $_ -gt $hi }).Count -eq 0) "every sample lies between the day value and the night level"
        $rising = $false
        for ($i = 1; $i -lt $samples.Count; $i++) { if (($descending -and $samples[$i] -gt $samples[$i - 1]) -or (-not $descending -and $samples[$i] -lt $samples[$i - 1])) { $rising = $true } }
        Check (-not $rising) "the display moved in one direction only while it faded"
        Check ((@($samples | Select-Object -Unique).Count) -gt 1) "more than one value was seen: the fade is a staircase on the real display too"

        Write-Host "`n== the night itself =="
        $night = WaitStatus $win '*until*' 45
        Check ($null -ne $night) "the app reports the night ('until', got '$(StatusText $win)')"
        Check ($night -like "*$strength% until 4:22 PM*") "it holds $strength% until the turn-off time"
        Start-Sleep -Milliseconds 1200
        $held = ProbeRead
        Check ($held -eq $strength) "the display itself reads the night level ($strength, probe saw $held)"
        Check (@(Texts $win) -contains $strength.ToString()) "and the row shows the level the monitor is at"
        [void](ShotGood $h (Join-Path $ShotDir '02-night.png'))

        Write-Host "`n== a manual move ends the schedule for the night =="
        SetSlider $sliders[0] $orig
        Start-Sleep -Milliseconds 2500
        $afterMove = ProbeRead
        Check ($afterMove -eq $orig) "the user's own $orig reached the monitor (probe saw $afterMove)"
        Check ((StatusText $win) -like '*until*') "the night is still on, but the schedule is not fighting the user"
        Check (@(Texts $win) -contains $orig.ToString()) "and the row shows the user's value"
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
        Check ((ProbeRead) -eq $orig) "the display keeps the user's value - no schedule write over an override"
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
Check ((ProbeRead) -eq $orig) "and the display is left at its original $orig (the escalation budget)"
$nowHash = if ((Test-Path $RealSettings)) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
Check ((Test-Path $RealSettings) -eq $realExisted -and $nowHash -eq $realHash) `
    "the user's real %APPDATA% settings file is untouched (this run used `"$SettingsFile`")"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
