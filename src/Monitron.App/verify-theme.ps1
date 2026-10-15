# EXP08 verification helper - render both windows in every theme and assert what was painted.
#
# UI Automation can drive a window and read the state it reports, but it cannot see color. So the
# witness here is the app's own theme report (ThemeReport.cs): each window is rendered to a bitmap
# and named regions are sampled - the window background, a row card, the slider's unfilled side, a
# check box, a radio mark, a field's face, a button's face - and written to a file beside the PNG.
# This script runs the app once per case, reads those numbers, and asserts them.
#
# The same report run with --theme-no-templates is the experiment's measurement: which regions a
# pure brush swap leaves light.
#
#   .\verify-theme.ps1                       # every case, evidence into %TEMP%\monitron-exp08
#   .\verify-theme.ps1 -OutDir .\out         # keep the evidence somewhere else
param(
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$OutDir = (Join-Path $env:TEMP 'monitron-exp08'),
    # The read-only EXP02 witness, so "no monitor was touched" does not rest on this script's word.
    [string]$Probe = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'probe\probe.exe')
)

$ErrorActionPreference = 'Stop'

$ReportRoot = Join-Path $OutDir 'reports'
$SettingsFile = Join-Path $OutDir 'state\settings.ini'
$RealSettings = Join-Path $env:APPDATA 'Monitron\settings.ini'
New-Item -ItemType Directory -Force -Path $ReportRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $SettingsFile) | Out-Null

$script:passed = 0
$script:failed = 0

function Check($condition, [string]$what) {
    if ($condition) { $script:passed++; Write-Host "  ok   $what" }
    else { $script:failed++; Write-Host "  FAIL $what" }
}

# --- runs ---

function RunReport([string]$label, [string]$appArgs) {
    $dir = Join-Path $ReportRoot $label
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }
    $p = Start-Process -FilePath $Exe -ArgumentList "$appArgs --theme-report `"$dir`" --settings-file `"$SettingsFile`"" -PassThru
    if (-not $p.WaitForExit(30000)) { $p.Kill(); throw "run '$label' did not exit" }
    if ($p.ExitCode -ne 0) { throw "run '$label' exited $($p.ExitCode)" }
    return $dir
}

function ReadReport([string]$path) {
    $report = @{}
    foreach ($line in Get-Content $path) {
        if ($line -match '^(elem\.[\w.]+) box=(\d+,\d+,\d+,\d+) avg=(#[0-9A-F]{6}) light=([\d.]+) dark=([\d.]+) n=(\d+)$') {
            $report[$matches[1]] = @{ box = $matches[2]; avg = $matches[3]; light = [double]$matches[4]; dark = [double]$matches[5] }
        }
        elseif ($line -match '^(elem\.[\w.]+) back=(\S+) ink=(\S+) ourTemplate=(true|false)$') {
            $report[$matches[1]] = @{ back = $matches[2]; ink = $matches[3]; ours = $matches[4] }
        }
        elseif ($line -match '^([\w.]+)=(.*)$') {
            $report[$matches[1]] = $matches[2]
        }
    }
    return $report
}

function Surface([string]$dir) { return ReadReport (Join-Path $dir 'surface.txt') }
function Settings([string]$dir) { return ReadReport (Join-Path $dir 'settings.txt') }

# Short reads, so the assertions below stay readable.
function L($report, [string]$name) { return [double]$report["elem.$name"].light }
function D($report, [string]$name) { return [double]$report["elem.$name"].dark }

# --- the outside witness ---

function ProbeRead {
    $out = & $Probe 2>&1 | Out-String
    $m = [regex]::Match($out, 'brightness cur=(\d+)')
    if (-not $m.Success) { throw "probe could not read a brightness: $out" }
    return [int]$m.Groups[1].Value
}

$realExisted = Test-Path $RealSettings
$realHash = if ($realExisted) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
$before = ProbeRead
Write-Host "the probe reads the display at $before before anything runs"
Write-Host "the user's real settings file $(if ($realExisted) { 'exists' } else { 'does not exist' }) before anything runs"

# --- light ---

Write-Host "`n== light: the palette the app has always drawn =="
$dir = RunReport 'light' '--theme light'
$s = Surface $dir; $g = Settings $dir
Check ($s['mode'] -eq 'light' -and $g['mode'] -eq 'light') "both windows report mode=light"
Check ($s['titlebar'] -eq 'light' -and $g['titlebar'] -eq 'light') "DWM reports a light title bar on both"
Check ($s['rendered'] -eq 'ok' -and $g['rendered'] -eq 'ok') "both windows rendered something"
Check ($s['palette.windowBg'] -eq '#FFFFFF') "the light palette's window background is #FFFFFF"
Check ((L $s 'bg') -ge 0.90 -and (L $g 'bg') -ge 0.90) "both window backgrounds are light ($(L $s 'bg'), $(L $g 'bg'))"
Check ((L $s 'row') -ge 0.90) "the monitor row card is light ($(L $s 'row'))"
Check ((L $s 'sliderRest') -ge 0.90) "the slider's unfilled side is light ($(L $s 'sliderRest'))"
Check ((L $g 'check') -ge 0.90) "the check box is light ($(L $g 'check'))"
Check ((L $g 'radio') -ge 0.90) "the radio mark is light ($(L $g 'radio'))"
Check ((L $g 'edit') -ge 0.60) "the field's face is light ($(L $g 'edit'))"
Check ((L $g 'button') -ge 0.60 -and (L $s 'button') -ge 0.60) "the buttons are light ($(L $g 'button'), $(L $s 'button'))"

# --- dark ---

Write-Host "`n== dark: the same windows, one palette over =="
$dir = RunReport 'dark' '--theme dark'
$s = Surface $dir; $g = Settings $dir
Check ($s['mode'] -eq 'dark' -and $g['mode'] -eq 'dark') "both windows report mode=dark"
Check ($s['titlebar'] -eq 'dark' -and $g['titlebar'] -eq 'dark') "DWM reports a dark title bar on both"
Check ($s['palette.windowBg'] -eq '#1E1E1E') "the dark palette's window background is #1E1E1E"
Check ((D $s 'bg') -ge 0.90 -and (D $g 'bg') -ge 0.90) "both window backgrounds are dark ($(D $s 'bg'), $(D $g 'bg'))"
Check ((D $s 'row') -ge 0.90) "the monitor row card is dark ($(D $s 'row'))"
Check ((L $s 'sliderRest') -le 0.05) "the slider's unfilled side is dark now ($(L $s 'sliderRest') light)"
Check ((L $g 'check') -le 0.05) "the check box is dark now ($(L $g 'check') light)"
Check ((L $g 'radio') -le 0.05) "the radio mark is dark now ($(L $g 'radio') light)"
Check ((L $g 'edit') -le 0.35) "the field's face is dark now ($(L $g 'edit') light)"
Check ((L $g 'button') -le 0.45 -and (L $s 'button') -le 0.45) "the buttons are dark now ($(L $g 'button'), $(L $s 'button') light)"
Check ($g['elem.edit.paint'].ours -eq 'true') "and the check box's dark look is ours, not the OS template's"

# --- the measurement: what a brush swap alone reaches ---

Write-Host "`n== the measurement: the same palette with no templates of ours =="
$dir = RunReport 'raw' '--theme dark --theme-no-templates'
$s = Surface $dir; $g = Settings $dir
Check ($s['mode'] -eq 'dark' -and $s['templates'] -eq 'false') "the run is dark with templates off"
Check ($g['elem.edit.paint'].ours -eq 'false') "the controls really are on the OS templates"
Check ((D $s 'bg') -ge 0.90 -and (D $s 'row') -ge 0.90) "the palette still reaches the background and the row card ($(D $s 'bg'), $(D $s 'row'))"
Check ((L $s 'sliderRest') -ge 0.25) "the Slider resists: its unfilled side stays light ($(L $s 'sliderRest') light)"
Check ((L $g 'check') -ge 0.20) "the CheckBox resists: its mark stays light ($(L $g 'check') light)"
Check ((L $g 'radio') -ge 0.20) "the RadioButton resists: its mark stays light ($(L $g 'radio') light)"
Check ((L $g 'edit') -le 0.35) "the TextBox does NOT resist: a property assignment is enough ($(L $g 'edit') light)"
Check ((L $g 'button') -le 0.45) "the Button face does NOT resist either ($(L $g 'button') light)"

$light = Surface (Join-Path $ReportRoot 'light'); $dark = Surface (Join-Path $ReportRoot 'dark')
$gLight = Settings (Join-Path $ReportRoot 'light'); $gDark = Settings (Join-Path $ReportRoot 'dark')
Check ((L $dark 'sliderRest') -lt (L $s 'sliderRest') -and (L $s 'sliderRest') -lt (L $light 'sliderRest')) `
    "the slider's side runs light $(L $light 'sliderRest') -> OS-with-dark-palette $(L $s 'sliderRest') -> ours $(L $dark 'sliderRest')"
Check ((L $gDark 'check') -lt (L $g 'check') -and (L $g 'check') -lt (L $gLight 'check')) `
    "and the check box light $(L $gLight 'check') -> $(L $g 'check') -> $(L $gDark 'check')"

# --- auto ---

Write-Host "`n== auto follows the OS reading =="
$dir = RunReport 'auto-os-light' '--theme auto --os-theme light'
$s = Surface $dir
Check ($s['mode'] -eq 'light' -and $s['osLight'] -eq 'true' -and $s['preference'] -eq 'auto') `
    "auto + an OS set to light -> light (osLight=$( $s['osLight'] ))"
$dir = RunReport 'auto-os-dark' '--theme auto --os-theme dark'
$s = Surface $dir
Check ($s['mode'] -eq 'dark' -and $s['osLight'] -eq 'false' -and $s['preference'] -eq 'auto') `
    "auto + an OS set to dark -> dark (osLight=$($s['osLight']))"
Check ((L $s 'sliderRest') -le 0.05) "and it is a real dark render, not just a label ($(L $s 'sliderRest') light)"

# --- the live switch ---

Write-Host "`n== following the OS while it runs: a scripted color-set change =="
$dir = Join-Path $ReportRoot 'live'
Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $SettingsFile) { Remove-Item $SettingsFile -Force }
$p = Start-Process -FilePath $Exe -ArgumentList `
    "--theme auto --os-theme light --theme-toggle-after 1.5 dark --theme-report `"$dir`" --settings-file `"$SettingsFile`"" -PassThru
$modes = New-Object System.Collections.Generic.List[string]
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline -and -not $p.HasExited) {
    $f = Join-Path $dir 'settings.txt'
    if (Test-Path $f) {
        $line = Get-Content $f | Select-String '^mode=' | Select-Object -First 1
        if ($line) {
            $m = $line.ToString()
            if ($modes.Count -eq 0 -or $modes[$modes.Count - 1] -ne $m) { $modes.Add($m) }
        }
    }
    Start-Sleep -Milliseconds 100
}
if (-not $p.HasExited) { $p.WaitForExit(10000) | Out-Null }
Check (($modes -join ',') -eq 'mode=light,mode=dark') "the report went light then dark ($($modes -join ' then '))"
$g = Settings $dir
Check ($g['mode'] -eq 'dark' -and $g['titlebar'] -eq 'dark') "the last report is dark, title bar included"
Check ((D $g 'bg') -ge 0.90) "and its pixels are dark ($(D $g 'bg'))"
Check ((L $g 'check') -le 0.05) "with the rebuilt check box dark too ($(L $g 'check') light)"

# --- side effects ---

Write-Host "`n== what these runs must not have touched =="
Check ((Test-Path $RealSettings) -eq $realExisted) "the user's real %APPDATA% settings file was not created (`"$RealSettings`")"
$nowHash = if ((Test-Path $RealSettings)) { (Get-FileHash $RealSettings -Algorithm SHA256).Hash } else { '' }
Check ($nowHash -eq $realHash) "and it still has its exact contents ($($realHash.Substring(0,12))...)"
$after = ProbeRead
Check ($after -eq $before) "the display is still at $before (probe saw $after): no theme run wrote to a monitor"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
Write-Host ("reports and PNGs: $ReportRoot")
if ($script:failed -gt 0) { exit 1 }
