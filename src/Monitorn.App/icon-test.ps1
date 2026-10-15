# EXP12 - build and run the headless icon test.
#
# Drives the icon pipeline with no window and no shell: the narrow SVG reader over the repo's real
# logo.svg, the WPF rasteriser at known sizes, and the ICO container parsed back. Then it compares
# the committed Monitorn.ico with a fresh render, byte for byte.
#
#   .\icon-test.ps1          # prints one line per assertion; exit code 0 when all pass
$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$refs = @("$(Join-Path $net 'WPF')\PresentationFramework.dll",
    "$(Join-Path $net 'WPF')\PresentationCore.dll",
    "$(Join-Path $net 'WPF')\WindowsBase.dll",
    (Join-Path $net 'System.Xaml.dll'))

$here = Split-Path -Parent $PSCommandPath
$logo = Join-Path (Split-Path -Parent (Split-Path -Parent $here)) 'logo.svg'
$ico = Join-Path $here 'Monitorn.ico'
$out = Join-Path $here 'icon-test.exe'

# /main: because IconGen.cs also carries icogen's own entry point.
& $csc /nologo /target:exe /platform:x64 /main:Monitorn.App.IconTests "/out:$out" `
    ($refs | ForEach-Object { "/reference:$_" }) `
    (Join-Path $here 'SvgIcon.cs') `
    (Join-Path $here 'IcoFile.cs') `
    (Join-Path $here 'IconGen.cs') `
    (Join-Path $here 'IconTests.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

& $out $logo $ico
if ($LASTEXITCODE -ne 0) { throw "Icon tests FAILED (exit $LASTEXITCODE)" }
Write-Host "icon tests passed"
