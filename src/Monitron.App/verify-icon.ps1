# EXP12 verification helper - prove the app carries the designed logo, from outside the app.
#
# Builds nothing. It runs the already-built Monitron.App.exe and the committed Monitron.ico and then:
#   * reads the .ico's own directory (nine frames, small ones DIB, 128/256 PNG) and draws the 16 px
#     frame to prove it is the mark and not a blank square;
#   * asks the SHELL for the exe's icon (PrivateExtractIcons at 16, 32, 48 and 128 px) and compares
#     the pixels with those frames - the witness that csc really embedded every size, PNG frame and
#     all - and against another program's icon, to prove it is not a stock one;
#   * asks WIN32 for the running window's own icon (WM_GETICON, then GCLP_HICON) and compares it with
#     the frame at the size Windows draws - the witness that the process really sets the designed one;
#   * witnesses the notification area with the shell (Shell_NotifyIconGetRect, EXP11's witness) and
#     reads the app's own --icon-report, which must agree with the file;
#   * checks the user's real settings file and Run entry are untouched, and nothing is left running.
#
#   .\verify-icon.ps1                  # full scenario, shots into %TEMP%\monitron-exp12
#   .\verify-icon.ps1 -ShotDir .\out   # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$Ico = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.ico'),
    [string]$ShotDir = (Join-Path $env:TEMP 'monitron-exp12'),
    [string]$SettingsFile = (Join-Path $env:TEMP 'monitron-exp12\state\settings.ini')
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $ShotDir)) { [void](New-Item -ItemType Directory -Path $ShotDir) }
$settingsDir = Split-Path -Parent $SettingsFile
if ($settingsDir -and -not (Test-Path $settingsDir)) { [void](New-Item -ItemType Directory -Path $settingsDir) }
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }

$ExePath = [System.IO.Path]::GetFullPath($Exe)
if (-not (Test-Path $ExePath)) { throw "not built: $ExePath" }
if (-not (Test-Path $Ico)) { throw "no icon at $Ico - run icogen.ps1" }

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
// Named per-experiment on purpose: a PowerShell session keeps types once added, so two verifier
// scripts sharing a class name collide in the second one's Add-Type.
public static class WinExp12 {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct NID { public int cbSize; public IntPtr hWnd; public uint uID; public Guid guidItem; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    // The window's own icon: WM_GETICON is what the app set, GCLP_HICON what the class carries.
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr wp, IntPtr lp);
    [DllImport("user32.dll", EntryPoint="GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr h, int index);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int istep, IntPtr hbr, int flags);
    // The shell's own answer for an exe's icon, at a requested size. Not the app's word - Windows'.
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int PrivateExtractIcons(string file, int index, int cx, int cy, out IntPtr phicon, out int piconid, int nIcons, uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
    // The shell's own answer for where it put the notification-area icon belonging to a window.
    [DllImport("shell32.dll", SetLastError=true)] public static extern int Shell_NotifyIconGetRect(ref NID id, out RECT r);
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
function Note([string]$what) { Write-Host "  .... $what" }

function Title([IntPtr]$h) {
    $sb = New-Object System.Text.StringBuilder 512
    [void][WinExp12]::GetWindowText($h, $sb, $sb.Capacity)
    return $sb.ToString()
}

function HwndByTitle([int]$targetPid, [string]$title) {
    $hits = New-Object System.Collections.ArrayList
    $cb = [WinExp12+EnumProc] {
        param($h, $l)
        $owner = [uint32]0
        [void][WinExp12]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -eq $targetPid -and [WinExp12]::IsWindowVisible($h) -and (Title $h) -eq $title) { [void]$hits.Add($h) }
        return $true
    }
    [void][WinExp12]::EnumWindows($cb, [IntPtr]::Zero)
    if ($hits.Count -eq 0) { return [IntPtr]::Zero }
    return $hits[0]
}

function WaitFor($scriptblock, [int]$tries = 30, [int]$milliseconds = 250) {
    for ($i = 0; $i -lt $tries; $i++) {
        $value = & $scriptblock
        if ($null -ne $value) { return $value }
        Start-Sleep -Milliseconds $milliseconds
    }
    return $null
}

function NotifyIconRect([IntPtr]$hwnd) {
    if ($hwnd -eq [IntPtr]::Zero) { return $null }
    $id = New-Object WinExp12+NID
    $id.cbSize = [System.Runtime.InteropServices.Marshal]::SizeOf($id)
    $id.hWnd = $hwnd
    $id.uID = 1
    $r = New-Object WinExp12+RECT
    try { if ([WinExp12]::Shell_NotifyIconGetRect([ref]$id, [ref]$r) -ne 0) { return $null } }
    catch { return $null }
    if (($r.Right - $r.Left) -le 0 -or ($r.Bottom - $r.Top) -le 0) { return $null }
    return [pscustomobject]@{ L = $r.Left; T = $r.Top; R = $r.Right; B = $r.Bottom }
}

# --- pixels: an HICON and an .ico frame, drawn the same way, compared ---

function DrawIcon([IntPtr]$hIcon, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    # DI_NORMAL (3): the icon's own alpha/AND mask decides transparency, so a corner stays clear.
    [void][WinExp12]::DrawIconEx($hdc, 0, 0, $hIcon, $size, $size, 0, [IntPtr]::Zero, 3)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    return $bmp
}

function IconBitmap([string]$path, [int]$size) {
    $icon = New-Object System.Drawing.Icon -ArgumentList @($path, (New-Object System.Drawing.Size $size, $size))
    $bmp = DrawIcon $icon.Handle $size
    $icon.Dispose()
    return $bmp
}

# Mean absolute difference over all four channels, 0 = identical, 1 = opposite. Named PixelDiff, not
# Diff: PowerShell's `diff` is an alias for Compare-Object and would swallow a call.
function PixelDiff($a, $b, [int]$size) {
    $sum = 0.0
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $pa = $a.GetPixel($x, $y); $pb = $b.GetPixel($x, $y)
            $sum += ([Math]::Abs($pa.A - $pb.A) + [Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B)) / 1020.0
        }
    }
    return $sum / ($size * $size)
}

function PaintCount($bmp, [int]$size) {
    $n = 0
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) { if ($bmp.GetPixel($x, $y).A -gt 0) { $n++ } }
    }
    return $n
}

function ShotWindow([IntPtr]$h, [string]$path) {
    if ($h -eq [IntPtr]::Zero) { return }
    $r = New-Object WinExp12+RECT
    if (-not [WinExp12]::GetWindowRect($h, [ref]$r)) { return }
    $w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
    if ($w -le 0 -or $ht -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

function ShotRect([int]$l, [int]$t, [int]$w, [int]$h, [string]$path) {
    if ($w -le 0 -or $h -le 0) { return }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($l, $t, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

# The .ico's own directory, read byte by byte: an ICONDIR, an entry each, then the images. The widths
# are what the file claims; the drawing further down is what it really holds.
function IcoFrames([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $count = [BitConverter]::ToInt16($bytes, 4)
    $frames = @()
    for ($i = 0; $i -lt $count; $i++) {
        $at = 6 + $i * 16
        $w = [int]$bytes[$at]
        if ($w -eq 0) { $w = 256 }
        $len = [BitConverter]::ToInt32($bytes, $at + 8)
        $off = [BitConverter]::ToInt32($bytes, $at + 12)
        $png = $bytes[$off] -eq 0x89 -and $bytes[$off + 1] -eq 0x50
        $frames += [pscustomobject]@{ Size = $w; Length = $len; Offset = $off; Png = $png }
    }
    return $frames
}

function RunValue([string]$name) {
    $key = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $key) { return $null }
    return $key.$name
}

# --- the sizes Windows draws at on this display (40 and 20 at 125%, 32 and 16 at 100%) ---
$BigIcon = [WinExp12]::GetSystemMetrics(11)    # SM_CXICON
$SmallIcon = [WinExp12]::GetSystemMetrics(49)  # SM_CXSMICON
$ExpectedSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)

# --- state that must not change ---
$realSettings = Join-Path $env:APPDATA 'Monitron\settings.ini'
$realBefore = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
$runBefore = RunValue 'Monitron'

$ReportA = Join-Path $ShotDir 'report-window'
$ReportB = Join-Path $ShotDir 'report-tray'
foreach ($d in @($ReportA, $ReportB)) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }

try {

# ---------------------------------------------------------------------------
Write-Host "`n== the asset itself =="
$frames = IcoFrames $Ico
Note ("$Ico holds " + ($frames | ForEach-Object { "$($_.Size)$(if ($_.Png) { 'p' } else { 'd' })" }) -join ', ')
Check ($frames.Count -eq 9) "the .ico has nine frames"
$sizes = @($frames | ForEach-Object { $_.Size })
Check ((($sizes | Sort-Object) -join ',') -eq (($ExpectedSizes | Sort-Object) -join ',')) "at 16, 20, 24, 32, 40, 48, 64, 128 and 256"
Check (@($frames | Where-Object { $_.Size -lt 128 -and $_.Png }).Count -eq 0) "the frames below 128 px are DIBs"
Check (@($frames | Where-Object { $_.Size -ge 128 -and -not $_.Png }).Count -eq 0) "and 128 and 256 are PNGs"

$tray = IconBitmap $Ico 16
$painted = PaintCount $tray 16
Check ($painted -gt 0) "the 16 px frame really is drawn ink, not an empty image ($painted of 256 px)"
Check ($tray.GetPixel(0, 0).A -eq 0 -and $tray.GetPixel(15, 15).A -eq 0) "with the corners transparent - it is a mark, not a black square"
$tray.Save((Join-Path $ShotDir '01-tray-16.png'))
$tray256 = IconBitmap $Ico 256
$tray256.Save((Join-Path $ShotDir '02-icon-256.png'))
$tray.Dispose(); $tray256.Dispose()

# ---------------------------------------------------------------------------
Write-Host "`n== the shell's own answer for the exe's icon =="
$stockPath = Join-Path $env:SystemRoot 'system32\notepad.exe'
foreach ($size in @(16, 32, 48, 128)) {
    $hIcon = [IntPtr]::Zero; $id = 0
    $got = [WinExp12]::PrivateExtractIcons($ExePath, 0, $size, $size, [ref]$hIcon, [ref]$id, 1, 0)
    Check ($got -eq 1 -and $hIcon -ne [IntPtr]::Zero) "Windows extracts a $size px icon from the exe itself"
    if ($got -ne 1 -or $hIcon -eq [IntPtr]::Zero) { continue }
    $theirs = DrawIcon $hIcon $size
    [void][WinExp12]::DestroyIcon($hIcon)
    $ours = IconBitmap $Ico $size
    $diff = PixelDiff $theirs $ours $size
    Note ("$size px: the shell's icon differs from the file's $size frame by " + [Math]::Round($diff, 4))
    Check ($diff -lt 0.02) "and it is that very frame, drawn by the shell"
    if ($size -eq 32 -and (Test-Path $stockPath)) {
        $hStock = [IntPtr]::Zero; $idStock = 0
        if ([WinExp12]::PrivateExtractIcons($stockPath, 0, 32, 32, [ref]$hStock, [ref]$idStock, 1, 0) -eq 1) {
            $stock = DrawIcon $hStock 32
            [void][WinExp12]::DestroyIcon($hStock)
            $vsStock = PixelDiff $theirs $stock 32
            Note ("32 px: and another program's icon differs from it by " + [Math]::Round($vsStock, 4))
            Check ($vsStock -gt 0.25) "so the exe is not carrying some stock icon"
            $stock.Dispose()
        }
    }
    $theirs.Save((Join-Path $ShotDir ("03-exe-icon-" + $size + ".png")))
    $theirs.Dispose(); $ours.Dispose()
}

# ---------------------------------------------------------------------------
Write-Host "`n== the running window's own icon (Win32), not the app's word =="
$argline = "--no-tray --no-schedule --settings-file `"$SettingsFile`" --icon-report `"$ReportA`""
Note "app: Monitron.App.exe $argline"
$p = Start-Process -FilePath $Exe -ArgumentList $argline -PassThru
$surface = WaitFor { $h = HwndByTitle $p.Id 'Monitron'; if ($h -ne [IntPtr]::Zero) { $h } else { $null } }
Check ($null -ne $surface) "the surface window is up"
if ($null -eq $surface) { $p.Kill(); throw "no surface window appeared" }
ShotWindow $surface (Join-Path $ShotDir '04-surface.png')

$hicon = [WinExp12]::SendMessage($surface, 0x7F, [IntPtr]1, [IntPtr]::Zero)   # WM_GETICON, ICON_BIG
if ($hicon -eq [IntPtr]::Zero) { $hicon = [WinExp12]::GetClassLongPtr($surface, -14) }  # GCLP_HICON
Check ($hicon -ne [IntPtr]::Zero) "the window carries an icon at all (Win32 says so)"
if ($hicon -ne [IntPtr]::Zero) {
    $theirs = DrawIcon $hicon $BigIcon
    $ours = IconBitmap $Ico $BigIcon
    $diff = PixelDiff $theirs $ours $BigIcon
    Note ("the window's $BigIcon px icon differs from the file's frame by " + [Math]::Round($diff, 4))
    Check ($diff -lt 0.12) "and it is the designed mark - the same pixels the file holds, at this display's icon size"
    $stock = New-Object System.Drawing.Icon -ArgumentList @([System.Drawing.SystemIcons]::Application, (New-Object System.Drawing.Size $BigIcon, $BigIcon))
    $stockBmp = DrawIcon $stock.Handle $BigIcon
    $vsStock = PixelDiff $theirs $stockBmp $BigIcon
    Check ($diff -lt $vsStock / 3) "much closer to it than to the stock application icon ($([Math]::Round($vsStock, 4)))"
    $theirs.Save((Join-Path $ShotDir '05-window-icon.png'))
    $theirs.Dispose(); $ours.Dispose(); $stockBmp.Dispose(); $stock.Dispose()
}

$report = Join-Path $ReportA 'icon.txt'
Check (Test-Path $report) "the app wrote its own icon report"
if (Test-Path $report) {
    $own = @{}
    foreach ($line in Get-Content $report) { $kv = $line -split '=', 2; if ($kv.Count -eq 2) { $own[$kv[0]] = $kv[1] } }
    Note "the app says: resource=$($own['resource']) frames=$($own['frames']) windowIcon=$($own['windowIcon']) trayIcon=$($own['trayIcon'])"
    Check ($own['resource'] -eq 'Monitron.ico') "it names the generated asset"
    Check ($own['frames'] -eq (($ExpectedSizes) -join ',')) "and lists the nine frames in order"
    Check ($own['windowIcon'] -eq 'true') "and says the window carries it"
    Check ($own['trayIcon'] -eq 'false') "and that --no-tray means no tray icon"
}
$p.Kill(); $p.WaitForExit(5000) | Out-Null

# ---------------------------------------------------------------------------
Write-Host "`n== the tray icon is the same asset =="
$argline = "--no-schedule --settings-file `"$SettingsFile`" --tray-report `"$ReportB`" --icon-report `"$ReportB`" --quit-after 8"
Note "app: Monitron.App.exe $argline"
$p = Start-Process -FilePath $Exe -ArgumentList $argline -PassThru
$surface = WaitFor { $h = HwndByTitle $p.Id 'Monitron'; if ($h -ne [IntPtr]::Zero) { $h } else { $null } }
Check ($null -ne $surface) "the surface window is up"
if ($null -eq $surface) { $p.Kill(); throw "no surface window appeared" }

$iconRect = WaitFor { NotifyIconRect $surface } 30 400
Check ($null -ne $iconRect) "the shell reports a notification-area icon for the surface window"
if ($null -ne $iconRect) {
    Note ("the shell put it at ({0},{1})-({2},{3}) px" -f $iconRect.L, $iconRect.T, $iconRect.R, $iconRect.B)
    # EXP11's boundary: a newly registered icon is put in the hidden-icons overflow, so the shell's
    # rectangle is that overflow window's - the shot below shows the area, not the glyph itself.
    Note "the icon is in the hidden-icons overflow, so that rectangle is the overflow area, not the glyph"
    ShotRect $iconRect.L $iconRect.T ($iconRect.R - $iconRect.L) ($iconRect.B - $iconRect.T) (Join-Path $ShotDir '06-tray-area.png')
}

$report = Join-Path $ReportB 'icon.txt'
Check (Test-Path $report) "the app wrote its icon report for the tray run too"
if (Test-Path $report) {
    $own = @{}
    foreach ($line in Get-Content $report) { $kv = $line -split '=', 2; if ($kv.Count -eq 2) { $own[$kv[0]] = $kv[1] } }
    Note "the app says: resource=$($own['resource']) frames=$($own['frames']) trayIcon=$($own['trayIcon'])"
    Check ($own['resource'] -eq 'Monitron.ico') "it names the generated asset"
    Check ($own['trayIcon'] -eq 'true') "and that the tray took it"
    Check ($own['frames'] -eq (($ExpectedSizes) -join ',')) "with the nine frames, matching the file's own directory"
}

$p.WaitForExit(30000) | Out-Null
if (-not $p.HasExited) { $p.Kill() }

# ---------------------------------------------------------------------------
Write-Host "`n== nothing outside the run changed =="
$realAfter = if (Test-Path $realSettings) { (Get-FileHash $realSettings -Algorithm SHA256).Hash } else { 'absent' }
Check ($realAfter -eq $realBefore) "the user's real settings file is untouched ($realBefore)"
Check ((RunValue 'Monitron') -eq $runBefore) "and the user's real Run entry is untouched"
$gone = WaitFor { if (Get-Process -Name 'Monitron.App' -ErrorAction SilentlyContinue) { $null } else { 'gone' } } 10 300
Check ($gone -eq 'gone') "and no Monitron process is left running"

} finally {
    Get-Process -Name 'Monitron.App' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "  $script:passed passed, $script:failed failed"
Write-Host "  shots and reports in $ShotDir"
if ($script:failed -ne 0) { exit 1 }
