# EXP10 - Raise the brightness regressions to N displays

## Summary
`verify-control.ps1` and `verify-schedule.ps1` assert "exactly one row", so on a machine with two
active targets they drive nothing: the brightness path and the scripted night have no working
end-to-end regression. Find each row, bind it to its own display, drive it, and assert it against an
**outside** witness - the DDC probe for an external row, a separate WMI read for an internal panel.

## Hypothesis
The surface and the schedule are already per display; only the verifiers assumed one row. With N
active targets every row is bound to its own display and drivable against its own witness, and the
scripted night still fades, holds, per-display overrides and gives **every** display's own day value
back.

**Falsified if:** the row count is not the active-target count; a row cannot be bound to a specific
display; driving one row does not reach that display, or disturbs another row; the scripted night
misses or double-writes a display; a manual move on one row ends the schedule for another, or is
undone for its own; either verifier still cannot run to completion here; or a run leaves any display
off its original value or touches the user's real `%APPDATA%\Monitorn\settings.ini`.

## Methodology & blast radius
- Method:
  1. **Row identity - the one product seam.** `Program.cs` `Row()` sets
     `AutomationProperties.SetAutomationId` on the slider *and* the gear to `monitor.Id`, which is
     already `DDC:<device path>` or `WMI:<InstanceName>`. The verifier therefore learns each row's
     kind, and for a panel the exact WMI instance, without guessing by position or value - the same
     pattern as the status line's `NightLightStatus` id and the gear's `Name`.
  2. **`verify-control.ps1`.** Assert `sliders.Count` **equals the probe's own `active paths`** (and
     is at least 1) instead of `-eq 1`; report the app's count when they disagree. Discover the rows
     by automation id and split them by the `DDC:` / `WMI:` prefix. Drive **every** row's slider away
     and back through `RangeValuePattern`, each against its own witness:
     - **DDC row** - the read-only EXP02 probe (`brightness cur=`), parsed per monitor block so the
       witness is read next to its own device; which readable display moved is asserted, and that
       the others did not;
     - **WMI row** - an independent read-only `Get-CimInstance -Namespace root\wmi
       -ClassName WmiMonitorBrightness` for the row's own `InstanceName`; the panel has no DDC
       witness, and the app's own text would be the app agreeing with itself;
     - the row's own value is read as its slider's `RangeValuePattern.Value`, not as a global
       `Texts -contains`, so an N-row window cannot satisfy the check from the wrong row.
     One gear per row is asserted, and the settings window is opened from the first row's gear.
     `-SkipInternal` is the escape hatch that leaves the panel's row undriven.
  3. **`verify-schedule.ps1`.** The same row discovery. The scripted night stays **probe-witnessed on
     the DDC row** (fade staircase, hold, override, turn-off), and the panel row is witnessed through
     the CIM read for the same phases. The N case asserts something a single row could not: after a
     manual move on the DDC row, that row keeps the user's value **while the panel row is still held
     at its strength** - overrides are per display.
  4. **Budgets kept.** Both scripts keep their shape: `--no-schedule` for the control pass, a temp
     `--settings-file`, shots and reports under `%TEMP%\monitorn-exp10\`, the real settings file's
     content hash asserted unchanged whether or not it exists, and - new - the assertion that every
     display is back at its original value, DDC by probe and panel by CIM.
  5. **Tests / regressions.** The relaxed end-to-end pair, plus `model-test` / `writer-test` /
     `schedule-test` / `persistence-test` / `theme-test` / `position-test` and `verify-settings` /
     `verify-persistence` / `verify-theme` / `verify-position`.
- May touch: branch `EXP10` off `EXP09` (tip `d3fc481`); `experiments/EXP10/README.md`; the overview
  row; `src/Monitorn.App/verify-control.ps1`, `verify-schedule.ps1`, and `Program.cs` (the two
  automation-id lines only).
- Off-limits: the schedule machine and its timings; the placement; the theme; the `settings.ini`
  format; the app's visible behaviour; the tray icon, autostart, z-order and activation (the
  standing next step, now EXP11); per-monitor profiles; the registry is **read only**; anything
  outside `D:\Playground\Projects\Apps\Monitorn\`; downloads.
- Escalation budget: every run pins a temp settings file and writes into `%TEMP%\monitorn-exp10\`;
  both displays are restored and asserted back at their original values, and the user's real
  `settings.ini` is asserted unchanged. **New since EXP09: the runs deliberately write to the
  internal panel too**, so the user's own screen dims briefly during a run (the user's call, taken at
  plan time).

## Open questions (answered at plan time)
- [x] **Is EXP10 this relaxation, or the tray shell?** - **the user's call**: the relaxation. EXP09
      named it first, and every later experiment leans on the regressions it restores. The tray shell
      + autostart becomes EXP11.
- [x] **The `AutomationId` seam on each row** - **the user's call**: yes, one line in `Program.cs`.
      Rows are indistinguishable to UI Automation today (only the gear's `Name` differs), and
      matching by position or value is the guessing that produced EXP09's bogus "taskbar" window.
- [x] **Witness the internal panel at all?** - **the user's call**: yes, `Get-CimInstance`
      (read-only, separate process, different API surface - the outside-witness role the probe plays
      for DDC), and drive that row away and back. The panel now exists on this machine and the
      scripted night already writes to it, so asserting its restore beats assuming it.
- [x] **A second DDC display** - count-matched: `readable DDC displays == DDC rows`, and a drive may
      change exactly one readable value. Real device->row matching waits for a machine that needs it.
      *Assumption.*
- [x] **Does the internal WMI write actually land here?** - assumed yes; a failure is reported as a
      finding, not worked around. *Assumption.*
- [x] **Factor the duplicated helper blocks into a shared file?** - no: keep the deliberate
      per-experiment duplication, minimal blast radius. *Assumption.*
- [x] **Evidence directory** - `%TEMP%\monitorn-exp10\`: the shots and both verifiers' console
      output. *Assumption.*

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11 25H2, build 26200; two displays, both at 125 %).

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitorn.App\build.ps1` | `Monitorn.App.exe` from the in-box `csc`, **0 MB** (`Program.cs`: the two automation-id lines only) |
| End to end | `verify-control.ps1` | **28 checks, 0 failed** - both rows bound, both driven away and back, each against its own witness; `-SkipInternal` also run: **22 checks, 0 failed** |
| End to end | `verify-schedule.ps1` | **35 checks, 0 failed** - a scripted night (strength 47) witnessed on both displays, per-display override, both restored |
| Regression | `verify-settings` / `verify-persistence` / `verify-theme` / `verify-position` | **30 / 27 / 42 / 60**, 0 failed |
| Regression | `model-test` / `writer-test` / `schedule-test` / `persistence-test` / `theme-test` / `position-test` | **38 / 19 / 114 / 42 / 34 / 63**, 0 failed |

The machine, as the two witnesses read it: the probe sees **2 active paths** and can read one of
them over DDC/CI - `\\.\DISPLAY1`, the AOC `Q27G3G3R3`, at **57**; the internal panel (EDID
`BOE0764`, which the app names "Display 1") reads **67** over WMI and is invisible to the probe.
Rows: `WMI:DISPLAY\BOE0764\4&29539ac7&0&UID265988_0` and
`DDC:\\?\DISPLAY#AOC2703#...#{e6f07b5f-...}`, each with its own gear (`Settings for Display 1`,
`Settings for Q27G3G3R3`).

Findings:
- **Both rows are driven, each against its own outside witness.** `verify-control` drove the
  **internal panel 67 -> 57 -> 67** with an independent `Get-CimInstance` read following, and the
  **AOC 57 -> 47 -> 57** with the probe following; driving the panel left the AOC at 57, and driving
  the AOC left the panel alone. This is also the first time the **WMI path has been exercised**: EXP05
  implemented it and recorded "not exercised (no internal *active* target on this machine)", and this
  machine now has one.
- **The scripted night holds on every display.** Strength 47 (from the AOC's 57): the probe saw the
  staircase **57 -> 56 -> 54 -> 52 -> 48 -> 47** while the panel went to 47 (independent WMI read),
  both held 47, and every row showed the level the schedule holds.
- **The override is per display - and that is new.** A manual move on the AOC's row put its own 57
  back *while the panel stayed held at 47*; the app kept saying "47% until 4:22 PM". A single-row run
  could not have observed this.
- **Turn-off gives each display its own value back, on its own ramp.** The overridden AOC kept 57
  (nothing written over it); the panel, which was not overridden, **ramped back up** - the first WMI
  read after turn-off saw 52, then 67 - and the close left AOC 57 / panel 67, with the user's real
  `settings.ini` unchanged (hash).
- **Two defects were found only by driving it.** `verify-schedule.ps1`'s fade loop reused `$p` as its
  loop variable, clobbering the process object: the gear check then searched pid 0, the app was never
  asked to close, and the run left a scripted night running on the desktop (the app was closed by
  hand, and both displays were left at 57 / 67). And the turn-off check for a **non-overridden**
  display cannot be an instant read: the panel is still climbing its ramp (52 of 47 -> 67) 1.5 s after
  the status changes, so it now polls for the baseline.

Boundaries this run does not cross:
- **The internal panel's staircase is not sampled.** A WMI read is slower than a fade step (0.33 s
  real at `--rate 30`), so the panel is asserted **at the night level and at the restore**, not step
  by step; the DDC probe carries the staircase claim. A slower `--rate` would allow sampling it.
- **The row <-> probe-device mapping is a count, not an identity.** The probe reports a device name
  per readable display but nothing that ties it to a row id, so with more than one DDC-readable
  display the override is asserted as "exactly one readable display now holds the user's value and
  the rest are still at the strength" rather than "the display this row drives". With one readable
  display - this machine - the mapping is unambiguous and asserted by count.
- **`rows == active paths` is this machine's fact.** Both active targets are controllable here, so
  equality is the right assertion; on a machine with an active-but-uncontrollable target, the row
  count legitimately differs and this one check names it instead of hiding it (the app gates a row on
  a readable monitor, `BrightnessController.Detect`).
- **Only two display kinds are witnessed.** A row that is neither `DDC:` nor `WMI:` fails by design
  ("is a display kind this verifier can witness") rather than passing silently.
- **Not the tray shell.** No icon, no autostart, no z-order or activation change, and the app's
  visible behaviour is untouched.
- **`verify.ps1` is stale**: its help still offers `--rows 5`, a seam EXP05 removed with the sample
  rows. Doc-only, and left alone here.

## Next experiment (suggested)
- The standing step, now with the corner it opens into proved by EXP09 and its regressions back:
  **the tray shell + autostart** (which also decides what closing does), then a stored `theme=`
  preference plus a switch, then real sunset/sunrise, then per-monitor profiles.
- If the internal panel is ever to be asserted as fully as the external one, a later experiment can
  sample its staircase too (a slower `--rate`), and `verify.ps1`'s stale `--rows` help line is a
  one-line cleanup nobody has owned.

## Conclusion (findings)
**`success`** (2026-10-15; the agent's recommendation, pending the user's declaration). The two
verifiers EXP09 left un-runnable now run to completion on this two-display machine and drive **every**
row against its **own** outside witness - so the brightness path and the scripted night have their
end-to-end regressions back, and they cover more than they did before the machine grew a second
display.

What this settles:
- **A row is bound to a display, not guessed at.** Each row's slider and gear carry `monitor.Id`
  (`DDC:<device path>` / `WMI:<InstanceName>`) as their UI Automation id; rows equal the probe's own
  `active paths` (2), one gear per row, and each row's value equals the value its own witness reads.
- **Every row is drivable against its own witness.** The internal panel `67 -> 57 -> 67` with an
  independent `Get-CimInstance` WMI read following, the AOC `57 -> 47 -> 57` with the probe following,
  and neither drive moved the other display. **The WMI path is exercised for the first time** - EXP05
  implemented it and recorded it as never exercised, which is now closed.
- **The schedule holds per display.** Night at strength 47: the AOC's staircase `57 -> 56 -> 54 -> 52
  -> 48 -> 47`, both displays at 47, a manual move on the AOC honoured **while the panel stayed held**
  (a one-row run could not have observed the per-display override), the overridden AOC left its 57
  and the panel ramped back to its own 67 at turn-off, and both at their originals after close.
- **Nothing else moved.** 28 / 35 end to end (22 with `-SkipInternal`) and 38 / 19 / 114 / 42 / 34 / 63
  headless, 30 / 27 / 42 / 60 end to end, all passing; every run left the real
  `%APPDATA%\Monitorn\settings.ini` untouched and every display at its own value.
- **It was worth relaxing rather than asserting once.** Driving it found the two defects the code did
  not show: a reused `$p` loop variable clobbered the process object (the app was never asked to
  close, and a scripted night was left running on the desktop), and a non-overridden display is
  mid-ramp at turn-off, so its restore needs a poll.
- **One learning from it was promoted into `AGENTS.md`, approved by the user** (the one promotion the
  workflow allows, so that budget is now spent): *verify once, when the experiment is done - not after
  every edit* - work the experiment to completion, then run the whole verification, fix and rerun as
  often as needed to go green.

What it deliberately does not settle - the boundary:
- **The panel's staircase is not sampled.** A WMI read is slower than a fade step (0.33 s real at
  `--rate 30`), so the panel is asserted at the night level and at the restore only; the probe carries
  the staircase claim.
- **A row is a count-matched witness, not an identified one.** The probe names the device a value came
  from but nothing ties it to a row id, so with two DDC-readable displays the override is asserted
  count-wise rather than per row. This machine has one, so the mapping is unambiguous here.
- **Two display kinds are witnessed, and only two.** A row that is neither `DDC:` nor `WMI:` fails by
  design instead of passing silently.
- **`rows == active paths` is this machine's fact**: both active targets are controllable. A target the
  app cannot control would legitimately differ, and that one check would name it.
- **Not the tray shell.** No icon, no autostart, no z-order or activation change - that is EXP11.
- **`verify.ps1` is still stale** (its help offers `--rows 5`, a seam EXP05 removed); left alone, as
  outside this experiment's radius.

Next, in the order the seams suggest: the overview's standing step, the **tray shell + autostart**
(EXP11 - it also decides what closing does), with the flyout corner EXP09 proved and the regressions
this experiment restored; then a stored `theme=` preference plus a switch, real sunset/sunrise, and
per-monitor profiles.

Evidence: the commands above; the console output of both verifiers and the shots in
`%TEMP%\monitorn-exp10\`.

## Learnings
- Witness a row without a device-level probe: `Get-CimInstance -Namespace root/wmi -ClassName
  WmiMonitorBrightness` (one `CurrentBrightness` per `InstanceName`) is an outside witness for an
  internal panel the DDC probe cannot see, and `Program.cs` puts `monitor.Id` on each slider as its UI
  Automation id, so `$slider.Current.AutomationId` yields both the kind (`DDC:` / `WMI:`) and the WMI
  instance to read. Use when a verifier must drive a row it cannot see over DDC/CI.
- Parse the probe per monitor block (`monitor[i] device=\\.\DISPLAYn`, then `brightness cur=`) into
  (device, value) pairs, never one `brightness cur=` regex match: compare two readings element-wise to
  assert "exactly one display moved" - which still holds when two displays share a value.
- Keep the process object out of any loop variable. `$p` reused as a fade sample index clobbered
  `Start-Process`'s object: `$p.Id` became 0, `$p.WaitForExit` failed on an `Int32`, and a scripted
  night was left running on the desktop.
- A display the night did **not** override is mid-ramp at turn-off (52 of 47 -> 67 a second later):
  poll for its baseline with a bounded wait; only an overridden display can be asserted immediately.
- Machine fixtures for the two-witness setup: active paths 2; `\\.\DISPLAY1` = AOC `Q27G3G3R3` over
  DDC/CI (57); internal panel EDID `BOE0764`, WMI `DISPLAY\BOE0764\4&29539ac7&0&UID265988_0` (67), its
  friendly name empty so the row reads "Display 1".
