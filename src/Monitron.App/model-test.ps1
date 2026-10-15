# EXP04 - build and run the headless ScheduleSettings test.
#
# The machine logic (mode coupling, the enable gate, strength and 12-hour parsing) is asserted
# by driving the model's own transitions - no test framework, no clock, no UI. It needs a
# console exe because build.ps1's output is a winexe and cannot print.
#
#   .\model-test.ps1          # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'model-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'ScheduleSettings.cs') `
    (Join-Path $here 'ScheduleSettingsTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "ScheduleSettings tests FAILED (exit $LASTEXITCODE)" }
Write-Host "model tests passed"
