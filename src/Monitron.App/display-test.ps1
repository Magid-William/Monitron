# EXP14 - build and run the headless display-set test.
#
# The diff rule and the registry are pure C# (DisplaySet.cs), so a display change can be advanced by
# invoking Refresh() with a fake controller - no hardware, no sleeping, no test framework. It needs a
# console exe because build.ps1's output is a winexe and cannot print. Brightness.cs and Schedule.cs
# come along because the registry drives the real writer and the real schedule.
#
#   .\display-test.ps1        # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'display-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'Brightness.cs') `
    (Join-Path $here 'ScheduleSettings.cs') `
    (Join-Path $here 'Schedule.cs') `
    (Join-Path $here 'DisplaySet.cs') `
    (Join-Path $here 'DisplayTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "DisplaySet tests FAILED (exit $LASTEXITCODE)" }
Write-Host "display tests passed"
