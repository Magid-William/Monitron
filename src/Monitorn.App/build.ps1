# Builds the Monitorn control surface with the in-box .NET Framework 4.8 compiler
# (no SDK, no Visual Studio, no download). EXP03, extended by EXP04.
#
# The UI is written code-only (no XAML), so no markup/BAML step is needed and csc alone
# can produce the executable; WPF comes from the in-box runtime assemblies.
#
# Sources are listed one by one on purpose: ScheduleSettingsTests.cs has its own Main and
# belongs to model-test.ps1, not to this winexe.
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

# WPF from the runtime's WPF folder, plus System.Xaml (referenced by PresentationFramework).
$refs = @("$(Join-Path $net 'WPF')\PresentationFramework.dll",
    "$(Join-Path $net 'WPF')\PresentationCore.dll",
    "$(Join-Path $net 'WPF')\WindowsBase.dll",
    (Join-Path $net 'System.Xaml.dll'))
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "reference assembly not found: $r" } }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'Monitorn.App.exe'

$args = @('/nologo', '/target:winexe', '/platform:x64', "/out:$out",
    "/win32manifest:$(Join-Path $here 'app.manifest')") +
    ($refs | ForEach-Object { "/reference:$_" }) +
    @((Join-Path $here 'Program.cs'),
      (Join-Path $here 'ScheduleSettings.cs'),
      (Join-Path $here 'SettingsWindow.cs'))

& $csc @args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }
Write-Host "built $out"
