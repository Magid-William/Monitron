# EXP08 - build and run the headless theme test.
#
# The theme model (the two palettes, the auto/pinned resolution rule, the parsing and the hex
# formatting) is asserted without WPF, without an OS and without a window. It needs a console exe
# because build.ps1's output is a winexe and cannot print.
#
#   .\theme-test.ps1          # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'theme-test.exe'

& $csc /nologo /target:exe /platform:x64 "/out:$out" `
    (Join-Path $here 'Theme.cs') `
    (Join-Path $here 'ThemeTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out
if ($LASTEXITCODE -ne 0) { throw "Theme tests FAILED (exit $LASTEXITCODE)" }
Write-Host "theme tests passed"
