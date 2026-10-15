# EXP07 verification helper - edit the settings window, restart, and assert the settings came back.
#
# Builds nothing. It runs the already-built Monitron.App.exe with a settings file under %TEMP%,
# drives the window through UI Automation (Value/Toggle/SelectionItem patterns - the way any
# automation or assistive tool drives it), closes it, and asserts the file on disk. Then it starts
# the app again on that file and asserts the window reports the edited values. A corrupt file must
# start the app on the defaults instead.
#
# The real per-user file (%APPDATA%\Monitron\settings.ini) is never passed to the app here, and the
# script asserts it did not change during the run.
#
#   .\verify-persistence.ps1                 # shots into %TEMP%\monitron-exp07
#   .\verify-persistence.ps1 -ShotDir .\out  # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$ShotDir = (Join-Path $env:TEMP 'monitron-exp07')
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
public static class WinExp07 {
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
    [void][WinExp07]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object WinExp07+RECT
    [void][WinExp07]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ("{0}x{1}" -f ($r.Right - $r.Left), ($r.Bottom - $r.Top))
}

function CloseAll([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [WinExp07+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp07]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp07]::IsWindowVisible($h)) {
            $r = New-Object WinExp07+RECT
            [void][WinExp07]::GetWindowRect($h, [ref]$r)
            if (($r.Right - $r.Left) -gt 0 -and ($r.Bottom - $r.Top) -gt 0) { [void]$found.Add($h) }
        }
        return $true
    }
    [void][WinExp07]::EnumWindows($cb, [IntPtr]::Zero)
    foreach ($h in $found) { [void][WinExp07]::PostMessage($h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }  # WM_CLOSE
    return $found.Count
}

# Find a top-level window by title, the Win32 way: the settings window opened from a gear is owned
# by the surface and does NOT appear as a UIA desktop child beside it (EXP04's finding).
function HwndByTitle([int]$targetPid, [string]$title) {
    $hits = New-Object System.Collections.ArrayList
    $cb = [WinExp07+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp07]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp07]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp07]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

# --- UI Automation helpers ---

function FindOne($root, $property, $value) {
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $property, $value
    return $root.FindFirst($TS::Descendants, $cond)
}

function FindType($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $AE::ControlTypeProperty, $controlType
    return @($root.FindAll($TS::Descendants, $cond))
}

function ByName([string]$name) { return FindOne $script:win $AE::NameProperty $name }

# The numeric fields, in reading order: strength first, then the two clock rows.
function Edits() {
    $all = FindType $script:win ([System.Windows.Automation.ControlType]::Edit)
    return @($all | Sort-Object { $_.Current.BoundingRectangle.Top }, { $_.Current.BoundingRectangle.Left })
}

function EditValues() {
    return @((Edits) | ForEach-Object { ([System.Windows.Automation.ValuePattern]$_.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).Current.Value })
}

function ToggleState($el) {
    return ([System.Windows.Automation.TogglePattern]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState
}

function SetValue($el, [string]$value) {
    ([System.Windows.Automation.ValuePattern]$el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)).SetValue($value)
    Start-Sleep -Milliseconds 300
}

function Toggle($el) {
    ([System.Windows.Automation.TogglePattern]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 300
}

# Launch the app with the given argument line and return its process and window, or throw.
function OpenApp([string]$argString) {
    $p = Start-Process -FilePath $Exe -ArgumentList $argString -PassThru
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        if ($p.HasExited) { throw "process exited early with code $($p.ExitCode) ($argString)" }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp07]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
    }
    if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared ($argString)" }
    Start-Sleep -Milliseconds 1200   # let WPF paint and settle

    $winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
    $w = $AE::RootElement.FindFirst($TS::Children, $winCond)
    if ($null -eq $w) { $p.Kill(); throw "UI Automation could not find the window ($argString)" }
    return [pscustomobject]@{ Process = $p; Handle = $h; Window = $w }
}

function CloseApp($app) {
    $n = CloseAll $app.Process.Id
    if (-not $app.Process.WaitForExit(5000)) { $app.Process.Kill(); Write-Host "  !! did not exit (killed)"; $script:failed++ }
    else { Write-Host ("  closed cleanly: exitCode=" + $app.Process.ExitCode) }
    return $n
}

# --- the file under test, and the real one we must not touch ---

$stateDir = Join-Path (Join-Path $env:TEMP 'monitron-exp07') 'state'
if (Test-Path $stateDir) { Remove-Item $stateDir -Recurse -Force }
[void](New-Item -ItemType Directory -Path $stateDir)
$settingsFile = Join-Path $stateDir 'settings.ini'

$realPath = Join-Path $env:APPDATA 'Monitron\settings.ini'
$realBefore = if (Test-Path $realPath) { (Get-FileHash $realPath).Hash } else { '<absent>' }
Write-Host "temp settings file: $settingsFile"
Write-Host "real settings file (must stay untouched): $realPath -> $realBefore"

$argsFor = { param($file) "--settings --settings-file `"$file`"" }

# --- 1. a first run: edit, and see it land on disk ---

Write-Host "`n== first run: the file does not exist, defaults are shown =="
$app = OpenApp (& $argsFor $settingsFile)
$script:win = $app.Window
Check ((Title $app.Handle) -eq 'Monitron settings') "window is titled 'Monitron settings' (got '$((Title $app.Handle))')"
Check (((EditValues) -join ',') -eq '38,5,00,9,00') "a missing file shows the built-in defaults (38, 5:00, 9:00) - got $((EditValues) -join ',')"
Check ((ToggleState (ByName 'gradual fade')) -eq [System.Windows.Automation.ToggleState]::On) "'gradual fade' is on by default"
Check (-not (Test-Path $settingsFile)) "and nothing was written just by opening it"
[void](Shot $app.Handle (Join-Path $ShotDir '01-first-run-defaults.png'))

Write-Host "`n== drive it: strength 55, gradual fade off =="
SetValue (Edits)[0] '55'
Check ((EditValues)[0] -eq '55') "the field reads 55"
Toggle (ByName 'gradual fade')
Check ((ToggleState (ByName 'gradual fade')) -eq [System.Windows.Automation.ToggleState]::Off) "'gradual fade' is off now"
Check (Test-Path $settingsFile) "the edit created the settings file"
[void](Shot $app.Handle (Join-Path $ShotDir '02-edited.png'))

$h1 = CloseApp $app
Write-Host "asked $h1 window(s) to close"

Write-Host "`n== what landed on disk =="
$text = Get-Content -Raw $settingsFile
Write-Host "----- $settingsFile -----"
Write-Host $text.TrimEnd()
Write-Host "-------------------------"
Check ($text -match '(?m)^strength=55\s*$') "the file has strength=55"
Check ($text -match '(?m)^gradualFade=false\s*$') "the file has gradualFade=false"
Check ($text -match '(?m)^version=1\s*$') "the file carries version=1"
Check (-not (Test-Path ($settingsFile + '.tmp'))) "no .tmp was left behind"

$hashAfterEdit = (Get-FileHash $settingsFile).Hash

# --- 2. restart: the edited values come back, and nothing is rewritten ---

Write-Host "`n== restart on that file: the edit comes back =="
$app = OpenApp (& $argsFor $settingsFile)
$script:win = $app.Window
Check ((EditValues)[0] -eq '55') "strength is 55 again (got $((EditValues)[0]))"
Check ((ToggleState (ByName 'gradual fade')) -eq [System.Windows.Automation.ToggleState]::Off) "'gradual fade' is still off"
Check ((ToggleState (ByName 'On')) -eq [System.Windows.Automation.ToggleState]::On) "the schedule is still on"
[void](Shot $app.Handle (Join-Path $ShotDir '03-restarted.png'))
$h2 = CloseApp $app
Write-Host "asked $h2 window(s) to close"
Check ((Get-FileHash $settingsFile).Hash -eq $hashAfterEdit) "opening and closing without an edit did not rewrite the file"

# --- 3. a corrupt file starts on the defaults, not a crash ---

Write-Host "`n== a corrupt file =="
Set-Content -Path $settingsFile -Value "this is not a settings file`nstrength=999`n" -NoNewline
$app = OpenApp (& $argsFor $settingsFile)
$script:win = $app.Window
Check ((EditValues)[0] -eq '38') "an unreadable strength falls back to the default 38 (got $((EditValues)[0]))"
Check ((ToggleState (ByName 'gradual fade')) -eq [System.Windows.Automation.ToggleState]::On) "the other fields are at their defaults"
Check (-not $app.Process.HasExited) "the app started and stayed up on a corrupt file"
[void](Shot $app.Handle (Join-Path $ShotDir '04-corrupt-defaults.png'))
$h3 = CloseApp $app
Write-Host "asked $h3 window(s) to close"

# --- 4. the product path: the surface's own gear writes the same file ---

Write-Host "`n== the surface's gear =="
$surfaceFile = Join-Path $stateDir 'surface.ini'
if (Test-Path $surfaceFile) { Remove-Item $surfaceFile -Force }
# Make the schedule inert without switching it off: turn-on == turn-off touches no monitor, and
# (unlike --no-schedule) it leaves the feature on, so the gear's body stays drivable.
$inert = "--on 00:00 --off 00:00 --no-tray --settings-file `"$surfaceFile`""
$app = OpenApp $inert
$script:win = $app.Window
Check ($app.Window.Current.Name -eq 'Monitron') "the surface is up (got '$($app.Window.Current.Name)')"

function Gear($root) {
    return @(FindType $root ([System.Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like 'Settings for *' })
}

function OpenGear($app, $gearList) {
    if ($gearList.Count -lt 1) { return [IntPtr]::Zero }
    ([System.Windows.Automation.InvokePattern]$gearList[0].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    $sh = [IntPtr]::Zero
    for ($i = 0; $i -lt 25 -and $sh -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 200
        $sh = HwndByTitle $app.Process.Id 'Monitron settings'
    }
    return $sh
}

$gear = Gear $app.Window
Check ($gear.Count -ge 1) "a per-display gear is present"
$sh = OpenGear $app $gear
Check ($sh -ne [IntPtr]::Zero) "the gear opened 'Monitron settings'"
if ($sh -ne [IntPtr]::Zero) {
    $script:win = $AE::FromHandle($sh)
    SetValue (Edits)[0] '42'
    Check ((EditValues)[0] -eq '42') "the gear's window took 42"
    Check (Test-Path $surfaceFile) "and the edit wrote the settings file"
}
$h4 = CloseApp $app
Write-Host "asked $h4 window(s) to close"
if (Test-Path $surfaceFile) {
    Check ((Get-Content -Raw $surfaceFile) -match '(?m)^strength=42\s*$') "the surface's edit landed as strength=42"
}

Write-Host "`n== a fresh surface on that file shows the edit =="
$app = OpenApp $inert
$script:win = $app.Window
$sh = OpenGear $app (Gear $app.Window)
Check ($sh -ne [IntPtr]::Zero) "the gear opened it again on a fresh launch"
if ($sh -ne [IntPtr]::Zero) {
    $script:win = $AE::FromHandle($sh)
    Check ((EditValues)[0] -eq '42') "and it shows the edited 42 (got $((EditValues)[0]))"
}
[void](Shot $app.Handle (Join-Path $ShotDir '05-surface-restarted.png'))
$h5 = CloseApp $app
Write-Host "asked $h5 window(s) to close"

# --- 5. the real file was never touched ---

Write-Host "`n== the user's own file =="
$realAfter = if (Test-Path $realPath) { (Get-FileHash $realPath).Hash } else { '<absent>' }
Check ($realBefore -eq $realAfter) "the real settings file is unchanged by this run ($realAfter)"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
