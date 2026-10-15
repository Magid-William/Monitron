# Publishes a Monitron release: the exe built from this tree, tagged, and attached to a GitHub
# Release. EXP16.
#
# The release is UNSIGNED on purpose - there is no Authenticode certificate - so Windows SmartScreen
# shows "Windows protected your PC" on a first run; the notes say so, and give the SHA-256 a download
# can be checked against. Nothing here signs anything.
#
# A release is cut from `main`, so this script refuses anywhere else and refuses a dirty tree: the
# tag must name the commit the exe was built from.
#
#   .\release.ps1 -DryRun     # build, hash, and print what would be published. Publishes nothing.
#   .\release.ps1             # tag v1.0.0 and publish it (needs a logged-in gh with push access)
#   .\release.ps1 -Version 1.1.0
#
# The exe is published as Monitron.exe, not Monitron.App.exe: the app never reads its own file name
# (AutoStart derives it from the running path), and a download should say what it is.
param(
    [string]$Version = '1.0.0',
    [string]$Repo = 'Magid-William/Monitron',
    [string]$AssetName = 'Monitron.exe',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $PSCommandPath
$root = Split-Path -Parent (Split-Path -Parent $here)
$tag = "v$Version"
$exe = Join-Path $here 'Monitron.App.exe'

function Git([string[]]$argv) {
    $out = & git -C $root @argv
    if ($LASTEXITCODE -ne 0) { throw "git $($argv -join ' ') failed with exit $LASTEXITCODE" }
    return $out
}

# --- the released line and a clean tree: the tag has to name the commit the exe was built from ---
$branch = [string](Git @('rev-parse', '--abbrev-ref', 'HEAD'))
if ($branch.Trim() -ne 'main') {
    throw "a release is cut from main, not '$($branch.Trim())'. Fast-forward main onto this branch first."
}
$dirty = [string](Git @('status', '--porcelain', '--untracked-files=no'))
if ($dirty.Trim().Length -gt 0) {
    throw "the tree has uncommitted changes; a release must be the commit `git` records:`n" + $dirty
}
Git @('fetch', '--quiet', 'origin', 'main') | Out-Null
$behind = [string](Git @('rev-list', '--count', 'main..FETCH_HEAD'))
if ($behind.Trim() -ne '0') {
    throw "main is $($behind.Trim()) commit(s) behind origin/main - push it before releasing."
}
$commit = [string](Git @('rev-parse', '--short', 'HEAD'))
$commit = $commit.Trim()

# --- the tag: one release per version, and it must be the one that was just pushed ---
$existing = [string](Git @('ls-remote', '--tags', 'origin', "refs/tags/$tag"))
if ($DryRun -eq $false -and $existing.Trim().Length -gt 0) {
    throw "$tag already exists on origin - a version is released once. Pick the next one."
}

# --- the build: the exe the tag will carry ---
& (Join-Path $here 'build.ps1')
if (-not (Test-Path $exe)) { throw "build.ps1 did not produce $exe" }

$file = Get-Item $exe
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToUpperInvariant()
$bytes = $file.Length

# --- stage the asset under the name a download should have ---
$stage = Join-Path (Join-Path $env:TEMP 'monitron-release') $tag
if (-not (Test-Path $stage)) { [void](New-Item -ItemType Directory -Path $stage -Force) }
$asset = Join-Path $stage $AssetName
Copy-Item $exe $asset -Force

$notes = @"
Monitron $Version - brightness for every monitor, and a night light that dims them for you.

Monitron sits in your notification area, next to the clock. Each screen gets its own brightness
slider, and it can dim them all on a schedule: fade down in the evening, hold the low level
overnight, and put every screen back the way it was in the morning.

**Download:** ``$AssetName`` - one program. No installer, nothing to install alongside it.

**Requirements:** Windows 10 or 11, 64-bit. It uses the .NET Framework 4.8 that Windows already has.

**Unsigned on purpose.** This build carries no code-signing certificate, so Windows SmartScreen may
say *"Windows protected your PC"* the first time you run it. Choose **More info** -> **Run anyway**.
Check the file is the one this release built:

    Get-FileHash .\$AssetName -Algorithm SHA256

    SHA-256  $hash
    Size     $bytes bytes

Built from the tag ``$tag`` (commit ``$commit``).

**What it does**
- A slider for every screen - external monitors over DDC/CI, laptop panels over the WMI brightness API.
- A night light that remembers: pick the hours and the strength, and each screen is handed back
  exactly the brightness it had before.
- Out of the way: close the window and it keeps running from the tray, with *Run on Windows startup*.
- Follows your Windows light or dark theme, and carries its own icon.
"@

$notesFile = Join-Path $stage 'notes.md'
Set-Content -Path $notesFile -Value $notes -Encoding UTF8

Write-Host ""
Write-Host "release   $tag  (Monitron $Version)"
Write-Host "repo      $Repo"
Write-Host "commit    $commit  on $($branch.Trim())"
Write-Host "asset     $AssetName  $bytes bytes"
Write-Host "sha256    $hash"
Write-Host "notes     $notesFile"
Write-Host ""

if ($DryRun) {
    Write-Host "DRY RUN - nothing tagged, nothing pushed, nothing published."
    return
}

# --- tag and push, so the release names a commit that is already on origin ---
Git @('tag', '-a', $tag, '-m', "Monitron $Version") | Out-Null
Git @('push', 'origin', $tag) | Out-Null
Write-Host "pushed tag $tag"

# --- publish. --verify-tag: the release must point at the tag that was just pushed ---
gh release create $tag $asset --repo $Repo --title "Monitron $Version" --notes-file $notesFile --verify-tag --latest

if ($LASTEXITCODE -ne 0) { throw "gh release create failed with exit $LASTEXITCODE" }

$url = [string](gh release view $tag --repo $Repo --json url --jq .url)
Write-Host "published $($url.Trim())"
Write-Host "now: .\verify-release.ps1 -ExpectHash $hash"
