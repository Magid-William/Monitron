# EXP04 verification helper - drive the night-light settings window and assert what it exposes.
#
# Builds nothing. It runs the already-built Monitron.App.exe, then drives the window through
# UI Automation: controls are found by name, invoked through their own patterns (Toggle,
# Select, Invoke, Value), and the state they report back is asserted. Screenshots of the key
# states are written next to each other for the visual record. Exit code is non-zero if any
# assertion fails.
#
# Driving by tab order was tried first and dropped: the count shifts whenever a control is
# added, and it cannot tell "the click landed" from "the window lost focus". UIA asserts.
#
#   .\verify-settings.ps1                 # full scenario, shots into %TEMP%\monitron-exp04
#   .\verify-settings.ps1 -ShotDir .\out  # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$AppArgs = '--settings',
    [string]$ShotDir = (Join-Path $env:TEMP 'monitron-exp04'),
    # EXP07: the app now persists settings. Driving this window edits the model, which would write
    # the user's real %APPDATA%\Monitron\settings.ini - so the verifier runs on a temp file it
    # deletes first, which also makes the defaults this script asserts on deterministic.
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitron-exp04\state\settings.ini'),
    [switch]$FromGear
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }

$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }
$launchArgs = "$AppArgs --no-tray --settings-file `"$SettingsFile`""

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
// Named per-experiment on purpose: a PowerShell session keeps types once added, so two
// verifier scripts sharing a class name collide in the second one's Add-Type.
public static class WinExp04 {
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

function Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][WinExp04]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object WinExp04+RECT
    [void][WinExp04]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ("{0}x{1}" -f ($r.Right - $r.Left), ($r.Bottom - $r.Top))
}

# A frame caught mid-paint is near-uniform; sample it and count pixels differing from the
# top-left one. The real window has plenty (text, boxes, controls), a blank one almost none.
function Content([IntPtr]$h) {
    $r = New-Object WinExp04+RECT
    [void][WinExp04]::GetWindowRect($h, [ref]$r)
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

# Find a top-level window by title, the Win32 way. UIA's desktop children are not reliable for
# this: a WPF window opened with an Owner does not show up beside its owner (see the run below).
function HwndByTitle([int]$targetPid, [string]$title) {
    $hits = New-Object System.Collections.ArrayList
    $cb = [WinExp04+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp04]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp04]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp04]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

# Close every visible, non-empty top-level window the process owns.
function CloseAll([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [WinExp04+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp04]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp04]::IsWindowVisible($h)) {
            $r = New-Object WinExp04+RECT
            [void][WinExp04]::GetWindowRect($h, [ref]$r)
            if (($r.Right - $r.Left) -gt 0 -and ($r.Bottom - $r.Top) -gt 0) {
                [void]$found.Add([pscustomobject]@{ Handle = $h; Title = (Title $h) })
            }
        }
        return $true
    }
    [void][WinExp04]::EnumWindows($cb, [IntPtr]::Zero)
    foreach ($w in $found) {
        Write-Host ("   closing [{0}] '{1}'" -f $w.Handle, $w.Title)
        [void][WinExp04]::PostMessage($w.Handle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)  # WM_CLOSE
    }
    return $found.Count
}

# --- UI Automation helpers ---

function Enabled($el) { return [bool]$el.GetCurrentPropertyValue($AE::IsEnabledProperty) }
function Name($el) { return $el.Current.Name }

function FindOne($root, $property, $value) {
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $property, $value
    return $root.FindFirst($TS::Descendants, $cond)
}

function FindType($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $AE::ControlTypeProperty, $controlType
    return @($root.FindAll($TS::Descendants, $cond))
}

# Only the buttons this window draws: the two AM/PM toggles. The title bar's minimize/maximize/
# close belong to the OS chrome and are not descendants of the WPF element tree (confirmed by the
# surface dump this script prints).
function Buttons() {
    return @((FindType $win ([System.Windows.Automation.ControlType]::Button)) | Where-Object {
        $_.Current.Name -in @('PM', 'AM') })
}

function CountNamed([string]$name) {
    return @((FindType $win ([System.Windows.Automation.ControlType]::Button)) | Where-Object {
        $_.Current.Name -eq $name }).Count
}

function ByName([string]$name) { return FindOne $win $AE::NameProperty $name }

# The numeric fields, in reading order: the Default brightness (outside the section), then strength,
# then the two clock rows.
function Edits() {
    $all = FindType $win ([System.Windows.Automation.ControlType]::Edit)
    return @($all | Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left })
}

function EditValues() { return @((Edits) | ForEach-Object { ([System.Windows.Automation.ValuePattern]$_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value }) }

function SelectRadio([string]$name) {
    $el = ByName $name
    if ($null -eq $el) { throw "radio '$name' not found" }
    ([System.Windows.Automation.SelectionItemPattern]$el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select()
    Start-Sleep -Milliseconds 350
}

function Toggle([string]$name) {
    $el = ByName $name
    if ($null -eq $el) { throw "toggle '$name' not found" }
    ([System.Windows.Automation.TogglePattern]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 350
}

function Invoke([System.Windows.Automation.AutomationElement]$el) {
    ([System.Windows.Automation.InvokePattern]$el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 350
}

function ToggleState($name) {
    $el = ByName $name
    return ([System.Windows.Automation.TogglePattern]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState
}

# --- launch ---

$p = Start-Process -FilePath $Exe -ArgumentList $launchArgs -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
    if ($p.HasExited) { throw "process exited early with code $($p.ExitCode)" }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp04]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }

[void][WinExp04]::BringWindowToTop($h)
$foregroundOk = $false
for ($i = 0; $i -lt 20; $i++) {
    [void][WinExp04]::SetForegroundWindow($h)
    if ([WinExp04]::GetForegroundWindow() -eq $h) { $foregroundOk = $true; break }
    # Windows' foreground lock refuses the call while another process holds it; a tapped Alt
    # releases the lock, after which the retry can take the foreground.
    if ($i -eq 5) { [System.Windows.Forms.SendKeys]::SendWait('%') }
    Start-Sleep -Milliseconds 200
}
if (-not $foregroundOk) {
    Write-Host "!! this window could not take the foreground (something else on the desktop has it)."
    Write-Host "   Screenshots may catch another window; the key-driven checks are skipped rather than reported as failures."
}
Start-Sleep -Milliseconds 1600   # let it paint and settle (WPF's first frame can lag the fade-in)

$winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win = $AE::RootElement.FindFirst($TS::Children, $winCond)
if ($null -eq $win) { $p.Kill(); throw "UI Automation could not find the window" }

# -FromGear: launch the control surface instead and prove the gear is what opens this window,
# by keyboard (what a user does) and by automation.
if ($FromGear) {
    if ($foregroundOk) {
        Write-Host "`n== gear -> settings, by keyboard =="
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}{TAB}{ENTER}')   # slider, gear, press
        $sh = [IntPtr]::Zero
        for ($i = 0; $i -lt 20 -and $sh -eq [IntPtr]::Zero; $i++) {
            Start-Sleep -Milliseconds 200
            $sh = HwndByTitle $p.Id 'Monitron settings'
        }
        Check ($sh -ne [IntPtr]::Zero) "tabbing to the gear and pressing it opens 'Monitron settings'"
        if ($sh -ne [IntPtr]::Zero) {
            $names = @((FindType ($AE::FromHandle($sh)) ([System.Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name })
            Check ($names -contains 'Schedule night light') "what it opened is the night-light schedule"
            [void][WinExp04]::PostMessage($sh, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
            Start-Sleep -Milliseconds 700
            Check ((HwndByTitle $p.Id 'Monitron settings') -eq [IntPtr]::Zero) "and the surface survives its closing"
        }
    }
    else {
        Write-Host "`n== gear -> settings, by keyboard: skipped =="
        Write-Host "  (the desktop kept the foreground, so keystrokes would not land on our window)"
    }

    Write-Host "`n== gear -> settings, by automation =="
    $desktopChildren = @($AE::RootElement.FindAll($TS::Children, $winCond))
    Write-Host ("  (uia sees {0} desktop child(ren) for our pid: {1} - the owned window is not among them, hence the HWND lookups)" -f $desktopChildren.Count, (@($desktopChildren | ForEach-Object { $_.Current.Name }) -join ' | '))
    $gear = ByName 'Settings for AOC Q27G3G3R3'
    Check ($null -ne $gear) "the gear is a named button (an icon-only control needs an accessible name)"
    if ($null -ne $gear) {
        Check (Enabled $gear) "the gear is enabled"
        Invoke $gear
        $sh = [IntPtr]::Zero
        for ($i = 0; $i -lt 20 -and $sh -eq [IntPtr]::Zero; $i++) {
            Start-Sleep -Milliseconds 200
            $sh = HwndByTitle $p.Id 'Monitron settings'
        }
        Check ($sh -ne [IntPtr]::Zero) "invoking the gear opens it again after it was closed"
        if ($sh -ne [IntPtr]::Zero) {
            [void][WinExp04]::BringWindowToTop($sh)
            [void][WinExp04]::SetForegroundWindow($sh)
            Start-Sleep -Milliseconds 900
            [void](ShotGood $sh (Join-Path $ShotDir '06-from-gear.png'))
        }
    }
    $n = CloseAll $p.Id
    Write-Host "asked $n window(s) to close"
    if (-not $p.WaitForExit(5000)) { $p.Kill(); Write-Host "!! did not exit (killed)"; $script:failed++ }
    else { Write-Host ("closed cleanly: exitCode=" + $p.ExitCode) }
    Write-Host ""
    Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
    if ($script:failed -gt 0) { exit 1 }
    return
}

Write-Host "`n== surface =="
Check ((Name $win) -eq 'Monitron settings') "window is titled 'Monitron settings' (got '$((Name $win))')"
foreach ($el in FindType $win ([System.Windows.Automation.ControlType]::Text)) {
    if ($el.Current.Name) { Write-Host ("  text    " + $el.Current.Name) }
}
foreach ($el in FindType $win ([System.Windows.Automation.ControlType]::CheckBox)) {
    Write-Host ("  check   '{0}' state={1}" -f (Name $el), (ToggleState (Name $el)))
}
foreach ($el in FindType $win ([System.Windows.Automation.ControlType]::RadioButton)) {
    $sel = ([System.Windows.Automation.SelectionItemPattern]$el.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected
    Write-Host ("  radio   '{0}' selected={1}" -f (Name $el), $sel)
}
foreach ($el in FindType $win ([System.Windows.Automation.ControlType]::Edit)) {
    Write-Host ("  edit    value='{0}' enabled={1}" -f ([System.Windows.Automation.ValuePattern]$el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value, (Enabled $el))
}
foreach ($el in FindType $win ([System.Windows.Automation.ControlType]::Button)) {
    Write-Host ("  button  '{0}'" -f (Name $el))
}

Write-Host "`n== the sketch, by default =="
$edits = Edits
Check ($edits.Count -eq 6) "six numeric fields: default, strength, hour+minute for each of the two times (got $($edits.Count))"
Check (((EditValues) -join ',') -eq '80,38,5,00,9,00') "values are default 80, strength 38, turn on 5:00, turn off 9:00 (got $((EditValues) -join ','))"
Check ((Name (ByName 'gradual fade')) -eq 'gradual fade') "'gradual fade' checkbox is there"
Check ((ToggleState 'gradual fade') -eq [System.Windows.Automation.ToggleState]::On) "'gradual fade' starts on"
Check ((ToggleState 'On') -eq [System.Windows.Automation.ToggleState]::On) "the schedule starts on"
Check ((Name (ByName 'Sunset to sunrise (12:00 AM - 12:00 AM)')) -ne '') "'Sunset to sunrise (12:00 AM - 12:00 AM)' is offered verbatim"
Check ((CountNamed 'PM') -eq 1 -and (CountNamed 'AM') -eq 1) "one AM/PM toggle reads 'PM' (turn on) and one reads 'AM' (turn off)"
Check (Enabled (ByName 'Sunset to sunrise (12:00 AM - 12:00 AM)')) "all of it is live while the schedule is on"
[void](ShotGood $h (Join-Path $ShotDir '01-default.png'))

Write-Host "`n== option 1: sunset to sunrise =="
SelectRadio 'Sunset to sunrise (12:00 AM - 12:00 AM)'
$sunsetSelected = ([System.Windows.Automation.SelectionItemPattern](ByName 'Sunset to sunrise (12:00 AM - 12:00 AM)').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected
Check $sunsetSelected "'Sunset to sunrise' is the selected option"
Check (((EditValues) -join ',') -eq '80,38,5,00,9,00') "the kept times are still displayed, not cleared"
$timeEdits = @((Edits) | Select-Object -Skip 2)
Check (@($timeEdits | Where-Object { Enabled $_ }).Count -eq 0) "all four time fields are disabled under sunset (coupling)"
Check (Enabled $edits[0]) "the Default field stays live under sunset (it is outside the section)"
Check (Enabled $edits[1]) "strength stays live under sunset"
Check (Enabled (ByName 'gradual fade')) "'gradual fade' stays live under sunset"
[void](ShotGood $h (Join-Path $ShotDir '02-sunset.png'))

Write-Host "`n== option 2: set hours =="
SelectRadio 'Set hours'
$hoursSelected = ([System.Windows.Automation.SelectionItemPattern](ByName 'Set hours').GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected
Check $hoursSelected "'Set hours' is the selected option"
Check (((EditValues) -join ',') -eq '80,38,5,00,9,00') "the times survived the round trip"
Check (@((Edits) | Select-Object -Skip 2 | Where-Object { Enabled $_ }).Count -eq 4) "all four time fields are live again"

Write-Host "`n== drive strength =="
# Driven through the Value pattern, not keystrokes: this works whatever window the desktop has
# put in front, and it is what any automation or assistive tool would do.
$strength = $edits[1]
$fade = ByName 'gradual fade'
$strengthPattern = [System.Windows.Automation.ValuePattern]$strength.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$strengthPattern.SetValue('55')
Start-Sleep -Milliseconds 300
$fade.SetFocus()                        # leaving the field canonicalises it
Start-Sleep -Milliseconds 400
Check ((EditValues)[1] -eq '55') "set strength 55 and it stuck (got $((EditValues)[1]))"
$strengthPattern.SetValue('999')        # out of range on purpose
Start-Sleep -Milliseconds 300
$fade.SetFocus()
Start-Sleep -Milliseconds 400
Check ((EditValues)[1] -eq '55') "999 was refused and strength reverted to 55 (got $((EditValues)[1]))"
[void](ShotGood $h (Join-Path $ShotDir '03-strength-55.png'))

Write-Host "`n== drive the Default brightness (outside the section) =="
$defaultPattern = [System.Windows.Automation.ValuePattern]$edits[0].GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
$defaultPattern.SetValue('70')
Start-Sleep -Milliseconds 300
$fade.SetFocus()
Start-Sleep -Milliseconds 400
Check ((EditValues)[0] -eq '70') "set Default 70 and it stuck (got $((EditValues)[0]))"
$defaultPattern.SetValue('999')         # out of range on purpose
Start-Sleep -Milliseconds 300
$fade.SetFocus()
Start-Sleep -Milliseconds 400
Check ((EditValues)[0] -eq '70') "999 was refused and Default reverted to 70 (got $((EditValues)[0]))"

Write-Host "`n== drive the AM/PM toggle =="
$onMeridiem = ByName 'PM'          # the turn-on toggle, currently PM
Invoke $onMeridiem
Check ((CountNamed 'PM') -eq 0 -and (CountNamed 'AM') -eq 2) "turn on flipped from PM to AM (PM toggles now: $(CountNamed 'PM'), AM: $(CountNamed 'AM'))"
Check (((EditValues) -join ',') -eq '70,55,5,00,9,00') "only the meridiem changed; the numbers are untouched"
Invoke $onMeridiem                 # the same element, now reading AM
Check ((CountNamed 'PM') -eq 1 -and (CountNamed 'AM') -eq 1) "and back to PM"

Write-Host "`n== the On switch gates the body =="
Toggle 'On'
Check ((ToggleState 'On') -eq [System.Windows.Automation.ToggleState]::Off) "the schedule is off"
$body = @()
$body += @((Edits) | Select-Object -Skip 1)   # the Default field is outside the section
$body += FindType $win ([System.Windows.Automation.ControlType]::RadioButton)
$body += @(ByName 'gradual fade')
$body += Buttons
$stillLive = @($body | Where-Object { Enabled $_ })
Check ($stillLive.Count -eq 0) "every control in the section is disabled (still live: $($stillLive.Count) of $($body.Count))"
Check (Enabled (ByName 'On')) "the On switch itself stays usable"
Check ((EditValues)[0] -eq '70') "and the Default field (outside the section) stays live"
[void](ShotGood $h (Join-Path $ShotDir '04-off.png'))
Toggle 'On'
Check ((ToggleState 'On') -eq [System.Windows.Automation.ToggleState]::On) "and back on"
Check (@((Edits) | Where-Object { Enabled $_ }).Count -eq 6) "everything is live again"

Write-Host "`n== gradual fade =="
Toggle 'gradual fade'
Check ((ToggleState 'gradual fade') -eq [System.Windows.Automation.ToggleState]::Off) "'gradual fade' toggles off"
Check ((EditValues)[1] -eq '55') "nothing else moved with it"
Toggle 'gradual fade'
Check ((ToggleState 'gradual fade') -eq [System.Windows.Automation.ToggleState]::On) "and back on"

Write-Host "`n== close =="
[void](ShotGood $h (Join-Path $ShotDir '05-final.png'))
$n = CloseAll $p.Id
Write-Host "asked $n window(s) to close"
if (-not $p.WaitForExit(5000)) { $p.Kill(); Write-Host "!! did not exit (killed)"; $script:failed++ }
else { Write-Host ("closed cleanly: exitCode=" + $p.ExitCode) }

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
