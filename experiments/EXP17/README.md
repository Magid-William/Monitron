# EXP17 - The night light returns to a day value you configure

## Summary
The schedule hands every display back a "day value" it read off the panel at adoption, not one you
state. Add a **Default** percent to the settings (`Default: [90]%`, above the night-light section),
and let the morning return each screen to it.

## Hypothesis
With a `daylight` value in the settings, the schedule's day value for a display is that percent scaled
into the display's own range - the same rule the night strength already uses - unless the user has
dragged that display's slider, in which case the user's value is its day value. A night that dims
90 -> 32 then gives **90** back at turn-off, at a fresh launch, at a mid-night restart and when the
feature is switched off, whatever the panel happened to read when Monitron adopted it. The return is
smooth (a staircase over the gradual fade), including at a launch during the day, which has no clock
boundary to anchor the ramp.

Falsified by: the morning still landing on the night level; the night level leaking into the day; a
display left at the daylight value on Quit when it should have kept the value it had; or a display
that needs to move during the day still jumping in one write.

## Methodology & blast radius
- Method: the schedule stays a clock-driven machine (no `Thread.Sleep`, no timer in the model), and
  the UI claim is driven through UI Automation patterns, per `AGENTS.md`.
  - `ScheduleSettings` gains `Daylight` (0-100) with `TrySetDaylight`, and the settings window a new
    **Default** row (`Default: [80]%`) *above* the "Schedule night light" section - outside the
    section's On switch, so it is always editable (its own `TextBox` seam for the verifier).
  - `NightLightScheduler`: `Observe` no longer stores the reading as the day value; a display's day
    value is the `daylight` percent scaled into `[Min, Max]`. `Request` (a slider drag) still sets that
    display's own day value - the per-display override. The fade-out, the switch-off restore and
    `Restore()` hand back what they did before.
  - The one new piece of state: because a launch during the day has no clock boundary, a display that
    must move in the Day phase carries the moment its ramp began (and the value it began from), so the
    move is a staircase over the gradual fade rather than one jump - `gradual fade` off keeps the jump.
  - `SettingsCodec` gains `daylight=NN` after `strength`; a missing key (an existing file) keeps the
    default. `SettingsPersistence.cs` stays the only I/O.
  - `Program.cs` gains one seam: `--daylight N`, applied like `--strength`, never saved.
- May touch: `src/Monitron.App/ScheduleSettings.cs`, `Schedule.cs`, `SettingsWindow.cs`,
  `SettingsPersistence.cs`, `Program.cs`, `README.md` (one line: how dim to go, and back up to),
  the `model-test.ps1` / `schedule-test.ps1` / `persistence-test.ps1` headless suites and the
  `verify-settings.ps1` / `verify-persistence.ps1` / `verify-schedule.ps1` end-to-end suites (their
  numeric-field count and reading order change), `experiments/EXP17/README.md`,
  `experiments-overview.md`.
- Off-limits: the night strength's meaning, the per-display override rule, the fade's shape, sunset
  mode; the user's real `%APPDATA%\Monitron\settings.ini`, autostart entry and displays (verifiers
  pass a throwaway settings file and `--monitors`); no new dependency (in-box `csc` only).

## Open questions (known unknowns)
- [x] What should `daylight` default to? - **80 %** (the user's answer). An existing settings file
      with no `daylight` key starts returning panels to 80 in the morning; the row makes it visible.
- [x] For a display the user has dragged, which value wins at the morning? - **the user's value**
      (per-display day value). The setting is the default only for panels never touched.
- [x] Does the setting own the whole day, not just the night-to-day transition? - **yes, everywhere,
      but smoothly**: a launch during the day moves to the day value as a staircase over the fade.
- [x] Quit / switch-off mid-night - unchanged: hand back the value the display had when the schedule
      took it over. The daylight value is the morning target, not a reset on close.
- [x] The label and the place - a **Default** row (`Default: [80]%`) *above* the night-light section,
      outside its On switch, per the user's sketch; same `[NN] %` shape and automation seam.
- [x] The file - `daylight=NN` after `strength`, `version=1` unchanged (additive; old files load the
      default).

## Conclusion (findings)
_Pending._

## Learnings
- The "day value" the night light restored was *read off the panel at adoption* (`Observe`), so any
  display the app first met while dimmed (a restart mid-window, a re-attach) had its dim value frozen
  as "daylight". A schedule that "remembers" brightness needs a configured value, not a sample.
- The phase stays clock-derived, but a move with **no boundary** (a launch during the day) cannot be:
  it needs its own per-display ramp anchor (`DayRampStart`/`DayRampFrom`/`DayRampTo`). Re-anchor it
  when the target changes, and the same staircase code (`Fraction`, one write per `step`) applies.
- The fade down must start from where the day *is*, not from the day target - else a ramp still
  running at turn-on jumps the display up. Capture `NightFrom` when the night begins.
- `verify-position.ps1` and `verify-icon.ps1` put the pipe at the start of a continuation line
  (`... | Where-Object`), which **Windows PowerShell 5.1 rejects** ("An empty pipe element is not
  allowed"). Run those two with pwsh: `& .\verify-position.ps1`. The `*-test.ps1` suites run under
  either.
- `verify-settings.ps1` / `verify-persistence.ps1` read the numeric fields in on-screen order (the
  Default field, then strength, then the two clock rows): **adding a settings row shifts the indexes
  and the expected value list** - the order went from `38,5,00,9,00` to `80,38,5,00,9,00`, and a field
  *outside* the section must be excluded from the "the On switch gates the body" check.
- A verifier that launches the schedule must pass `--daylight`, or the new Day-phase ramp moves the
  displays before the scenario starts (a no-op when the daylight equals the reading).
