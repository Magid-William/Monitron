# EXP06 - build and run the headless NightLightScheduler test.
#
# The schedule machine is asserted by moving a clock by hand and invoking its own transitions: a
# whole night (fade down, hold across midnight, fade back, the override, the switch) is driven in
# milliseconds, with no hardware, no sleeping and no test framework. It needs a console exe because
# build.ps1's output is a winexe and cannot print. Schedule.cs carries no P/Invoke and no WPF, so it
# compiles here together with the model it reads.
#
#   .\schedule-test.ps1        # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'schedule-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'ScheduleSettings.cs') `
    (Join-Path $here 'Schedule.cs') `
    (Join-Path $here 'ScheduleTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "NightLightScheduler tests FAILED (exit $LASTEXITCODE)" }
Write-Host "schedule tests passed"
