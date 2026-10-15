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
_Pending._

## Learnings
