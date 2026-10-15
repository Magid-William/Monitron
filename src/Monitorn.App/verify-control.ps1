# EXP05 verification helper - drive the real surface and prove a slider reaches the monitor.
#
# It reads the monitor independently with the read-only EXP02 probe (so the check does not rest on
# the app agreeing with itself), then drives the app's slider through UI Automation's RangeValue
# pattern away from and back to the original value, asserting each time that the probe's own read
# followed. Screenshots of each state are written for the visual record. Exit code is non-zero if
# any assertion fails.
#
#   .\verify-control.ps1                 # full scenario, shots into %TEMP%\monitorn-exp05
#   .\verify-control.ps1 -ShotDir .\out  # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitorn.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [string]$AppArgs = '',
    [string]$ShotDir = (Join-Path $env:TEMP 'monitorn-exp05')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }

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

# --- the independent witness: read a display's brightness with the EXP02 probe ---

function ProbeRead {
    $out = & $Probe 2>&1 | Out-String
    $m = [regex]::Match($out, 'brightness cur=(\d+)')
    if (-not $m.Success) { throw "probe could not read a brightness: $out" }
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
    return ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Value
}

function SetSlider($slider, [double]$v) {
    ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).SetValue($v)
}

# ============================================================================

$targets = ActiveTargets
Write-Host "`n== the monitor as the probe sees it =="
$orig = ProbeRead
Write-Host ("  active targets: {0}   brightness now: {1}" -f $targets, $orig)

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

Write-Host "`n== the surface reflects the real display =="
Check ((($win.Current.Name)) -eq 'Monitorn') "window is titled 'Monitorn' (got '$($win.Current.Name)')"

$sliders = FindType $win ([System.Windows.Automation.ControlType]::Slider)
Check ($sliders.Count -eq 1) "one row per active display: expected 1 slider for $targets active target(s) (got $($sliders.Count))"

$names = Texts $win
Check ($names -contains ($orig.ToString())) "the row shows the monitor's real brightness ($orig) read for it"
$gearNames = @((FindType $win ([System.Windows.Automation.ControlType]::Button)) | ForEach-Object { $_.Current.Name } | Where-Object { $_ -like 'Settings for *' })
Check ($gearNames.Count -eq 1) "the row is bound to a real monitor (gear named '$($gearNames -join ',')')"
[void](ShotGood $h (Join-Path $ShotDir '01-before.png'))

if ($sliders.Count -ne 1) {
    Write-Host "`n!! cannot drive the slider checks without exactly one row; stopping here"
    $n = CloseAll $p.Id
    if (-not $p.WaitForExit(5000)) { $p.Kill() }
    Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
    if ($script:failed -gt 0) { exit 1 }
    return
}

$slider = $sliders[0]
$max = ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Maximum

# Move away from the original, clamped, so the target always differs.
$away = if ($orig -ge 10) { $orig - 10 } else { [Math]::Min($orig + 10, $max) }
if ($away -eq $orig) { $away = [Math]::Max(0, $orig - 1) }

Write-Host "`n== drive the slider away from $orig to $away =="
SetSlider $slider $away
Start-Sleep -Milliseconds 400
Check ((ValueOf $slider) -eq $away) "the slider took the value ($(ValueOf $slider))"
Start-Sleep -Milliseconds 1600     # debounce (250 ms) + DDC/CI write latency
$read = ProbeRead
Check ($read -eq $away) "the monitor itself now reads $away (probe saw $read)"
$shown = Texts $win
Check ($shown -contains ($away.ToString())) "the app shows the written value ($away)"
[void](ShotGood $h (Join-Path $ShotDir '02-lowered.png'))

Write-Host "`n== drive it back to the original $orig =="
SetSlider $slider $orig
Start-Sleep -Milliseconds 400
Start-Sleep -Milliseconds 1600
$read2 = ProbeRead
Check ($read2 -eq $orig) "the monitor is restored to $orig (probe saw $read2)"
$shown2 = Texts $win
Check ($shown2 -contains ($orig.ToString())) "the app shows the restored value ($orig)"
[void](ShotGood $h (Join-Path $ShotDir '03-restored.png'))

Write-Host "`n== the gear still opens the settings window =="
$gear = @((FindType $win ([System.Windows.Automation.ControlType]::Button)) | Where-Object { $_.Current.Name -like 'Settings for *' })[0]
if ($null -ne $gear) {
    ([System.Windows.Automation.InvokePattern]$gear.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 700
    $sh = [IntPtr]::Zero
    for ($i = 0; $i -lt 20 -and $sh -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $sh = HwndByTitle $p.Id 'Monitorn settings'
    }
    Check ($sh -ne [IntPtr]::Zero) "invoking the gear opens 'Monitorn settings'"
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

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
