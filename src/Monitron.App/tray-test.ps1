# EXP11 - build and run the headless tray test.
#
# The tray's decisions (the Show/Hide line, the Run command and whether an existing value is ours,
# enabling/disabling over a store) are asserted by driving the model and a fake IStartupStore - no
# registry, no shell, no UI. It needs a console exe because build.ps1's output is a winexe and
# cannot print.
#
#   .\tray-test.ps1          # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'tray-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'Tray.cs') `
    (Join-Path $here 'AutoStart.cs') `
    (Join-Path $here 'TrayTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "Tray tests FAILED (exit $LASTEXITCODE)" }
Write-Host "tray tests passed"
