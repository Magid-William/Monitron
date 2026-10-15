# EXP16 - The download: an unsigned release of the exe, proven by fetching it back

## Summary
Monitron has no release and no download: getting it means building it. Tag `v1.0.0` on `main`, attach
the exe built from that tag to a GitHub Release, and fetch it back to prove a download runs with
nothing installed - unsigned, so SmartScreen warns.

## Hypothesis
A GitHub Release whose one asset is the exe built from the tagged commit *is* a complete
distribution of Monitron: the tag resolves to a commit on `main`, the asset is byte-identical to that
build, and the downloaded exe runs on this machine with nothing installed beside it (it uses the
in-box .NET Framework 4.8). It carries no Authenticode signature - unsigned on purpose - so the only
barrier to a first run is SmartScreen's *"More info" -> "Run anyway"*.

Falsified by: the tag not resolving to the released commit, or the asset's hash differing from the
build's (the release is not the build); the downloaded file refusing to start on this machine (a
missing dependency - not self-sufficient); an Authenticode signature appearing (it would not be
unsigned); or the release being unreachable or not the one a visitor is offered.

## Methodology & blast radius
- Method: a release is the app's *outside* act, so it gets what every other claim here gets - one
  script that does it (`release.ps1`) and one outside witness that checks what it did
  (`verify-release.ps1`), both beside `build.ps1`.
  - `release.ps1`: refuses unless the branch is `main` with a clean tree, builds with `build.ps1`,
    hashes the exe, tags `v1.0.0` (annotated) and pushes the tag, then `gh release create` with the
    exe as its one asset and the notes written by the script (features, requirements, no installer,
    unsigned + the SmartScreen steps, and the SHA-256). `-DryRun` prints all of it and publishes
    nothing.
  - `verify-release.ps1`: `gh release view`/`gh api` read the release back (not a draft, not a
    prerelease, Latest, one asset of the expected size); `git ls-remote` peels the tag and the peeled
    commit must be on `origin/main`; the asset is downloaded into a temp folder and its SHA-256 must
    equal the local build's own hash (read from the exe beside the script - not passed in);
    `Get-AuthenticodeSignature` must be `NotSigned` (the "unsigned" claim asserted rather than
    described); and finally the **downloaded** exe is run out of its download folder with a throwaway
    settings file and `--no-tray --no-schedule`, asserting its window appears, the app's own
    `--icon-report` names the embedded icon, and it exits 0.
  - Version `1.0.0` is `app.manifest`'s `assemblyIdentity version="1.0.0.0"`; the exe is published as
    `Monitron.exe` because the app never reads its own file name (`AutoStart` derives it from the
    running path).
- May touch: `src/Monitron.App/release.ps1`, `src/Monitron.App/verify-release.ps1`, `README.md`
  (Getting it: the download), `experiments/EXP16/README.md`, `experiments-overview.md` - and, outside
  the repo, **the tag `v1.0.0` on `origin`, a public GitHub Release on `Magid-William/Monitron`, and
  `main`**, which is fast-forwarded onto this branch so the tag sits on the released line.
- Off-limits: the app's own sources - this experiment adds no code to the product; any code-signing
  certificate (there is none, and that is the point); the user's real settings file, autostart entry
  and displays (the verifier passes a throwaway settings file, a throwaway Run value name and
  `--no-schedule`, so no brightness is written and no autostart is changed).

## Open questions (known unknowns)
- [x] The version and tag - `v1.0.0`, matching the manifest's `assemblyIdentity`, since this is the
      project's first release.
- [x] The asset's shape - the bare exe, published as `Monitron.exe`. A zip was the alternative;
      rejected because "nothing installed alongside it" is exactly a single program you can run.
- [x] Does `main` move? - yes, fast-forwarded onto this branch before tagging, so "the tag is on the
      released line" is true at publish time and can be asserted.
- [x] Unsigned - yes, on purpose. No certificate is bought or used; the fact is asserted
      (`NotSigned`) and stated in the notes with the SmartScreen steps.
- [x] Prerelease? - no; the first release is a full one and is marked Latest.
- [x] Verified on what? - on the **published artifact**, downloaded back from GitHub through the same
      endpoint a visitor uses, not on the local build.

## Conclusion (findings)
`success` (user-declared). Monitron has a download. **v1.0.0** is a GitHub Release on
`Magid-William/Monitron` - tag `v1.0.0` at commit `54e0145` on `main`, marked **Latest**, carrying one
asset: `Monitron.exe`, 254,464 bytes, unsigned (`NotSigned`, no signer certificate), with notes that
print the SHA-256 and the SmartScreen step. `README.md`'s *Getting it* now sends a visitor to that
page instead of to a build command. Two scripts do it and check it: `release.ps1` (refuses off `main`
or on a dirty tree, builds, hashes, tags, pushes and publishes; `-DryRun` publishes nothing) and
`verify-release.ps1` (reads the release back, downloads the asset, drives it).

**Run 2026-10-16.** The nine headless suites first - **433 assertions, 0 failed** - then
`release.ps1 -DryRun`, then the real run, and then the witness: **`verify-release.ps1` 29 checks,
0 failed**, all of it against the *published* artifact rather than the local build.
- the releases API says it is Latest; `gh release view` says not a draft, not a prerelease, exactly
  one asset, named `Monitron.exe`;
- the annotated tag peels to `54e0145`, which is on `origin/main`;
- `gh release download` brought the asset back at 254,464 bytes, and its SHA-256
  (`3ECA95A1...AFE3`) equals the local build's own hash - the published bytes **are** the build's;
- `Get-AuthenticodeSignature` is `NotSigned` with no signer certificate, and the notes say so and
  print the same hash;
- the **downloaded** exe, run out of its download folder with a throwaway settings file and
  `--no-schedule`, opened its window (`Monitron`), read all nine frames out of its embedded
  `Monitron.ico` (16,20,24,32,40,48,64,128,256), detected this machine's display read-only (the AOC,
  `cur=32`) and exited 0.

Nothing of the user's was touched: the run used a throwaway settings file and a throwaway Run value
name, the real `%APPDATA%\Monitron\settings.ini` kept its byte-for-byte size and timestamp, and the
read-only probe read the same 32 before and after.

**Boundaries.** The tag names `54e0145`; the one commit after it (the verifier's Latest fix and the
Learnings above) touches no app source, so `main` sits one commit past the release and GitHub says so
on the release page. The release is unsigned by decision, not by omission - a certificate is a
purchase and an identity, and the claim here is that the download is complete without one - and
**SmartScreen's first-run warning is the one thing not witnessed**: `gh release download` attaches no
`Zone.Identifier` (checked: the file has only `:$DATA`), so a downloaded file on this machine never
wore the mark that warning keys on, let alone on another Windows version. Nor is the `--hidden`
autostart path a downloaded user would switch on afterwards. Nothing here signs anything.

## Learnings
- Never name a wrapper after the command it runs: PowerShell is case-insensitive, so `function Git`
  shadows native `git` and `& git` inside it recursed forever (`git rev-parse`, thousands a second).
  Use `GitRun` - the same reason each verifier names its `Add-Type` class per experiment.
- A function that emits nothing leaves a call-site `[string]` cast with AutomationNull, not `""`: a
  clean `git status` broke the next `.Trim()`. Join inside the wrapper: `return ($out -join "`n")`.
- Print before the slow step. `release.ps1` checked and built in silence, so a three-minute dry run
  looked exactly like an infinite loop. `-DryRun` on the released branch first: it caught the
  recursion and, next time, the uncommitted-tree guard.
- `gh release view --json` has no `isLatest` (gh 2.86); "Latest" is
  `gh api repos/<owner>/<repo>/releases/latest --jq .tag_name`.
- Verify a release on the **published asset**: `gh release download` through the endpoint a browser
  uses, hash-compare it with the build, and run it. `Get-AuthenticodeSignature` turns "unsigned" into
  an assertion (`NotSigned`). `gh`'s download adds no `Zone.Identifier`, so SmartScreen's first-run
  warning cannot be reproduced that way.
- Resolve paths from `$PSCommandPath` (as `build.ps1` does) so the scripts run from the repo root and
  from `src\Monitron.App\` alike.
