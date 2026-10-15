# EXP07 - build and run the headless settings-persistence test.
#
# The codec (text <-> ScheduleSettings) and the store (a real temp file) are asserted by driving
# them: a full round trip, a missing file, a partial/corrupt file, a refused value, an unwritable
# path, and the Changed event. No hardware, no UI, no test framework. It needs a console exe
# because build.ps1's output is a winexe and cannot print; SettingsPersistence.cs has no WPF.
#
#   .\persistence-test.ps1     # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'persistence-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'ScheduleSettings.cs') `
    (Join-Path $here 'SettingsPersistence.cs') `
    (Join-Path $here 'SettingsPersistenceTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "SettingsPersistence tests FAILED (exit $LASTEXITCODE)" }
Write-Host "persistence tests passed"
