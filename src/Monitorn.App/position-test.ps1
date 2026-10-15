# EXP09 - build and run the headless position test.
#
# The placement model (the pixels<->DIP conversion, which side a taskbar is docked on, the corner a
# window is put in, and the clamp that keeps it on screen) is asserted without WPF, without a window
# and without touching the desktop. It needs a console exe because build.ps1's output is a winexe and
# cannot print.
#
#   .\position-test.ps1       # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'position-test.exe'

# Placement.cs only: it is the pure half, so this test needs no WPF and no Win32.
& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'Placement.cs') `
    (Join-Path $here 'PlacementTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "Position tests FAILED (exit $LASTEXITCODE)" }
Write-Host "position tests passed"
