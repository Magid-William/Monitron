# EXP11 verification helper - prove the tray shell, from outside the app and through its own menu.
#
# Builds nothing. It runs the already-built Monitron.App.exe and then:
#   * witnesses the icon in the shell's own notification area (UI Automation finds the taskbar button
#     named "Monitron"), not the app's word for it;
#   * drives the real context menu through UI Automation - the Show/Hide line, the checkable
#     "Run on Windows startup" (TogglePattern) and Quit (InvokePattern) - with the menu opened by the
#     --tray-menu seam, the same way --now scripts the clock;
#   * witnesses the Run entry outside the app, with Get-ItemProperty on HKCU\...\Run, under a
#     throwaway value name so the user's real autostart is never touched;
#   * witnesses Show/Hide with Win32 IsWindowVisible and the process still running;
#   * witnesses Quit's restore with the read-only EXP02 probe.
#
#   .\verify-tray.ps1                  # full scenario, shots into %TEMP%\monitron-exp11
#   .\verify-tray.ps1 -ShotDir .\out   # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [string]$ShotDir = (Join-Path $env:TEMP 'monitron-exp11'),
    [string]$StartupValue = 'MonitronExp11',
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitron-exp11\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }

$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$ExePath = [System.IO.Path]::GetFullPath($Exe)
$ExpectedCommand = '"' + $ExePath + '" --hidden'

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
// Named per-experiment on purpose: a PowerShell session keeps types once added, so two verifier
// scripts sharing a class name collide in the second one's Add-Type.
public static class WinExp11 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct NID { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string win);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    // The shell's own answer for where it put the icon belonging to a given window - an independent
    // witness that the icon really is registered in the notification area.
    [DllImport("shell32.dll", SetLastError=true)] public static extern int Shell_NotifyIconGetRect(ref NID id, out RECT r);
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

function Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][WinExp11]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object WinExp11+RECT
    if (-not [WinExp11]::GetWindowRect($h, [ref]$r)) { return }
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function ShotRect($el, [string]$path) {
    if ($null -eq $el) { return }
    $r = $el.Current.BoundingRectangle
    if ($r.Width -le 0 -or $r.Height -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.Left, [int]$r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# --- Win32 reads and the notify-area witness ---

function HwndByTitle([int]$targetPid, [string]$title) {
    $hits = New-Object System.Collections.ArrayList
    $cb = [WinExp11+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp11]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp11]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp11]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

# The shell's own answer for the icon belonging to a window: Shell_NotifyIconGetRect. The app is not
# asked - the shell is. Fails (nonzero) when no icon is registered for that window.
function NotifyIconRect([IntPtr]$hwnd) {
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    $id = New-Object WinExp11+NID
    $id.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($id)
    $id.hWnd = $hwnd
    $id.uID = 1
    $r = New-Object WinExp11+RECT
    try { if ([WinExp11]::Shell_NotifyIconGetRect([ref]$id, [ref]$r) -ne 0) { return $null } }
    catch { return $null }
    if (($r.Right - $r.Left) -le 0 -or ($r.Bottom - $r.Top) -le 0) { return $null }
    return [pscustomobject]@{ L = $r.Left; T = $r.Top; R = $r.Right; B = $r.Bottom }
}

# Best effort: the icon can also be found as a taskbar button by name if the user pinned it out of
# the hidden-icons overflow. Not a claim by itself - the shell's rectangle above is.
function TrayButtonNamed {
    foreach ($cls in @('Shell_TrayWnd', 'Shell_SecondaryTrayWnd')) {
        $bar = [WinExp11]::FindWindow($cls, $null)
        if ($bar -eq [IntPtr]::Zero) { continue }
        try {
            $root = $AE::FromHandle($bar)
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Monitron')
            $hit = $root.FindFirst($TS::Descendants, $cond)
            if ($null -ne $hit) { return $hit }
        } catch { }
    }
    return $null
}

function WaitFor($scriptblock, [int]$tries = 20, [int]$milliseconds = 400) {
    for ($i = 0; $i -lt $tries; $i++) {
        $value = & $scriptblock
        if ($null -ne $value) { return $value }
        Start-Sleep -Milliseconds $milliseconds
    }
    return $null
}

# A window's size in pixels, from Win32 (used to prove the menu does not resize when a line is checked).
function WindowSize([IntPtr]$h) {
    $r = New-Object WinExp11+RECT
    if (-not [WinExp11]::GetWindowRect($h, [ref]$r)) { return $null }
    return [pscustomobject]@{ W = $r.Right - $r.Left; H = $r.Bottom - $r.Top }
}

# --- the real context menu, opened by the app's seam and read/driven through UI Automation ---

function MenuRoot([int]$targetPid) {
    $cb = [WinExp11+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp11]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -ne $targetPid) { return $true }
        try {
            $el = $AE::FromHandle($h)
            if ($null -eq $el) { return $true }
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
            if ($null -ne $el.FindFirst($TS::Descendants, $cond)) { $script:menuEl = $el; return $false }
        } catch { }
        return $true
    }
    $script:menuEl = $null
    [void][WinExp11]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:menuEl
}

function MenuItems($menuEl) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    return @($menuEl.FindAll($TS::Descendants, $cond))
}

# The menu popup's own HWND, so its real pixels can be measured (the menu is its own top-level
# window, not part of the surface's element tree).
function MenuHwnd([int]$targetPid) {
    $cb = [WinExp11+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp11]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -ne $targetPid) { return $true }
        if ((Title $h) -eq 'Monitron') { return $true }
        try {
            $el = $AE::FromHandle($h)
            if ($null -eq $el) { return $true }
            $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
            if ($null -ne $el.FindFirst($TS::Descendants, $cond)) { $script:menuHwnd = $h; return $false }
        } catch { }
        return $true
    }
    $script:menuHwnd = [IntPtr]::Zero
    [void][WinExp11]::EnumWindows($cb, [IntPtr]::Zero)
    if ($script:menuHwnd -eq [IntPtr]::Zero) { return $null }
    return $script:menuHwnd
}

# Average pixel luminance of a window: UI Automation cannot see color, so a theme claim needs this
# (the same reason EXP08's theme report samples pixels).
function MenuLuminance([IntPtr]$hwnd) {
    $r = New-Object WinExp11+RECT
    if (-not [WinExp11]::GetWindowRect($hwnd, [ref]$r)) { return $null }
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { return $null }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size); $g.Dispose()
    $sum = 0.0; $n = 0
    for ($y = 0; $y -lt $ht; $y += 2) {
        for ($x = 0; $x -lt $w; $x += 2) {
            $px = $bmp.GetPixel($x, $y)
            $sum += (0.2126 * $px.R + 0.7152 * $px.G + 0.0722 * $px.B) / 255.0
            $n++
        }
    }
    $bmp.Dispose()
    if ($n -eq 0) { return $null }
    return ($sum / $n)
}

function ItemById($menuEl, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $menuEl.FindFirst($TS::Descendants, $cond)
}

function ToggleState($el) {
    return ([System.Windows.Automation.TogglePattern]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Current.ToggleState
}

function Toggle($el) {
    ([System.Windows.Automation.TogglePattern]$el.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)).Toggle()
    Start-Sleep -Milliseconds 500
}

function Invoke($el) {
    ([System.Windows.Automation.InvokePattern]$el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Start-Sleep -Milliseconds 700
}

# --- the Run key, read outside the app ---

function RunValue([string]$name) {
    $key = Get-ItemProperty -Path $RunKey -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $key) { return $null }
    return $key.$name
}

function RemoveRunValue([string]$name) {
    if ($null -ne (Get-ItemProperty -Path $RunKey -Name $name -ErrorAction SilentlyContinue)) {
        Remove-ItemProperty -Path $RunKey -Name $name -Force -ErrorAction SilentlyContinue
    }
}

# --- the independent brightness witness ---

function ProbeRead {
    if (-not (Test-Path $Probe)) { return $null }
    try {
        $out = & $Probe 2>&1 | Out-String
        $m = [regex]::Match($out, 'brightness cur=(\d+)')
        if ($m.Success) { return [int]$m.Groups[1].Value }
    } catch { }
    return $null
}

# --- launching ---

$realSettings = Join-Path $env:APPDATA 'Monitron\settings.ini'
$realBefore = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }

RemoveRunValue $StartupValue   # a leftover from a killed run must not read as this run's doing

function Launch([string]$label, [string]$extra, [int]$quitAfter = 25) {
    $argline = "--settings-file `"$SettingsFile`" --startup-value $StartupValue --quit-after $quitAfter --tray-report `"$ReportDir`" $extra"
    Write-Host "`n== $label =="
    Note "app: Monitron.App.exe $argline"
    return Start-Process -FilePath $Exe -ArgumentList $argline -PassThru
}

function WaitSurface([int]$targetPid, [int]$tries = 40) {
    return WaitFor { $h = HwndByTitle $targetPid 'Monitron'; if ($h -ne [IntPtr]::Zero) { $h } else { $null } } $tries 250
}

function WaitMenu([int]$targetPid, [int]$tries = 40) {
    return WaitFor { MenuRoot $targetPid } $tries 250
}

# ============================================================================

$ReportDir = Join-Path $ShotDir 'report'
if (Test-Path $ReportDir) { Remove-Item $ReportDir -Recurse -Force }

try {

# ---------------------------------------------------------------------------
Write-Host "`n== the icon, the menu, and the Run entry (scenario A) =="

$p = Launch 'a visible surface, the menu scripted open' '--no-schedule --tray-menu' 30
$surface = WaitSurface $p.Id
Check ($null -ne $surface) "the surface window is up"
if ($null -eq $surface) { $p.Kill(); throw "no surface window appeared" }

$iconRect = WaitFor { NotifyIconRect $surface } 30 400
Check ($null -ne $iconRect) "the shell reports a notification-area icon for the surface window - it is really there"
if ($null -ne $iconRect) {
    Note ("the shell puts the icon at ({0},{1})-({2},{3}) px" -f $iconRect.L, $iconRect.T, $iconRect.R, $iconRect.B)
    Check ($iconRect.R -gt $iconRect.L -and $iconRect.B -gt $iconRect.T) "and it has a real rectangle on screen"
}
$named = TrayButtonNamed
if ($null -ne $named) { Note "the notification area also shows a taskbar button named '$(($named).Current.Name)'" }
else { Note "the icon sits in the shell's hidden-icons overflow, so it has no taskbar button until that is opened" }

$menu = WaitMenu $p.Id
Check ($null -ne $menu) "the tray menu opened"
if ($null -eq $menu) { $p.Kill(); throw "the tray menu never opened" }
[void](ShotRect $menu (Join-Path $ShotDir '01-menu.png'))

$items = MenuItems $menu
Check ($items.Count -eq 3) "the menu has exactly three lines (got $($items.Count))"

$visItem = ItemById $menu 'TrayShowHide'
$startItem = ItemById $menu 'TrayStartup'
$quitItem = ItemById $menu 'TrayQuit'
Check ($null -ne $visItem) "the Show/Hide line is there"
Check ($null -ne $startItem) "the run-on-startup line is there"
Check ($null -ne $quitItem) "the Quit line is there"

Check ($null -ne $visItem -and $visItem.Current.Name -eq 'Hide') "with the surface up, the line offers Hide (got '$($visItem.Current.Name)')"
Check ($null -ne $startItem -and $startItem.Current.Name -eq 'Run on Windows startup') "the checkable line reads 'Run on Windows startup'"
Check ($null -ne $quitItem -and $quitItem.Current.Name -eq 'Quit') "and Quit reads 'Quit'"

Check ((RunValue $StartupValue) -eq $null) "the Run entry starts absent (outside witness)"
Check ((ToggleState $startItem) -eq [System.Windows.Automation.ToggleState]::Off) "and the checkable line reads unchecked"

$menuHwnd = MenuHwnd $p.Id
$sizeBefore = WindowSize $menuHwnd
Note ("the menu is {0} px wide unchecked" -f $(if ($null -ne $sizeBefore) { $sizeBefore.W } else { '?' }))

Write-Host "`n== toggling Run on Windows startup writes the Run key =="
Toggle $startItem
Check ((ToggleState $startItem) -eq [System.Windows.Automation.ToggleState]::On) "the line is checked now"
$written = RunValue $StartupValue
Note "HKCU Run '$StartupValue' = '$written'"
Check ($written -eq $ExpectedCommand) "the registry holds exactly `"$ExePath`" --hidden"
Check ((RunValue $StartupValue) -notlike '*--tray-menu*') "and it carries none of this run's seam arguments"

# A check mark that is Collapsed rather than Hidden takes no space when off and pushes the text
# right when on - so the menu would grow (or the text jump) on the first check. Assert it does not.
$sizeAfter = WindowSize $menuHwnd
Note ("and {0} px wide once a line is checked" -f $(if ($null -ne $sizeAfter) { $sizeAfter.W } else { '?' }))
Check ($null -ne $sizeBefore -and $null -ne $sizeAfter -and $sizeBefore.W -eq $sizeAfter.W) "checking a line does not resize the menu - the check column is always reserved"

Toggle $startItem
Check ((ToggleState $startItem) -eq [System.Windows.Automation.ToggleState]::Off) "toggling back unchecks it"
Check ((RunValue $StartupValue) -eq $null) "and removes the Run entry (outside witness)"

Write-Host "`n== the app's own report agrees with the witnesses =="
$reportPath = Join-Path $ReportDir 'tray.txt'
Check (Test-Path $reportPath) "the app wrote its tray report"
if (Test-Path $reportPath) {
    $report = @{}
    foreach ($line in Get-Content $reportPath) {
        $i = $line.IndexOf('=')
        if ($i -gt 0) { $report[$line.Substring(0, $i)] = $line.Substring($i + 1) }
    }
    Check ($report['visible'] -eq 'true') "it agrees the surface is up"
    Check ($report['startupEnabled'] -eq 'false') "it agrees the Run entry is off"
    Check ($report['iconRect'] -ne 'none' -and $report['iconRect'] -ne '') "it can see its own icon's rectangle ($($report['iconRect']))"
    if ($null -ne $iconRect) {
        $own = $report['iconRect'].Split(',')
        $same = ([Math]::Abs([int]$own[0] - $iconRect.L) -lt 40) -and ([Math]::Abs([int]$own[1] - $iconRect.T) -lt 40)
        Check $same "and its rectangle agrees with the shell's own answer"
    }
}

Write-Host "`n== Quit exits cleanly and takes the icon with it =="
Invoke $quitItem
Check ($p.WaitForExit(6000)) "invoking Quit ended the process"
Check ($p.HasExited -and $p.ExitCode -eq 0) "with exit code 0 (got $($p.ExitCode))"
Check ($null -eq (NotifyIconRect $surface)) "and the shell no longer has an icon for that window"
RemoveRunValue $StartupValue

# ---------------------------------------------------------------------------
Write-Host "`n== a tray-only start offers Show (scenario B) =="

$p = Launch 'hidden, the menu scripted open' '--no-schedule --hidden --tray-menu' 20
Start-Sleep -Milliseconds 1500
Check (-not $p.HasExited) "the app is running with no window shown"
Check ((HwndByTitle $p.Id 'Monitron') -eq [IntPtr]::Zero) "the surface is hidden at start"

$menu = WaitMenu $p.Id
Check ($null -ne $menu) "the tray menu opened anyway"
if ($null -ne $menu) {
    $visItem = ItemById $menu 'TrayShowHide'
    Check ($null -ne $visItem -and $visItem.Current.Name -eq 'Show') "and the line offers Show (got '$($visItem.Current.Name)')"

    Invoke $visItem
    $shown = WaitSurface $p.Id
    Check ($null -ne $shown) "invoking it shows the surface"
    [void](Shot $shown (Join-Path $ShotDir '02-shown.png'))
}
$p.WaitForExit(20000) | Out-Null
Check ($p.ExitCode -eq 0) "the run ended cleanly (exit $($p.ExitCode))"

# ---------------------------------------------------------------------------
Write-Host "`n== Hide keeps the app alive with no window (scenario C) =="

$p = Launch 'visible, the menu scripted open' '--no-schedule --tray-menu' 12
$surface = WaitSurface $p.Id
Check ($null -ne $surface) "the surface is up"

$menu = WaitMenu $p.Id
Check ($null -ne $menu) "the menu opened"
if ($null -ne $menu) {
    $visItem = ItemById $menu 'TrayShowHide'
    Check ($visItem.Current.Name -eq 'Hide') "the line offers Hide"
    Invoke $visItem
    $hidden = WaitFor { if ((HwndByTitle $p.Id 'Monitron') -eq [IntPtr]::Zero) { 'hidden' } else { $null } } 20 300
    Check ($hidden -eq 'hidden') "invoking it hides the surface"
    Check (-not $p.HasExited) "and the app is still running - it lives in the tray now"
}
$p.WaitForExit(15000) | Out-Null
Check ($p.ExitCode -eq 0) "the run ended cleanly (exit $($p.ExitCode))"

# ---------------------------------------------------------------------------
Write-Host "`n== closing the window hides to the tray (scenario D) =="

$p = Launch 'visible, then its X' '--no-schedule' 12
$surface = WaitSurface $p.Id
Check ($null -ne $surface) "the surface is up"
[void][WinExp11]::PostMessage($surface, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)   # WM_CLOSE
$hidden = WaitFor { if ((HwndByTitle $p.Id 'Monitron') -eq [IntPtr]::Zero) { 'hidden' } else { $null } } 20 300
Check ($hidden -eq 'hidden') "the close hid the window"
Check (-not $p.HasExited) "and did not end the app"
$p.WaitForExit(15000) | Out-Null
Check ($p.ExitCode -eq 0) "the run ended cleanly (exit $($p.ExitCode))"

# ---------------------------------------------------------------------------
Write-Host "`n== Quit gives the displays back (scenario E) =="

$day = ProbeRead
if ($null -eq $day) {
    Note "no read-only probe available, so Quit's restore was not witnessed"
} else {
    $strength = [Math]::Max(0, $day - 10)
    Note "the AOC reads $day; a scripted night at strength $strength will dim it"
    $today = (Get-Date).ToString('yyyy-MM-dd')
    # Turn on 30 simulated seconds in (1 real second at rate 30); the dim then glides at 10 points a
    # simulated second, which is quick in real time at this rate.
    $night = "--now `"$today 16:04:30`" --rate 30 --on 16:05 --off 16:30 --strength $strength --daylight $day --tray-menu"
    $p = Launch 'a scripted night, then Quit from the menu' $night 40
    $dimmed = WaitFor { $v = ProbeRead; if ($null -ne $v -and $v -lt $day) { $v } else { $null } } 40 500
    Check ($null -ne $dimmed) "the AOC was dimmed mid-fade (probe read $dimmed, day $day)"

    $menu = WaitMenu $p.Id
    Check ($null -ne $menu) "the menu is open to Quit from"
    if ($null -ne $menu) {
        $quitItem = ItemById $menu 'TrayQuit'
        Invoke $quitItem
    }
    Check ($p.WaitForExit(8000)) "Quit ended the process"
    Check ($p.ExitCode -eq 0) "with exit code 0 (got $($p.ExitCode))"

    $after = WaitFor { $v = ProbeRead; if ($null -ne $v -and $v -ge $day) { $v } else { $null } } 20 500
    Check ($after -eq $day) "and the AOC is back at its day value ($day -> $after)"
}

# ---------------------------------------------------------------------------
Write-Host "`n== the menu follows the app theme (scenario F) =="

# Pinned with --theme, so this does not depend on (or change) the user's Windows theme. The menu is
# its own popup window, so its real pixels are measured the way EXP08 measured the windows.
$p = Launch 'pinned dark, the menu scripted open' '--no-schedule --theme dark --tray-menu' 12
$menuHwnd = WaitFor { MenuHwnd $p.Id } 40 250
Check ($null -ne $menuHwnd) "the dark menu opened"
$darkLum = if ($null -ne $menuHwnd) { MenuLuminance $menuHwnd } else { $null }
if ($null -ne $darkLum) { Note ("the dark menu's average luminance is {0:0.00}" -f $darkLum) }
Check ($null -ne $darkLum -and $darkLum -lt 0.35) "it is painted dark (luminance < 0.35)"
if ($null -ne $menuHwnd) { [void](Shot $menuHwnd (Join-Path $ShotDir '03-menu-dark.png')) }
$p.WaitForExit(15000) | Out-Null

$p = Launch 'pinned light, the menu scripted open' '--no-schedule --theme light --tray-menu' 12
$menuHwnd = WaitFor { MenuHwnd $p.Id } 40 250
Check ($null -ne $menuHwnd) "the light menu opened"
$lightLum = if ($null -ne $menuHwnd) { MenuLuminance $menuHwnd } else { $null }
if ($null -ne $lightLum) { Note ("the light menu's average luminance is {0:0.00}" -f $lightLum) }
Check ($null -ne $lightLum -and $lightLum -gt 0.6) "it is painted light (luminance > 0.6)"
Check ($null -ne $darkLum -and $null -ne $lightLum -and ($lightLum - $darkLum) -gt 0.3) "and the two are plainly different, so the palette is what painted them"
if ($null -ne $menuHwnd) { [void](Shot $menuHwnd (Join-Path $ShotDir '04-menu-light.png')) }
$p.WaitForExit(15000) | Out-Null

}
finally {
    RemoveRunValue $StartupValue
    if ($null -ne $p) { if (-not $p.HasExited) { $p.Kill() } }
}

# ---------------------------------------------------------------------------
Write-Host "`n== nothing of the user's was touched =="

Check ((RunValue $StartupValue) -eq $null) "the throwaway Run value is gone"
$realAfter = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
Check ($realAfter -eq $realBefore) "the user's real %APPDATA%\Monitron\settings.ini is untouched"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("shots: $ShotDir")
if ($script:failed -gt 0) { exit 1 }
