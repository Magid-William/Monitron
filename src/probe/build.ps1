# Builds Probe.cs with the in-box .NET Framework 4.8 compiler (no SDK, no VS, no download).
# EXP02 throwaway tooling.
$ErrorActionPreference = 'Stop'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "in-box csc not found at $csc" }
$here = Split-Path -Parent $PSCommandPath
$out = Join-Path $here 'probe.exe'
& $csc /nologo /target:exe /platform:x64 "/out:$out" (Join-Path $here 'Probe.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit $LASTEXITCODE" }
Write-Host "built $out"
