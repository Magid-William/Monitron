# EXP06 - Apply the night-light schedule (scheduler FSM + real monitors)

## Summary
Make the schedule real: a headless state machine with an injected clock, reading EXP04's
`ScheduleSettings` and feeding EXP05's `BrightnessWriter`, fades the active displays down to the
strength at turn-on, holds through the night and restores their previous brightness at turn-off.
Nothing is persisted; no location lookup.

## Hypothesis
A code-only net48 state machine (`NightLightScheduler`) whose only inputs are the clock, the
settings and each display's last real value can drive the **real** monitors through a whole night -
day value -> gradual fade -> night level -> fade back to the captured day value - **crossing a
boundary by advancing the injected clock, not by waiting**, with the physical monitor's own
read-back (`probe.exe`) following every phase, and with a manual slider move ending the schedule for
the night.

**Falsified if:** a boundary cannot be crossed without real waiting; a fade is not monotonic or
overshoots its target; the restore does not return the pre-schedule value; the wrap across midnight
mis-computes (a display is dimmed *during* the day); a manual slider move during the night is undone
by the schedule; or closing the app leaves a display dimmed.

## Methodology & blast radius
- Method:
  1. `src/Monitron.App/Schedule.cs`: `NightLightScheduler` - pure state plus transitions, no WPF, no
     P/Invoke, no timer (so it compiles into the headless test, like `Brightness.cs`). Phase is
     derived from the clock (`Day | FadingIn | Night | FadingOut | Off | Inert`) with a **wrap-aware**
     window; per display a captured day baseline and an override flag; `Advance()` returns the writes
     it wants (deduplicated, so a fine tick cannot cause a write storm) and `Restore()` hands every
     display its baseline back; the ramp is a bounded staircase of `fade/step` steps; the night level
     is `Min + round((Max-Min) * Strength/100)`.
  2. `ScheduleSettings.ResolvedWindow` / `TryParseClock`: the window the scheduler uses (sunset mode
     without a location resolves to turn-on == turn-off, hence inert - exactly EXP04's placeholder)
     and the 24-hour text the verification seams need.
  3. `src/Monitron.App/ScheduleTests.cs` + `schedule-test.ps1`: the machine driven by a clock that
     only moves when the test says so - no sleeping, no hardware, no framework.
  4. `Program.cs`: one `ScheduleSettings` shared by the settings window and the scheduler; the tick
     calls `scheduler.Advance()` -> `writer.Request(...)` -> `writer.Advance()`; a status line on the
     surface reports the phase; verification seams `--now/--rate` (a scaled clock, so a whole night
     takes seconds) and `--on/--off/--strength/--fade-seconds/--step-seconds`, applied through the
     model's own transitions; `--no-schedule` runs the surface without the scheduler; closing
     restores the day value and flushes.
  5. `SettingsWindow.cs`: `SettingsHost` takes the injected model instead of owning its own, and the
     footer stops claiming that no brightness is written.
  6. Verify by driving: `schedule-test.ps1` (machine, fake clock) and `verify-schedule.ps1` (real
     displays: read with the read-only `probe.exe`, launch with a scaled night, assert the read
     follows the fade to the strength, holds, is **not** undone by the schedule after a manual move,
     returns to the original at turn-off, and is left at the original on close).
- May touch: branch `EXP06` off `EXP05`; `experiments/EXP06/README.md`; the overview row;
  `src/Monitron.App/` (`Schedule.cs`, `ScheduleTests.cs`, `schedule-test.ps1`, `verify-schedule.ps1`,
  `Program.cs`, `ScheduleSettings.cs`, `SettingsWindow.cs`, `build.ps1`).
- Off-limits: VCP `0x60` (input) and `0xD6` (power) and the DDC/CI capabilities string; persistence
  (no registry, no file - the schedule is in memory only); real sunset/sunrise (no location, no
  network, no timezone math); tray shell and autostart; per-monitor profiles; `src/probe/**` beyond
  running it read-only; anything outside `the repository root`; downloads.
- Escalation budget: every scheduled write stays inside the display's own `Min..Max` and on the one
  active display; no write at all if the initial read fails; the verifier drives the excursion and
  ends with the original value restored (and the app restores it again on close).

## Open questions (assumptions adopted - the user approved the plan)
- [x] Manual override - a slider move inside the night window ends the schedule **for that display
      for that night**: nothing is written until the next turn-on, and the user's value becomes the
      day baseline. *Assumption.*
- [x] What turn-off restores - the value each display had when the schedule took over (captured at
      detection), not a fixed day brightness. *Assumption.*
- [x] Fade - 30 minutes in 1-minute steps, constants in the machine, overridable only through the
      verification seams. *Assumption.*
- [x] Launching inside the night window - step to the night level at the first pass (the fade
      anchors at the boundary, not at launch). *Assumption.*
- [x] Tick - one `DispatcherTimer` (200 ms) drives both machines; the scheduler's own step dedup
      bounds the writes, so a fine tick cannot storm the monitor. *Assumption.*
- [x] Sunset-to-sunrise mode - inert until a location exists (`turn-on == turn-off`), no special
      case. *Assumption.*
- [x] Scope - every controllable active display is scheduled; per-display schedules come later.
      *Assumption.*
- [x] Persistence - out of scope. *Assumption.*
- [x] Branch - `EXP06` off `EXP05` (EXP05 is `inProg`; all app code lives on the experiment branch,
      `main` is still only the scaffold). *Assumption.*
- [x] Verification clock - the app accepts a scaled clock for verification only; the machine under
      test is the same one the headless test drives. *Assumption.*

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11, the AOC `Q27G3G3R3` over DDC/CI), every run exiting
cleanly:

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitron.App\build.ps1` | `Monitron.App.exe` from the in-box `csc`, **0 MB** (`Schedule.cs` added to the source list) |
| Machine | `src\Monitron.App\schedule-test.ps1` | **114 assertions, 0 failed** - a clock the test moves drives day -> fade -> night -> fade -> restore, the wrap across midnight, the override, the switch, sunset inertness, per-display ranges, and that a whole night costs at most `fade/step` writes each way |
| End to end | `src\Monitron.App\verify-schedule.ps1` | **24 checks, 0 failed** - a scripted night (`--now`/`--rate 30`) on the real display: the probe watched the fade **41 -> 40 -> 37 -> 35 -> 33 -> 31**, the night held 31, a manual move to 41 was **not** undone, turn-off wrote nothing over it, the settings window showed the running schedule, and closing left the display at its original **41** |
| Surface | `verify.ps1 -AppArgs '--strength 38'` | renders **500x339** with the live default state: the real display at 41 and `Night light: daylight - dims to 38% at 5:00 PM`, exiting 0 |
| Regression | `model-test.ps1` / `writer-test.ps1` / `verify-settings.ps1` | **38 / 19 / 30**, 0 failed |
| Regression | `verify-control.ps1 -AppArgs '--no-schedule'` | **11 checks, 0 failed**: the slider still drives the monitor (**41 -> 31 -> 41**, probe following) |

Findings:
- **The hypothesis holds.** One machine, driven only by a clock and the settings, took a real
  display through a whole night and gave it back exactly: the fade was observed on the hardware as a
  staircase, the night level held, a manual move ended the night's schedule, and closing restored
  the day value. The witness throughout is EXP02's separate read-only `probe.exe`.
- **Deriving the phase from the clock (rather than accumulating it) is what makes it drivable.** An
  `Advance()` that lands far past a boundary computes the same phase as one that walked there tick
  by tick: that is why the headless test crosses a night in milliseconds, why a fine tick is free,
  and why launching inside the window dims at once instead of replaying a stale ramp.
- **A whole night is bounded by construction:** the ramp is a staircase of `fade/step` steps, so
  ticking four times a minute from 17:00 to 06:00 still costs **30 writes** one way (as the test
  asserts) and the hold costs none. The debounce then decides when each lands.
- **A UI defect, found by driving:** the row's slider did not follow the schedule's own write - the
  row showed `31` while the thumb stayed at `41`, so the "user's value" the verifier set was the
  value the slider already had and no move was reported at all (the first run of this verifier
  failed three checks on it). The writer's `Written` event now syncs the slider too, **guarded**:
  assigning `Slider.Value` raises `ValueChanged`, and without the guard the schedule would read its
  own write as the user taking over and stand down for the night.
- **The override rule is the product thesis, and it is now asserted twice** - headless (the override
  survives to the next turn-on, which re-arms from the user's value) and on the hardware (the
  schedule stayed out of the way until turn-off and wrote nothing over it).
- **Sunset mode needs no special case:** without a location its window is turn-on == turn-off, which
  the machine treats as no window at all - it reports "not configured" and touches nothing.

Boundaries this run did not exercise:
- **The internal-WMI path is again only implemented, not exercised** (this machine has one active
  target and it is external). Nothing in the schedule is per-kind; it hands values to the same
  `BrightnessWriter` EXP05 proved.
- **Nothing is persisted.** The schedule lives in memory and dies with the process; every launch
  starts from EXP04's defaults. Sunset/sunrise still has no location, and the window has no
  "fade over N minutes" field - the fade is a constant (30 min in 1-min steps) overridable only by
  the verification seams.
- **Closing restores the day value.** That is friendlier for a hand-run app and it is also what
  keeps the verifier's escalation budget honest; a future tray app that keeps running needs no such
  rule, but a decision about it belongs to the experiment that adds persistence.
- Stale artefacts, unchanged and untouched: `verify-settings.ps1 -FromGear` still pins the EXP03
  sample name `Settings for AOC Q27G3G3R3` (the real row's gear is `Settings for Q27G3G3R3`), which
  EXP05 already recorded as stale; and `verify.ps1`/`verify-control.ps1` fail on their default
  `-AppArgs ''` under this PowerShell (an empty `-ArgumentList` is refused), so they need a flag.
  **The surface now runs the schedule by default**, so a verifier that drives a slider should pass
  `--no-schedule` - which is what the regression pass above does.

## Conclusion (findings)
**`success`** (2026-10-15, user-declared). The user asked for the night-light scheduling
functionality; this is it - the schedule runs, on the real displays, off the settings the window
edits, and an independent reader watched it do so.

The hypothesis holds. One code-only net48 state machine, driven only by a clock and
`ScheduleSettings`, took the active display through a whole night - day value, fade down, hold, fade
back, manual override - and left it exactly where it started. The clock is the one thing a verifier
had to script: `--now`/`--rate` moves a whole night into ~50 seconds of real time, and the physical
monitor's own read (`probe.exe`) followed every phase - 41 -> 40 -> 37 -> 35 -> 33 -> 31, held, stood
aside for a manual move, and was left at 41.

What this settles:
- **The schedule is a machine, and the machine is the experiment.** 114 assertions drive it with a
  clock the test moves: the fade staircase, the midnight wrap, a bounded write count, the override,
  the switch, sunset inertness, per-display ranges. Deriving the phase from the clock instead of
  accumulating it is what makes that possible - and it is also why launching inside the window dims
  at once, why a jump past a boundary is not missed, and why a fine tick costs nothing.
- **The last seam EXP02 opened is closed.** EXP02 proved detection and a write, EXP03 the surface,
  EXP04 the settings shape, EXP05 the real values on it; this applies that shape to those displays.
  A slider drag and a scheduled fade now share one actuator and one debounce.
- **The product thesis' rule is real, not aspirational:** "a manual override ends the schedule for
  the night" is asserted headlessly *and* on hardware - the night is still running, and the schedule
  does not fight the user until the next turn-on, which re-arms from the user's own value.
- **Nothing rests on the app's opinion of itself.** Every hardware claim is read back with EXP02's
  separate read-only probe, and the escalation budget held: the display ends at its original value
  on every path, a failed check included.

What it deliberately does not settle - the boundary of this experiment:
- **Nothing is persisted.** The schedule lives in memory and dies with the process; every launch
  starts from EXP04's defaults. A real overnight dim therefore depends on the app running, and there
  is no tray shell or autostart behind it yet.
- **Sunset to sunrise has no location** - inert by construction (turn-on == turn-off), which is
  exactly the placeholder EXP04 left and the honest answer until an experiment looks one up.
- **The fade is a constant** (30 minutes in 1-minute steps); the window has no field for it, only
  the verification seams can change it.
- **The internal-WMI path is again unexercised** (this machine's one active target is external), and
  the schedule is not per-display: every controllable active display gets it.
- **Closing restores the day value.** That suits a hand-run app and keeps the escalation budget
  honest; a resident tray app will have to decide what it does instead.
- **EXP05 was `inProg` and un-concluded when this experiment concluded.** EXP06 branches off its
  tip and builds on its code; EXP05's record stands as its own (it was concluded later, on its own
  branch).
- Stale artefacts, untouched: `verify-settings.ps1 -FromGear` still pins EXP03's sample name
  `Settings for AOC Q27G3G3R3`, and `verify.ps1`/`verify-control.ps1` need a non-empty `-AppArgs` -
  now `--no-schedule` too, when they drive a slider.

Next, in the order the seams suggest: **persistence** (a file behind this window, so the schedule
survives a reboot), then the **tray shell + autostart** that makes an overnight schedule useful with
no window open, then real **sunset/sunrise** by location, then per-monitor profiles and identity
that survives reconnect (`docs/prior-art/apis-and-identity.md`).

Evidence: `%TEMP%\monitron-exp06\` - `00-default-live`, `01-day`, `02-night`, `03-override`,
`04-day-after`. The reproducible form is the three commands above: `schedule-test.ps1`,
`verify-schedule.ps1` and `verify.ps1 -AppArgs '--strength 38'`.

## Learnings
- `src\Monitron.App\schedule-test.ps1` - builds and runs the headless `NightLightScheduler`
  assertions with the in-box `csc` (console exe; a winexe cannot print) against a clock the test
  moves: a whole night, the midnight wrap, the override and the switch in milliseconds. Use it
  whenever the schedule or the fade changes.
- `src\Monitron.App\verify-schedule.ps1` - a real night against a real display in ~50 s:
  `--now "<today> 16:00:00" --rate 30 --on 16:05 --off 16:22 --strength N --fade-seconds 120
  --step-seconds 10` (the scaled clock carries the debounce with it), then assert with the read-only
  `probe.exe` that the display follows the fade, holds, survives a manual move and is left at its
  original on close.
- Derive a state machine's state from the clock, never accumulate it: any tick computes the same
  answer, a jump past a boundary is not missed, and the machine becomes drivable in milliseconds.
  Carry only what the clock cannot tell you - the day value, the last value written, an override.
- Read a WPF `TextBlock`'s text over UI Automation by finding it on `AutomationId` and reading
  `Current.Name`; never set `AutomationProperties.Name` on it, or UIA reports that name instead of
  the text.
- Assigning `Slider.Value` raises `ValueChanged`: when the app writes a value itself, guard the
  handler (`syncing`), or the machine reads its own write as a user action - here it would have
  cancelled the night's schedule silently.
- `Start-Process -ArgumentList ''` is refused by this PowerShell: a verifier needs at least one
  argument, and since the surface now runs the schedule by default that argument should be
  `--no-schedule` when the script drives a slider.
- An overnight window wraps midnight: contain it as `t >= on || t < off` when turn-on is later in
  the day than turn-off, and measure "how long ago" as `t - boundary` (+24 h when negative).

