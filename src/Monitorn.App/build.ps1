# Builds the Monitorn control surface with the in-box .NET Framework 4.8 compiler
# (no SDK, no Visual Studio, no download). EXP03, extended by EXP04, extended by EXP05,
# extended by EXP06 (the schedule machine), extended by EXP07 (the settings file),
# extended by EXP08 (the theme: Theme.cs, Theming.cs, SystemTheme.cs, ThemeReport.cs),
# extended by EXP09 (the screen position: Placement.cs, SystemPlacement.cs, PlacementApp.cs) and by
# EXP10 (each row's display id as its UI automation id, for the N-display verifiers), and by EXP11
# (the tray shell: Tray.cs, AutoStart.cs, TrayIcon.cs - and the System.Drawing the icon is drawn with),
# and by EXP12 (the app's own icon: IconAssets.cs + IcoFile.cs read the generated Monitorn.ico, which
# csc embeds with /win32icon and /resource; icogen.ps1 regenerates it from logo.svg).
#
# The UI is written code-only (no XAML), so no markup/BAML step is needed and csc alone
# can produce the executable; WPF comes from the in-box runtime assemblies.
#
# Sources are listed one by one on purpose: ScheduleSettingsTests.cs, BrightnessWriterTests.cs and
# ScheduleTests.cs have their own Main and belong to model-test.ps1 / writer-test.ps1 /
# schedule-test.ps1, not to this winexe.
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

# WPF from the runtime's WPF folder, plus System.Xaml (referenced by PresentationFramework)
# and System.Management (the WMI brightness API, EXP05).
$refs = @("$(Join-Path $net 'WPF')\PresentationFramework.dll",
    "$(Join-Path $net 'WPF')\PresentationCore.dll",
    "$(Join-Path $net 'WPF')\WindowsBase.dll",
    (Join-Path $net 'System.Xaml.dll'),
    (Join-Path $net 'System.Management.dll'),
    (Join-Path $net 'System.Drawing.dll'))
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "reference assembly not found: $r" } }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'Monitorn.App.exe'

# The app's icon (EXP12): Monitorn.ico is generated from the repo's logo.svg and committed, so a
# clone builds without WPF's rasteriser; it is only regenerated when it is missing or stale.
$logo = Join-Path (Split-Path -Parent (Split-Path -Parent $here)) 'logo.svg'
$ico = Join-Path $here 'Monitorn.ico'
if ((Test-Path $logo) -and ((Test-Path $ico) -eq $false -or (Get-Item $ico).LastWriteTime -lt (Get-Item $logo).LastWriteTime))
{
    Write-Host "regenerating $ico from $logo"
    & (Join-Path $here 'icogen.ps1')
    if (-not (Test-Path $ico)) { throw "icogen did not produce $ico" }
}
if (-not (Test-Path $ico)) { throw "icon not found at $ico - run icogen.ps1" }

$args = @('/nologo', '/target:winexe', '/platform:x64', "/out:$out",
    "/win32manifest:$(Join-Path $here 'app.manifest')",
    "/win32icon:$ico", "/resource:$ico,Monitorn.ico") +
    ($refs | ForEach-Object { "/reference:$_" }) +
    @((Join-Path $here 'Program.cs'),
      (Join-Path $here 'Brightness.cs'),
      (Join-Path $here 'BrightnessController.cs'),
      (Join-Path $here 'Schedule.cs'),
      (Join-Path $here 'ScheduleSettings.cs'),
      (Join-Path $here 'SettingsPersistence.cs'),
      (Join-Path $here 'Theme.cs'),
      (Join-Path $here 'Theming.cs'),
      (Join-Path $here 'SystemTheme.cs'),
      (Join-Path $here 'ThemeReport.cs'),
      (Join-Path $here 'Placement.cs'),
      (Join-Path $here 'SystemPlacement.cs'),
      (Join-Path $here 'PlacementApp.cs'),
      (Join-Path $here 'Tray.cs'),
      (Join-Path $here 'AutoStart.cs'),
      (Join-Path $here 'TrayIcon.cs'),
      (Join-Path $here 'IcoFile.cs'),
      (Join-Path $here 'IconAssets.cs'),
      (Join-Path $here 'SettingsWindow.cs'))

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }
Write-Host "built $out"
