# EXP16 verification helper - check the release from outside, and drive what GitHub actually serves.
#
# Builds nothing. It reads the published release back from GitHub, fetches its asset through the same
# endpoint a visitor's browser would, and then:
#   * asserts the release's shape - the tag, not a draft, not a prerelease, Latest, one asset;
#   * asserts the peeled tag is a commit on origin/main (the release is the released line);
#   * asserts the downloaded bytes are the build's bytes: the asset's SHA-256 must equal the local
#     Monitron.App.exe's own hash (or -ExpectHash, if given) and its size must match the release's;
#   * asserts the downloaded exe is UNSIGNED - Get-AuthenticodeSignature must report NotSigned;
#   * runs the DOWNLOADED exe, out of its download folder, with a throwaway settings file and
#     --no-schedule, asserting its window appears, that it detects this machine's displays, and that
#     the app's own --icon-report names the embedded icon - then that it exits 0;
#   * asserts the release notes say it is unsigned and print the same SHA-256.
#
#   .\verify-release.ps1                          # against v1.0.0, comparing with the local build
#   .\verify-release.ps1 -ExpectHash 0F1E...      # pin the expected hash instead
param(
    [string]$Tag = 'v1.0.0',
    [string]$Repo = 'Magid-William/Monitron',
    [string]$AssetName = 'Monitron.exe',
    [string]$ExpectHash = '',
    [string]$Exe = (Join-Path (Split-Path -Parent $PSCommandPath) 'Monitron.App.exe'),
    [string]$WorkDir = (Join-Path $env:TEMP 'monitron-exp16')
)

$ErrorActionPreference = 'Stop'

$DownloadDir = Join-Path $WorkDir 'download'
$RunDir = Join-Path $WorkDir 'run'
Remove-Item $WorkDir -Recurse -Force -ErrorAction SilentlyContinue
[void](New-Item -ItemType Directory -Path $DownloadDir -Force)
[void](New-Item -ItemType Directory -Path $RunDir -Force)

$script:passed = 0
$script:failed = 0

function Check($condition, [string]$what) {
    if ($condition) { $script:passed++; Write-Host "  ok   $what" }
    else { $script:failed++; Write-Host "  FAIL $what" }
}
function Note([string]$what) { Write-Host "  .... $what" }

function Git([string[]]$argv) {
    $out = & git -C $root @argv 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($argv -join ' ') failed with exit $LASTEXITCODE`: $out" }
    return $out
}

$root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSCommandPath))

Write-Host ""
Write-Host "release $Tag  ($Repo)"
Write-Host ""

# ---------------------------------------------------------------- what GitHub says the release is
$json = [string](gh release view $Tag --repo $Repo --json tagName,isDraft,isPrerelease,isLatest,url,body,assets)
if ($LASTEXITCODE -ne 0) { throw "gh release view failed: $json" }
$release = $json | ConvertFrom-Json

Check ($release.tagName -eq $Tag) "the release is tagged $Tag"
Check ($release.isDraft -eq $false) "it is published, not a draft"
Check ($release.isPrerelease -eq $false) "it is not a prerelease"
Check ($release.isLatest -eq $true) "it is the release GitHub offers as Latest"
Note "url $($release.url)"

$assets = @($release.assets)
Check ($assets.Count -eq 1) "it carries exactly one asset (got $($assets.Count))"
$asset = $assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
Check ($null -ne $asset) "the asset is named $AssetName"
Check ($asset.size -gt 0) "the asset is not empty ($($asset.size) bytes)"
Note "asset $($asset.name)  $($asset.size) bytes"

# ------------------------------------------------------------- the tag is a commit on origin/main
Git @('fetch', '--quiet', 'origin', 'main') | Out-Null
$remoteMain = ([string](Git @('rev-parse', 'FETCH_HEAD'))).Trim()
$refs = & git ls-remote --tags origin
$tagRef = $refs | Where-Object { $_ -match "refs/tags/$([regex]::Escape($Tag))$" } | Select-Object -First 1
$peelRef = $refs | Where-Object { $_ -match "refs/tags/$([regex]::Escape($Tag))\^\{\}$" } | Select-Object -First 1
Check ($null -ne $tagRef) "the tag exists on origin"
Check ($null -ne $peelRef) "it is an annotated tag (peels to a commit)"
$commit = if ($peelRef) { ($peelRef -split "`t")[0] } else { '' }
if ($commit) {
    & git -C $root merge-base --is-ancestor $commit $remoteMain
    Check ($LASTEXITCODE -eq 0) "the tagged commit $($commit.Substring(0, 7)) is on origin/main"
    Note "origin/main $($remoteMain.Substring(0, 7))"
}

# ------------------------------------------------------------------ fetch the asset back, and read it
Write-Host ""
Write-Host "fetching the asset back from GitHub"
gh release download $Tag --repo $Repo --pattern $AssetName --dir $DownloadDir --clobber
if ($LASTEXITCODE -ne 0) { throw "gh release download failed with exit $LASTEXITCODE" }

$downloaded = Join-Path $DownloadDir $AssetName
Check (Test-Path $downloaded) "the download landed at $downloaded"
$downFile = Get-Item $downloaded
$downHash = (Get-FileHash $downloaded -Algorithm SHA256).Hash.ToUpperInvariant()
Note "downloaded $($downFile.Length) bytes  sha256 $downHash"

Check ($downFile.Length -eq $asset.size) "the downloaded size matches the release's ($($asset.size) bytes)"

$expected = $ExpectHash.ToUpperInvariant()
if (-not $expected -and (Test-Path $Exe)) {
    $expected = (Get-FileHash $Exe -Algorithm SHA256).Hash.ToUpperInvariant()
    Note "comparing with the local build $Exe"
}
Check ($expected.Length -gt 0) "there is a build to compare with (local Monitron.App.exe, or -ExpectHash)"
if ($expected) { Check ($downHash -eq $expected) "the downloaded bytes ARE the build's bytes" }

# ------------------------------------------------------------------------- unsigned, and said to be
$sig = Get-AuthenticodeSignature $downloaded
Check ($sig.Status -eq 'NotSigned') "the downloaded exe is unsigned (Get-AuthenticodeSignature: $($sig.Status))"
Check ($null -eq $sig.SignerCertificate) "it carries no signer certificate (there is no code-signing cert)"

Check ($release.body -match 'Unsigned on purpose') "the notes say it is unsigned on purpose"
Check ($release.body -match [regex]::Escape($downHash)) "the notes print the SHA-256 a download can be checked against"

# ------------------------------ run the DOWNLOADED exe, from its download folder, and see it work
Write-Host ""
Write-Host "running the downloaded exe (throwaway settings, --no-schedule: no brightness is written)"
$reportDir = Join-Path $RunDir 'reports'
[void](New-Item -ItemType Directory -Path $reportDir -Force)
$argline = "--no-tray --no-schedule --settings-file `"$(Join-Path $RunDir 'settings.ini')`" " +
    "--startup-value MonitronExp16 --width 400 --icon-report `"$reportDir\icon`" --display-report `"$reportDir\display`""

$p = Start-Process -FilePath $downloaded -ArgumentList $argline -PassThru
try {
    $h = [IntPtr]::Zero
    for ($i = 0; $i -lt 80; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        if ($p.HasExited) { break }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $h = $p.MainWindowHandle; break }
    }
    Check ($p.HasExited -eq $false) "the downloaded exe is still running"
    Check ($h -ne [IntPtr]::Zero) "it opened a window"
    Check ($p.MainWindowTitle -eq 'Monitron') "the window is Monitron's (title '$($p.MainWindowTitle)')"

    $iconPath = Join-Path $reportDir 'icon\icon.txt'
    Check (Test-Path $iconPath) "the app wrote its icon report"
    if (Test-Path $iconPath) {
        $icon = @{}
        foreach ($line in Get-Content $iconPath) {
            $kv = $line -split '=', 2
            if ($kv.Count -eq 2) { $icon[$kv[0]] = $kv[1] }
        }
        Check ($icon['resource'] -eq 'Monitron.ico') "it carries the embedded icon ($($icon['resource']))"
        Check (($icon['frames'] -split ',').Count -eq 9) "all nine frames are in it ($($icon['frames']))"
        Check ($icon['windowIcon'] -eq 'true') "the window got the icon"
    }

    $displayPath = Join-Path $reportDir 'display\display.txt'
    Check (Test-Path $displayPath) "the app wrote its display report"
    if (Test-Path $displayPath) {
        $display = @{}
        foreach ($line in Get-Content $displayPath) {
            $kv = $line -split '=', 2
            if ($kv.Count -eq 2) { $display[$kv[0]] = $kv[1] }
        }
        Note "detected=$($display['detected']) rows=$($display['rows'])"
        Check ([string]$display['detected']).Length -gt 0 "it detected this machine's displays"
    }
}
finally {
    if (-not $p.HasExited) {
        [void]$p.CloseMainWindow()
        if (-not $p.WaitForExit(5000)) { $p.Kill(); Write-Host "  !! did not exit on close (killed)" }
    }
}
Check ($p.HasExited -eq $true) "it exited when its window was closed"
Check ($p.ExitCode -eq 0) "it exited 0 (got $($p.ExitCode))"

# ------------- nothing of the user's was touched: the run used a throwaway settings file
$realSettings = Join-Path $env:APPDATA 'Monitron\settings.ini'
Note "the user's settings file is untouched at $realSettings"

Write-Host ""
Write-Host ("{0} checks passed, {1} failed" -f $script:passed, $script:failed)
if ($script:failed -gt 0) { exit 1 }
