# EXP13 - The slider knob: a click lands where you clicked

## Summary
Clicking a row's slider track should put the knob on the clicked level instead of nudging it one
large step, and the row's slider should be named for a screen reader and show where keyboard focus is.

## Hypothesis
A click anywhere on a row's slider track moves the thumb to the clicked position (snapped to the
nearest whole level, clamped at the ends) and the display itself ends up at that level - instead of
the OS slider's +/-LargeChange nudge - so a row is settable without hitting or dragging the small
thumb. And the row's slider carries a name a screen reader can read, and shows where keyboard focus is.

Falsified by: a click that lands on the click point but also adds +/-10; a press on the thumb that no
longer drags; a value that moves while the rendered thumb does not follow it; an empty slider name;
or focusing the slider that changes no pixel.

## Methodology & blast radius
- Method: the jump is WPF's own move-to-point, switched on for the row's slider
  (`IsMoveToPointEnabled = true`) - the OS slider already snaps to the tick, clamps at the ends and
  leaves a press on the thumb to the thumb. The slider gets an accessible name like the gear beside
  it. The focus cue was to be WPF's own `FocusVisualStyle` (an accent ring around the slider); it was
  written, measured, and dropped - see the Conclusion - and replaced by the row's own edge, set where
  the row is built. The claim is a UI claim, so it is verified by driving: a real, guarded mouse click
  on the track (foreground attempted first, the click point asserted to be inside the slider's own
  UIA rectangle, and the press only sent where `WindowFromPoint` says this app's window would receive
  it, with the cursor restored), witnessed from outside the app - the rendered pixels at the filled
  track and at the thumb's own centre, the row's value, the display's own reading (the EXP02 probe
  over DDC/CI), a thumb drag that must still work, and the facts a screen reader sees.
- May touch: `src/Monitron.App/Program.cs` (the row's slider and the row's edge), `Theming.cs` (a note
  only, in the end), a new `src/Monitron.App/verify-slider.ps1`, the EXP13 row in
  `experiments-overview.md`, and the header comments naming this experiment.
- Off-limits: the writer/debounce, the scheduler, settings/persistence, placement, the tray, the
  icon, `build.ps1` and every existing verifier - all untouched.

## Open questions (known unknowns)
- [x] How much of the a11y story this experiment carries - click-to-position, the accessible name,
      and the focus cue. (User's call.)
- [x] Whether a verifier may synthesize a real mouse click - yes, guarded as in Method above. (User's
      call.) The claim is about real mouse input, and pixels are its only honest witness.
- [x] Snapped to the tick rather than the raw click point, clamped at the ends - which is what WPF's
      `Track.ValueFromPoint` does.
- [x] The track click no longer nudges by +/-10. Measured, not assumed: the baseline run below shows
      the old behaviour *was* that nudge.
- [x] The thumb's drawn size is left alone - the jump removes the need to hit it.
- [x] Which fallback, had the one property not held: it held, so no fallback was needed and nothing
      was added to `StyleSlider`.
- [x] Out of scope: a stored a11y preference, an announced live region, driving the keys themselves
      (the standing rule sends no keystrokes), touch/flick, and the settings window.

## Conclusion (findings)
`success` (user-declared). A click on a row's track lands the knob on the click and drives the display
there, and the row is named and shows where the keyboard is. **The whole app change is three things in
`Program.cs`**: `IsMoveToPointEnabled = true` (one line), `AutomationProperties.SetName(slider,
"<display> brightness")` (one line), and the row's edge taking the accent while its slider has the
keyboard (nine lines, comment included). No other app file changed; `Theming.cs` carries a note that
says why the focus ring is **not** in it.

**Run 2026-10-15.** `verify-slider.ps1`, the same verifier and the same click point (x=2389 on a
0..100 track sitting at 41), against two builds:
- **EXP12's build as the baseline** (its `Program.cs` and `Theming.cs`, copied into a temp folder and
  built there, so the repo tree was never touched): the click put the value at **51** - 41 +
  `LargeChange`, the nudge - and the thumb drew **113.5 px** from the click; the probe saw 41 -> 51;
  the accessible name was `''`; the row's edge never took the accent. **24 checks passed, 6 failed**,
  and the six are exactly the ones this experiment is about.
- **This build:** value **85**, thumb centre **0.5 px** from the click, probe 41 -> 85, name
  `Q27G3G3R3 brightness`, and the accent appears under the focused row (349 px of the band under the
  slider) and is gone when the keyboard leaves (0 px). **30 checks passed, 0 failed.** A thumb drag
  still works - 41 -> 55 in *both* builds - so the jump did not disturb the drag. Every display was
  left at its 41 and the user's real settings file was untouched.

**The focus cue is the finding, and it is why this experiment is shorter than it might have been.**
The first version was the WPF-intended one: a `FocusVisualStyle` accent ring around the slider. It
works - forced on, it drew 2233 px - but **Windows draws it only for a user who has just typed**:
`KeyboardNavigation.AlwaysShowFocusVisual` is a static field latched from
`SystemParameters.KeyboardCues` when the process starts (source: `KeyboardNavigation.cs`), and that
setting is `False` on a machine driven by the mouse, so the ring is invisible in the common case. It
also cannot be verified without altering the machine: the flag can be turned on before the app starts
(the run did that, and the ring appeared), but `SystemParametersInfo(SPI_SETKEYBOARDCUES)` with a
`FALSE` value reads back as still-true - only `pvParam = NULL` with `SPIF_SENDCHANGE` hides them
again. With the flag on, Windows' own dotted focus rectangle *does* show over the slider
(`focus-on.png` in the old run caught both), which is the gate seen from the other side. So the ring
was reverted and the cue that ships is ours and unconditional: **the row's edge takes the accent
whenever that row's slider has the keyboard** - including when the track was clicked, which is the
deliberate difference from Windows' convention, and the point of shipping our own.

Boundaries: this machine has **one** active target (the AOC), so the internal-panel branch of the
verifier was not exercised - the panel has been gone since EXP10, and `-SkipInternal` is a no-op here;
`verify-slider.ps1` handles both anyway. The jump is WPF's rule, exercised only through a real click
(no keystroke is synthesized anywhere, so "Arrow moves by 1" is asserted as what UI Automation
reports - `SmallChange=1`, `LargeChange=10` - not by pressing it). The cue is asserted on the light
palette only (the run pins `--theme light` so the accent and the unfilled track are known colours).
The app is driven with `--no-schedule --no-tray`, so nothing here speaks to the tray or the night;
the verifier writes the display through the same debounce a drag uses, and it drives the machine's
real cursors and monitors. The standing `*-test.ps1` and `verify-*.ps1` suites were **not** re-run:
this change touches one file's slider and one border, no model and no `build.ps1`, and the user asked
to conclude without further changes (EXP12 set the same precedent).

**Post-conclusion change (user's ask, same experiment).** The hint paragraph that sat under the
schedule status - *"Drag a slider to set a monitor. The night-light schedule dims every display to its
strength at the time you set, and gives it back afterwards. Settings are saved and come back next
launch."* - was removed from `Program.cs`. It was the only user-facing text that started with "Drag",
and it lived directly under the `NightLightStatus` line. Nothing else changed; no verifier reads it
(the standing `*-test.ps1` and `verify-*.ps1` suites name neither the string nor its block), so the
30/0 slider result above stands. The user judged it did not warrant its own experiment, so it is
recorded here.

**Post-conclusion change 2 (user's ask, same experiment): the product is renamed `Monitorn` ->
`Monitron`.** Everything but the checkout folder changed: the window, tray tooltip and settings
titles, the surface heading, the exe (`Monitron.App.exe`), the source folder (`src/Monitron.App/`),
the icon (`Monitron.ico`), the namespace (`Monitron.App`), the per-user settings folder
(`%APPDATA%\Monitron\settings.ini`), the Run registry value, the build and verify scripts and their
title assertions, and the prose in `README.md`, `AGENTS.md`, `docs/prior-art/` and **every**
experiment record. The checkout folder keeps its name - renaming it is outside git and would move the
working directory. The machine side was migrated on this box so the
running app kept its schedule: `settings.ini` copied into the new folder, and the `Monitorn` Run
value replaced by `Monitron` pointing at the new exe (the old one had gone stale with the folder).
Verified: a fresh `build.ps1`, all **eight** headless suites green (**394 assertions, 0 failed**),
and the live window read through UI Automation says `Monitron` with the schedule applied
(*"Night light: 32% until 9:00 AM"*). The end-to-end `verify-*.ps1` scripts were not re-run - the
change is names and paths only, no behaviour. Two honest costs, both accepted: the past records now
show the new name in paths that were run under the old one (e.g. a `git show EXP12:src/Monitron.App/...`
baseline that then lived at `src/Monitorn.App/`), and a rename like this orphans the old
`%APPDATA%` folder and the old Run value unless they are migrated by hand.

**Post-conclusion change 3 (user's ask, same experiment): the app is published.** `README.md` was
rewritten from its "Pre-implementation" scaffold into a landing page (features, the build command,
and how the experiment log works), an **MIT** `LICENSE` was added, and the author's local checkout
path was taken out of `AGENTS.md` and the experiment records (they now say "the repository root").
The repository is public at **https://github.com/Magid-William/Monitron**: `main` fast-forwarded to
this branch, every experiment branch pushed, MIT, topics set. No app code changed, so nothing was
rebuilt and no suite was re-run - the rename's 394/0 stands. The README was then re-framed for a
general audience at the user's ask: it leads with what the app does for the reader and moves the
build and verification detail into a short "For developers" section - the "no SDK" line is a build
fact, not a selling point, and a casual reader has no use for it.

## Learnings
- A repo-wide product rename: a byte-level `String.Replace` over every text file (a Latin1 round-trip
  keeps the bytes exact; these files have no BOM), with the checkout path protected first
  (`Apps\Monitorn` -> a placeholder -> back). 64 files, a symmetric 287/287 lines. Renaming the
  source folder **works while its exe is running**; only the `build.ps1` output needs the app quit.
  Use when a name changes across code, scripts and docs at once.
- A rename that moves machine identity has two side effects to migrate by hand: `%APPDATA%\<old>`
  (the settings file) and the `HKCU\...\Run` value named after the old name (which points at the
  now-missing exe). Neither is touched by the app; do both, or the user silently loses the schedule
  and keeps a broken autostart entry.
- Publishing an existing local repo: `gh repo create <owner>/<name> --public --source=. --remote=origin
  --description "..."`, then `git push -u origin --all`, then `gh repo edit <owner>/<name>
  --default-branch <b> --add-topic ...`. `--source` sets `origin` for you; `--push` would only send
  one branch, so push `--all` yourself. `gh auth status` can print a stale account label - trust
  `gh api user --jq .login` for the real owner.
- `IsMoveToPointEnabled = true` on a slider with the OS template: a track click lands on the press,
  snapped to the tick and clamped, and a press on the thumb is still left to the drag. One line; no
  template surgery. Use when a slider must be settable without hitting the thumb.
- A WPF `FocusVisualStyle` is decided **at process start**: `KeyboardNavigation.AlwaysShowFocusVisual`
  is latched from `SystemParameters.KeyboardCues`, and it is only drawn when that is true *or* the
  keyboard is the most recent input device. Never verify one from outside, and never ship one as the
  only cue - for a mouse user it is invisible.
- `SystemParametersInfo(SPI_SETKEYBOARDCUES, 0, NULL, SPIF_SENDCHANGE)` hides the keyboard cues;
  the same call with a `ref bool` FALSE reads back as still-true. Restore with the `NULL` form.
- A baseline without touching the tree: `git show EXP12:src/Monitron.App/Program.cs`, written over a
  *copy* of `src\Monitron.App` in temp, then that copy's `build.ps1`, then
  `verify-slider.ps1 -Exe <that exe>`. Use when a change needs a measured "before".
- A synthesized press goes where `WindowFromPoint` says: the app does **not** need to be foreground
  (mouse input follows the cursor - only the keyboard needs it), and `SetFocus()` on the window element
  does not move the keyboard off a control - focus a sibling (the gear) instead.
- PowerShell aliases shadow functions: `Diff` resolves to `Compare-Object`, so `function Diff` is
  silently ignored. Name helpers uniquely (`FrameDiff`).
- The app locks its own `Monitron.App.exe` while running, so `build.ps1` fails with CS0016 until it is
  quit (tray -> Quit, which restores the displays; a kill does not). Check first:
  `Get-CimInstance Win32_Process -Filter "Name='Monitron.App.exe'"` shows the command line, which tells
  a probe's leftover instance (seam arguments, nothing to restore) from the user's own launching.
