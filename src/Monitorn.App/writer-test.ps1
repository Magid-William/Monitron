# EXP05 - build and run the headless BrightnessWriter test.
#
# The debounce machine (when a slider value is written, and that the last one wins) is asserted by
# driving its own transitions with a fake controller and an injected clock - no hardware, no
# sleeping, no test framework. It needs a console exe because build.ps1's output is a winexe and
# cannot print. Only Brightness.cs is compiled (no P/Invoke, no WMI).
#
#   .\writer-test.ps1          # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'writer-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'Brightness.cs') `
    (Join-Path $here 'BrightnessWriterTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "BrightnessWriter tests FAILED (exit $LASTEXITCODE)" }
Write-Host "writer tests passed"
