# EXP12 - builds icogen.exe (the icon rasteriser) with the in-box csc and runs it over logo.svg.
#
# icogen is a tool, not the app: it is a console exe built from SvgIcon.cs + IcoFile.cs + IconGen.cs
# and it never ships. build.ps1 runs this script only when Monitron.ico is missing or older than
# logo.svg, so a fresh clone builds from the committed .ico without needing WPF here.
#
#   pwsh -File icogen.ps1                 # src/Monitron.App/Monitron.ico, the default nine frames
#   pwsh -File icogen.ps1 -Png preview    # plus one png per frame, to look at
param([string]$Png)

$ErrorActionPreference = 'Stop'

$net = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $net 'csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }

$refs = @("$(Join-Path $net 'WPF')\PresentationFramework.dll",
    "$(Join-Path $net 'WPF')\PresentationCore.dll",
    "$(Join-Path $net 'WPF')\WindowsBase.dll",
    (Join-Path $net 'System.Xaml.dll'))

$here = Split-Path -Parent $PSCommandPath
$logo = Join-Path (Resolve-Path (Join-Path $here '..\..')) 'logo.svg'
if (-not (Test-Path $logo)) { throw "logo not found at $logo" }

$exe = Join-Path $here 'icogen.exe'
$ico = Join-Path $here 'Monitron.ico'

$build = @('/nologo', '/target:exe', '/platform:x64', "/out:$exe") +
    ($refs | ForEach-Object { "/reference:$_" }) +
    @((Join-Path $here 'SvgIcon.cs'),
      (Join-Path $here 'IcoFile.cs'),
      (Join-Path $here 'IconGen.cs'))

& $csc @build
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }

$run = @($logo, $ico)
if ($PSBoundParameters.ContainsKey('Png')) { $run += @('--png', (Join-Path $here $Png)) }
& $exe @run
if ($LASTEXITCODE -ne 0) { throw "icogen failed with exit $LASTEXITCODE" }
