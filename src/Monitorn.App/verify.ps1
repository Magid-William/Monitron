# EXP03 verification helper - launch the control surface, drive it, look at it.
#
# Builds nothing; it runs the already-built Monitorn.App.exe, waits for its window, takes a
# screenshot (optionally after sending keystrokes), then closes the window and reports the
# exit code. Used to verify the surface without a test framework.
#
#   .\verify.ps1                                             # default 3 rows, screenshot only
#   .\verify.ps1 -AppArgs '--rows 5 --width 520'             # layout pass: taller list, wider
#   .\verify.ps1 -Keys '{TAB}{RIGHT 10}'                     # drive the 1st slider, then shoot
#   .\verify.ps1 -Keys '{TAB}{TAB}{ENTER}' -Out ...\gear.png # focus + press the 1st gear
#
# SendKeys note: a literal space works, but there is no {SPACE} keyword; {ENTER} also presses a
# focused WPF button. With -Keys, a "...before.png" shot is written next to -Out for comparison.
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitorn.App.exe'),
    [string]$AppArgs = '',
    [string]$Keys = '',
    [string]$Out = (Join-Path $env:TEMP 'monitorn-exp03.png')
)

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
"@

function Shot([IntPtr]$h, [string]$path) {
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($h, [ref]$r)
    $bmp = New-Object System.Drawing.Bitmap ($r.Right - $r.Left), ($r.Bottom - $r.Top)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return ("{0}x{1} at {2},{3}" -f ($r.Right - $r.Left), ($r.Bottom - $r.Top), $r.Left, $r.Top)
}

# A frame caught mid-paint is near-uniform. Sample it and count pixels that differ from the
# top-left one; the real surface has plenty (rows, text, sliders), a blank one almost none.
function Content([IntPtr]$h) {
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($h, [ref]$r)
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

# Shoot until the frame actually has content on it (masking WPF's lagging first frame).
function ShotGood([IntPtr]$h, [string]$path) {
    $where = ''
    $best = -1
    for ($try = 1; $try -le 4; $try++) {
        if ($try -gt 1) { Start-Sleep -Milliseconds 600 }
        $where = Shot $h $path
        $c = Content $h
        if ($c -gt 150) { return @($where, $c, $try) }
        Write-Host ("   (frame $try looks blank: content=$c - retrying)")
        $best = $c
    }
    Write-Host "!! giving up: last frame still looks blank"
    return @($where, $best, 4)
}

$p = Start-Process -FilePath $Exe -ArgumentList $AppArgs -PassThru
$h = [IntPtr]::Zero
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 250
    $p.Refresh()
    if ($p.HasExited) { throw "process exited early with code $($p.ExitCode)" }
    if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [Win]::IsWindowVisible($p.MainWindowHandle)) { $h = $p.MainWindowHandle; break }
}
if ($h -eq [IntPtr]::Zero) { $p.Kill(); throw "no visible main window appeared" }

# the window must be in front: for the screenshot to show it, and for -Keys to land on it
[void][Win]::BringWindowToTop($h)
for ($i = 0; $i -lt 20; $i++) {
    [void][Win]::SetForegroundWindow($h)
    if ([Win]::GetForegroundWindow() -eq $h) { break }
    Start-Sleep -Milliseconds 200
}
if ([Win]::GetForegroundWindow() -ne $h) { Write-Host "!! window is not foreground - the shot may catch whatever is on top of it" }
Start-Sleep -Milliseconds 1600   # let it paint and settle (WPF's first frame can lag the fade-in)

if ($Keys) {
    $before = [System.IO.Path]::GetFileNameWithoutExtension($Out) + '.before.png'
    $before = Join-Path (Split-Path -Parent $Out) $before
    $b = ShotGood $h $before
    Write-Host ("before: " + $b[0] + " content=" + $b[1] + " -> $before")
}

if ($Keys) {
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds 700
    $p.Refresh()
    Write-Host ("after keys '$Keys': hasExited=" + $p.HasExited + " sameMainWindow=" + ($p.MainWindowHandle -eq $h))
}

$a = ShotGood $h $Out
Write-Host ("captured " + $a[0] + " content=" + $a[1] + " -> $Out")

# clean exit: ask the window to close and wait for the process to end by itself
$closed = $p.CloseMainWindow()
if (-not $p.WaitForExit(5000)) { $p.Kill(); Write-Host "!! did not exit on close request (killed)" }
else { Write-Host ("closed cleanly: CloseMainWindow=" + $closed + " exitCode=" + $p.ExitCode) }
