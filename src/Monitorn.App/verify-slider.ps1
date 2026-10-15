# EXP13 verification helper - put a real mouse click on a row's slider track and prove where the knob
# lands, which is the whole claim.
#
# The witness is deliberately outside the app's own numbers:
#   * the rendered pixels: the accent thumb's centre, measured from a screenshot, must sit on the
#     point that was clicked (the picture:  ---[]---x---  becomes  ---[x]---  );
#   * the value the row reports, against the value the RENDERED geometry implies - the mapping from a
#     value to an x is learned from the thumb's own positions at two known values, not assumed from
#     the template;
#   * the display itself: the read-only EXP02 probe over DDC/CI, or a WMI query for an internal panel;
#   * the regression a jump could cause: a press on the thumb must still drag it;
#   * and what a screen reader sees: the slider's name, the steps, and a keyboard-focus cue that is
#     visible on the pixels (WPF's OS focus visual is not, for anyone on the mouse - that is a
#     finding of this experiment).
# A press is only ever sent when WindowFromPoint says this app's own window would receive it, and the
# cursor is put back where the run found it.
#
#   .\verify-slider.ps1                      # every row, shots into %TEMP%\monitorn-exp13
#   .\verify-slider.ps1 -ShotDir .\out       # keep the evidence somewhere else
#   .\verify-slider.ps1 -SkipInternal        # leave the internal panel's row undriven
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitorn.App.exe'),
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe'),
    [string]$AppArgs = '',
    [switch]$SkipInternal,
    [string]$ShotDir = (Join-Path $env:TEMP 'monitorn-exp13'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitorn-exp13\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }

# A temp settings file, no schedule (this pass drives sliders, it does not test the night), and the
# light palette pinned: the pixel witness looks for the accent and the unfilled track, so the run
# must not depend on whatever theme this machine happens to wear.
$RealSettings = Join-Path $env:APPDATA 'Monitorn\settings.ini'
$realExisted = Test-Path $RealSettings
$realHash = if ($realExisted) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }
$AppArgs = "--no-schedule --no-tray --theme light --settings-file `"$SettingsFile`" $AppArgs"

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

# Named per-experiment on purpose: a PowerShell session keeps types once added, so two verifier
# scripts sharing a class name collide in the second one's Add-Type.
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class WinExp13 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
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
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint data, IntPtr extra);
    public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004;
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
# device it came from, so "which display" is answerable rather than "some display read 57".
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

# The internal panel's brightness, read the way the app writes it but from this process: an outside
# witness for a display the DDC probe cannot see.
function WmiRead([string]$instance) {
    try {
        $v = Get-CimInstance -Namespace root/wmi -ClassName WmiMonitorBrightness -ErrorAction Stop |
            Where-Object { $_.InstanceName -eq $instance } | Select-Object -First 1
        if ($null -eq $v) { return -1 }
        return [int]$v.CurrentBrightness
    } catch { return -1 }
}

function ProbeValues { return @(ProbeMonitors | ForEach-Object { $_.Value }) }

# --- the pixels ---

# The light palette's accent (Theme.cs): the thumb and the filled track. The unfilled track is a
# ControlEdge grey, far enough away that a generous tolerance cannot confuse the two.
function IsAccent($c) {
    return ([Math]::Abs($c.R - 0) -lt 60) -and ([Math]::Abs($c.G - 0x78) -lt 60) -and ([Math]::Abs($c.B - 0xD7) -lt 60)
}

function ShotRect([int]$x, [int]$y, [int]$w, [int]$hh) {
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($x, $y, 0, 0, $bmp.Size)
    $g.Dispose()
    return $bmp
}

# Where the thumb is, in screen pixels: the track's filled bar is 4 DIP tall, so the only accent in
# the slider's own band, eight pixels off the centre line, is the thumb. Its centre is the position
# the eye reads as "where the knob is".
function ThumbCentre($slider) {
    $r = $slider.Current.BoundingRectangle
    $x0 = [int]$r.Left - 6; $y0 = [int]$r.Top - 6
    $w = [int]$r.Width + 12; $hh = [int]$r.Height + 12
    if ($w -le 0 -or $hh -le 12) { return -1 }
    $bmp = ShotRect $x0 $y0 $w $hh
    $cy = [int]($hh / 2)
    $first = -1; $last = -1
    for ($x = 0; $x -lt $w; $x++) {
        if ((IsAccent $bmp.GetPixel($x, $cy - 8)) -or (IsAccent $bmp.GetPixel($x, $cy + 8))) {
            if ($first -lt 0) { $first = $x }
            $last = $x
        }
    }
    $bmp.Dispose()
    if ($first -lt 0) { return -1 }
    return $x0 + ($first + $last) / 2
}

# How much of the accent is drawn in a region. "Bluish" rather than an exact match, because a
# one-pixel border is anti-aliased against whatever it sits on.
function AccentPixels([int]$x, [int]$y, [int]$w, [int]$hh) {
    $bmp = ShotRect $x $y $w $hh
    $n = 0
    for ($yy = 0; $yy -lt $hh; $yy++) {
        for ($xx = 0; $xx -lt $w; $xx++) {
            $c = $bmp.GetPixel($xx, $yy)
            if ($c.B -gt ($c.R + 40) -and $c.B -gt 140) { $n++ }
        }
    }
    $bmp.Dispose()
    return $n
}

function SaveShot([IntPtr]$h, [string]$path) {
    $r = New-Object WinExp13+RECT
    [void][WinExp13]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# --- the mouse ---

function CursorNow {
    $p = New-Object WinExp13+POINT
    [void][WinExp13]::GetCursorPos([ref]$p)
    return $p
}

function WouldReceive([int]$x, [int]$y) {
    $p = New-Object WinExp13+POINT
    $p.X = $x; $p.Y = $y
    return [WinExp13]::WindowFromPoint($p)
}

function ClickAt([int]$x, [int]$y) {
    [void][WinExp13]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 120
    [WinExp13]::mouse_event([WinExp13]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 60
    [WinExp13]::mouse_event([WinExp13]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
}

# Press the thumb and walk the cursor sideways with the button down: the drag the jump must not break.
function DragBy($slider, [int]$dx) {
    $cx = [int](ThumbCentre $slider)
    $r = $slider.Current.BoundingRectangle
    $cy = [int]($r.Top + $r.Height / 2)
    [void][WinExp13]::SetCursorPos($cx, $cy)
    Start-Sleep -Milliseconds 120
    [WinExp13]::mouse_event([WinExp13]::LEFTDOWN, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    for ($k = 1; $k -le 6; $k++) {
        [void][WinExp13]::SetCursorPos($cx + [int]($dx / 6) * $k, $cy)
        Start-Sleep -Milliseconds 60
    }
    Start-Sleep -Milliseconds 300
    [WinExp13]::mouse_event([WinExp13]::LEFTUP, 0, 0, 0, [IntPtr]::Zero)
    return $cx
}

# --- UI Automation helpers ---

function FindType($root, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition -ArgumentList $AE::ControlTypeProperty, $controlType
    return @($root.FindAll($TS::Descendants, $cond))
}

function ValueOf($slider) {
    return [int]([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).Current.Value
}

function NumericOf($slider, [string]$which) {
    $rv = [System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    if ($which -eq 'max') { return [int]$rv.Current.Maximum }
    if ($which -eq 'min') { return [int]$rv.Current.Minimum }
    if ($which -eq 'small') { return $rv.Current.SmallChange }
    return $rv.Current.LargeChange
}

function SetSlider($slider, [double]$v) {
    ([System.Windows.Automation.RangeValuePattern]$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)).SetValue($v)
}

# One entry per row, keyed by the display id the row carries (EXP10).
function Rows($win) {
    $rows = New-Object System.Collections.ArrayList
    foreach ($s in @(FindType $win ([System.Windows.Automation.ControlType]::Slider))) {
        $id = $s.Current.AutomationId
        $kind = 'unknown'
        $instance = ''
        if ($id -like 'DDC:*') { $kind = 'ddc' }
        elseif ($id -like 'WMI:*') { $kind = 'wmi'; $instance = $id.Substring(4) }
        [void]$rows.Add([pscustomobject]@{
                Slider = $s; Id = $id; Kind = $kind; Instance = $instance
                Value = (ValueOf $s); Name = $s.Current.Name
            })
    }
    return @($rows)
}

function Texts($root) {
    return @((FindType $root ([System.Windows.Automation.ControlType]::Text)) | ForEach-Object { $_.Current.Name })
}

function CloseAll([int]$targetPid) {
    $found = New-Object System.Collections.ArrayList
    $cb = [WinExp13+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp13]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp13]::IsWindowVisible($h)) {
            $r = New-Object WinExp13+RECT
            [void][WinExp13]::GetWindowRect($h, [ref]$r)
            if (($r.Right - $r.Left) -gt 0 -and ($r.Bottom - $r.Top) -gt 0) { [void]$found.Add($h) }
        }
        return $true
    }
    [void][WinExp13]::EnumWindows($cb, [IntPtr]::Zero)
    foreach ($w in $found) { [void][WinExp13]::PostMessage($w, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
    return $found.Count
}

# ============================================================================

$cursor0 = CursorNow
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
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [WinExp13]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }

# In front, so the pointer is over our window and not over whatever the user has there. A press is
# only sent where WindowFromPoint agrees (checked again right before each click).
[void][WinExp13]::BringWindowToTop($h)
for ($i = 0; $i -lt 20; $i++) {
    [void][WinExp13]::SetForegroundWindow($h)
    if ([WinExp13]::GetForegroundWindow() -eq $h) { break }
    Start-Sleep -Milliseconds 200
}
Start-Sleep -Milliseconds 1600   # let it paint and settle

$winCond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
$win = $AE::RootElement.FindFirst($TS::Children, $winCond)
if ($null -eq $win) { $p.Kill(); throw "UI Automation could not find the window" }

$rows = @(Rows $win)
$ids = ($rows | ForEach-Object { $_.Id }) -join ' | '
Check ($rows.Count -eq $targets) "one row per active display: expected $targets slider(s) (got $($rows.Count))"
Check ($rows.Count -ge 1) "the surface has a row to drive"
Check (@($rows | Where-Object { $_.Kind -eq 'unknown' }).Count -eq 0) "every row carries its display id (got '$ids')"

$texts = Texts $win
$ddcRows = @($rows | Where-Object { $_.Kind -eq 'ddc' })
$wmiRows = @($rows | Where-Object { $_.Kind -eq 'wmi' })
Check ($ddcRows.Count -eq $probe0.Count) "each display the probe can read has its own row ($($ddcRows.Count) row(s) vs $($probe0.Count) readable display(s))"

# --- what a screen reader gets ---

Write-Host "`n== the row as a screen reader hears it =="
foreach ($row in $rows) {
    $name = $row.Name
    Check (-not [string]::IsNullOrWhiteSpace($name)) "row '$($row.Id)' has an accessible name (got '$name')"
    $display = $name -replace '\s+brightness$', ''
    Check ($display -ne $name -and $display.Length -gt 0) "and it names the display, not just the control ('$display')"
    Check ($texts -contains $display) "and that name is the display the row shows on screen ('$display' is a text in the window)"
    Check ((NumericOf $row.Slider 'small') -eq 1) "the Arrow step is 1 (got $(NumericOf $row.Slider 'small'))"
    Check ((NumericOf $row.Slider 'large') -eq 10) "the Page step is 10 (got $(NumericOf $row.Slider 'large'))"
    Check ($row.Slider.Current.IsKeyboardFocusable) "the slider can take the keyboard"
}

# --- the focus cue, on the pixels. The row's own bottom edge is the only thing drawn in the band
# under the slider, so the accent appearing there is the row saying "the keyboard is on my slider". ---

Write-Host "`n== the keyboard's focus is visible =="
$gears = @(FindType $win ([System.Windows.Automation.ControlType]::Button) | Where-Object { $_.Current.Name -like 'Settings for *' })
$fi = 0
foreach ($row in $rows) {
    $r = $row.Slider.Current.BoundingRectangle
    $fx = [int]$r.Left; $fy = [int]$r.Bottom + 2; $fw = [int]$r.Width; $fh = 14
    # A key apart from the slider: the window's own SetFocus does not move the keyboard off a
    # control, so the baseline and the "cue goes" step focus the gear beside it instead.
    if ($gears.Count -gt 0) { [void]$gears[$fi % $gears.Count].SetFocus() }
    Start-Sleep -Milliseconds 600
    $atRest = AccentPixels $fx $fy $fw $fh
    Check ($atRest -eq 0) "nothing of the accent is drawn on row '$($row.Id)' at rest ($atRest px under it)"
    if ($fi -eq 0) { SaveShot $h (Join-Path $ShotDir 'focus-off.png') }
    $row.Slider.SetFocus()
    Start-Sleep -Milliseconds 700
    $lit = AccentPixels $fx $fy $fw $fh
    Check ($row.Slider.Current.HasKeyboardFocus) "row '$($row.Id)' takes the keyboard focus"
    Check ($lit -ge 50) "and its row's edge takes the accent while it has it ($lit px under it)"
    if ($fi -eq 0) { SaveShot $h (Join-Path $ShotDir 'focus-on.png') }
    if ($gears.Count -gt 0) { [void]$gears[$fi % $gears.Count].SetFocus() }
    Start-Sleep -Milliseconds 700
    $back = AccentPixels $fx $fy $fw $fh
    Check ($back -eq 0) "and the cue goes when the keyboard leaves it ($back px under it)"
    $fi++
}

[void](SaveShot $h (Join-Path $ShotDir '01-before.png'))

# --- the click: where the knob lands, what the value is, and what the display does ---

$driven = 0
for ($i = 0; $i -lt $rows.Count; $i++) {
    $row = $rows[$i]
    $before = $row.Value
    $min = NumericOf $row.Slider 'min'
    $max = NumericOf $row.Slider 'max'

    if ($row.Kind -eq 'unknown') {
        Write-Host "`n== row '$($row.Id)': no witness for this kind; not driven =="
        Check $false "row '$($row.Id)' is a display kind this verifier can witness"
        continue
    }
    if ($row.Kind -eq 'wmi' -and $SkipInternal) {
        Write-Host "`n== row '$($row.Id)': internal panel, skipped (-SkipInternal) =="
        continue
    }
    Write-Host "`n== row '$($row.Id)' at $before ($min..$max) =="

    # What the thumb's own positions say: x(v) = a + b v, learned from two values. Nothing about the
    # template is assumed - if the track moved or its margins changed, this follows it.
    $lo = $min + 5; $hi = $max - 5
    SetSlider $row.Slider $lo
    Start-Sleep -Milliseconds 350
    $xLo = ThumbCentre $row.Slider
    SetSlider $row.Slider $hi
    Start-Sleep -Milliseconds 350
    $xHi = ThumbCentre $row.Slider
    Check ($xLo -gt 0 -and $xHi -gt 0) "the thumb can be found in the pixels at both ends ($lo -> x=$xLo, $hi -> x=$xHi)"
    if ($xLo -lt 0 -or $xHi -lt 0) { $p.Kill(); throw "the thumb is not where this verifier can measure it" }
    Check ($xHi -gt $xLo) "and it travels right as the value rises ($($xHi - $xLo) px over $($hi - $lo) levels)"
    $b = ($xHi - $xLo) / ($hi - $lo)
    $a = $xLo - $b * $lo

    # A target far from where the thumb is, and far from where the old LargeChange nudge would have
    # put it - that is what makes this line able to tell the two behaviours apart.
    $target = -1
    foreach ($f in @(0.85, 0.2, 0.65, 0.35)) {
        $t = $min + [Math]::Round(($max - $min) * $f)
        if ([Math]::Abs($t - $before) -ge 15 -and [Math]::Abs($t - ($before + 10)) -ge 5 -and [Math]::Abs($t - ($before - 10)) -ge 5) {
            $target = $t; break
        }
    }
    if ($target -lt 0) {
        Write-Host "  (no target far enough from $before to tell a jump from a nudge; skipping the click)"
        Check $false "row '$($row.Id)' has room to tell a jump from a nudge"
        continue
    }
    $clickX = [int][Math]::Round($a + $b * $target)
    $clickY = [int]($row.Slider.Current.BoundingRectangle.Top + $row.Slider.Current.BoundingRectangle.Height / 2)
    Write-Host ("  now=$before target=$target -> click at ({0},{1})" -f $clickX, $clickY)
    Check ($clickX -ge $row.Slider.Current.BoundingRectangle.Left -and $clickX -le $row.Slider.Current.BoundingRectangle.Right) `
        "the click point is on the slider ($clickX in $([int]$row.Slider.Current.BoundingRectangle.Left)..$([int]$row.Slider.Current.BoundingRectangle.Right))"

    # Back to the start, and let the display settle there: the reference the click is measured against.
    SetSlider $row.Slider $before
    Start-Sleep -Milliseconds 1800
    if ($row.Kind -eq 'ddc') {
        $atStart = @(ProbeValues)
        Check ((@($atStart | Where-Object { $_ -eq $before }).Count -ge 1)) "the display is at $before before the click (probe: $($atStart -join ','))"
    } else {
        Check ((WmiRead $row.Instance) -eq $before) "the panel is at $before before the click"
    }

    $otherBefore = @($rows | Where-Object { $_.Id -ne $row.Id } | ForEach-Object { "$($_.Id)=$($_.Value)" })

    # The guard: the press must go to our own window, or it is not sent at all.
    $receiver = WouldReceive $clickX $clickY
    if ($receiver -ne $h) {
        Write-Host "`n!! a press at ($clickX,$clickY) would go to window $receiver, not this app ($h)."
        Write-Host "   Is another window covering the surface? Not sending a click into a stranger's window."
        Check $false "the app's own window is the one under the click point"
        break
    }
    Check ($receiver -eq $h) "the app's own window would receive the press at ($clickX,$clickY)"

    ClickAt $clickX $clickY
    Start-Sleep -Milliseconds 600
    $landed = ThumbCentre $row.Slider
    $value = ValueOf $row.Slider
    [void](SaveShot $h (Join-Path $ShotDir ("02-row{0}-clicked.png" -f ($i + 1))))
    Write-Host ("  after the click: value=$value, thumb centre x=$landed (clicked at $clickX)")
    Check ([Math]::Abs($landed - $clickX) -le 3) "the knob is drawn on the point that was clicked ($landed vs $clickX, $([Math]::Abs($landed - $clickX)) px)"
    Check ([Math]::Abs($value - $target) -le 1) "and the row reads the level that point means ($value vs $target)"
    Check ($value -ne ($before + 10) -and $value -ne ($before - 10)) "and it is not one LargeChange from where the thumb was ($value, not $($before + 10) or $($before - 10))"

    Start-Sleep -Milliseconds 1800     # debounce (250 ms) + the write latency
    if ($row.Kind -eq 'ddc') {
        $after = @(ProbeValues)
        Write-Host ("  the probe saw: " + ($atStart -join ',') + " -> " + ($after -join ','))
        Check ((@($after | Where-Object { $_ -eq $value }).Count -ge 1)) "the display itself is at $value after one click (probe: $($after -join ','))"
    } else {
        $wmiAfter = WmiRead $row.Instance
        Check ($wmiAfter -eq $value) "the internal panel itself is at $value after one click (independent WMI read saw $wmiAfter)"
    }
    $otherAfter = @($rows | Where-Object { $_.Id -ne $row.Id } | ForEach-Object { "$($_.Id)=$($_.Value)" })
    Check ((($otherAfter -join ',') -eq ($otherBefore -join ','))) "no other row moved (was $($otherBefore -join ','), now $($otherAfter -join ','))"

    # --- and the drag the jump must not have broken: press the thumb and pull it sideways ---
    Write-Host "`n  the thumb still drags:"
    SetSlider $row.Slider $before
    Start-Sleep -Milliseconds 500
    $dragFrom = ValueOf $row.Slider
    $started = DragBy $row.Slider 48
    Start-Sleep -Milliseconds 800
    $dragTo = ValueOf $row.Slider
    $moved = ThumbCentre $row.Slider
    [void](SaveShot $h (Join-Path $ShotDir ("03-row{0}-dragged.png" -f ($i + 1))))
    Check ($dragTo -gt $dragFrom) "pressing the thumb and dragging it right raises the value ($dragFrom -> $dragTo)"
    Check ($moved -gt ($started + 20)) "and the thumb came along ($started -> $moved px)"

    # --- restore, and prove the display went back ---
    SetSlider $row.Slider $before
    Start-Sleep -Milliseconds 1800
    if ($row.Kind -eq 'ddc') {
        $back = @(ProbeValues)
        Check ((@($back | Where-Object { $_ -eq $before }).Count -ge 1)) "the display is back at $before (probe: $($back -join ','))"
    } else {
        Check ((WmiRead $row.Instance) -eq $before) "the panel is back at $before"
    }
    Check ((ValueOf $row.Slider) -eq $before) "and the row shows the restored value ($before)"
    $driven++
}
Write-Host ""
Write-Host ("rows driven: {0} of {1}" -f $driven, $rows.Count)

# --- teardown: the cursor back where it was found, the window closed, the settings untouched ---

$cursorBack = CursorNow
[void][WinExp13]::SetCursorPos($cursor0.X, $cursor0.Y)
Write-Host ("cursor put back: ({0},{1}) -> ({2},{3})" -f $cursorBack.X, $cursorBack.Y, $cursor0.X, $cursor0.Y)

Write-Host "`n== close =="
$n = CloseAll $p.Id
Write-Host "asked $n window(s) to close"
if (-not $p.WaitForExit(5000)) { $p.Kill(); Write-Host "!! did not exit (killed)"; $script:failed++ }
else { Write-Host ("closed cleanly: exitCode=" + $p.ExitCode) }

Write-Host "`n== every display left where it was =="
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
