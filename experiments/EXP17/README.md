# EXP17 - The night light returns to a Default you configure

## Summary
The schedule's "day value" was *read off the panel* when the app first adopted it, and a slider drag
became that display's own day value - so the morning could come back dim, or come back at yesterday's
slider. Add a **Default** percent to the settings (`Default: [90]%`, above the night-light section) as
the one authoritative day value, and make the slider purely **ephemeral**.

## Hypothesis
The Default (settings) is the only day value: the schedule holds it whenever it is not dimming
(daylight, the night light switched **off**, and the sunset placeholder) and returns to it at every
turn-off, scaled into each display's own range like the night strength. A slider drag is ephemeral - it
moves the display and stands the schedule down for that display only until the next boundary (the coming
turn-on or turn-off), and it never changes the Default. So with Default 80 and a slider at 80: dragging
the slider to 50 moves the screen to 50, leaves the Default at 80, and the **next day's daylight is 80
again**. Changing the Default moves the screens (while the schedule is not dimming). Every move that has
no clock boundary to anchor it - a launch in the day, a Default edit, the off/inert states - is a
staircase over the gradual fade, not a jump.

Falsified by: the morning landing on a slider value rather than the Default; a drag changing the
Default; the Default being ignored when the night light is off or in the sunset placeholder; a
needed move still jumping in one write; or a fade starting from a stale value instead of where the
display actually is.

## Methodology & blast radius
- Method: the schedule stays a clock-driven machine (no `Thread.Sleep`, no timer in the model), and
  the UI claim is driven through UI Automation patterns, per `AGENTS.md`.
  - `ScheduleSettings` gains `Daylight` (0-100) with `TrySetDaylight`, and the settings window a new
    **Default** row (`Default: [80]%`) *above* the "Schedule night light" section - outside the
    section's On switch, so it is always editable (its own `TextBox` seam for the verifier).
  - `NightLightScheduler`: `Observe` records the reading but never makes it the day value; the day
    value is the Default scaled into `[Min, Max]`, and nothing else sets it. `Request` (a slider drag)
    is an **ephemeral** stand-down: `Last = value` plus an expiry at the next boundary
    (`NextBoundary` = the earlier of the next turn-on and turn-off; the kept times when there is no
    window). A changed Default ends the stand-down while not dimming, so "editing the Default moves the
    screens" holds.
  - The Default governs in **Day, Off and Inert** (so switching the night light off, the sunset
    placeholder, and changing the Default all move the displays). Only the night phases dim.
  - A fade begins where the display is: `NightFrom` captured at turn-on, `DayFrom` at turn-off - so a
    slider value does not jump the fade back to a level the display is not at.
  - A move to the Default gets its **own, short** ramp - `dayFade`/`dayStep`, **20 seconds** by default -
    not the night's 30-minute fade: a settings edit is something the user just asked for, so it settles
    promptly. Both come from `Program.cs` (`DayMove`/`DayMoveStep`), with `--day-fade-seconds` /
    `--day-step-seconds` to compress it for a verifier. The night fade is unchanged.
  - `Restore()` hands back the Default, not the reading it took at adoption.
  - `Program.cs` gains one seam (`--daylight N`) **and** a correction: `--no-schedule` must now mean the
    schedule never writes (an off schedule writes the Default), so `Apply` asks the scheduler only when
    the run is scheduled - while still advancing the writer, so a slider drag always lands.
  - `SettingsCodec` gains `daylight=NN` after `strength`; a missing key (an existing file) keeps the
    default. `SettingsPersistence.cs` stays the only I/O.
- May touch: `src/Monitron.App/ScheduleSettings.cs`, `Schedule.cs`, `SettingsWindow.cs`,
  `SettingsPersistence.cs`, `Program.cs`, `README.md` (the day level, and the two levels), the
  `model-test.ps1` / `schedule-test.ps1` / `persistence-test.ps1` headless suites and the
  `verify-settings.ps1` / `verify-persistence.ps1` / `verify-schedule.ps1` end-to-end suites,
  `experiments/EXP17/README.md`, `experiments-overview.md`.
- Off-limits: the night strength's meaning, the fade's shape, sunset mode's lack of a location; the
  user's real `%APPDATA%\Monitron\settings.ini`, autostart entry and displays (verifiers pass a
  throwaway settings file, and `--monitors` where a run would otherwise move a real monitor); no new
  dependency (in-box `csc` only).

## Open questions (known unknowns)
- [x] What should the Default be? - **80 %**, and the UI calls it **Default** (`Default: [80]%`), per
      the user's answers and sketch. The stored key stays `daylight`.
- [x] Slider vs Default - **the Default wins, always**: a drag is *ephemeral* and never becomes a day
      value. (This reverses EXP17's first answer, "the slider's value wins" - the user's correction.)
- [x] How long does a slider value stand? - **until the next boundary, whichever comes first**: a
      daytime drag still lets the evening dim happen, and a night drag is undone by the morning. The
      next day's daylight is the Default. (The alternative - hold until the next turn-off - would keep
      a daytime drag through the night.)
- [x] Does the Default own the whole day? - **yes, smoothly**: daylight, the night light switched off,
      and the sunset placeholder all hold the Default, as a staircase over the fade.
- [x] Editing the Default while a slider value stands - **the Default wins** while not dimming: it ends
      the stand-down and ramps to the new Default. During the night it only changes the morning target.
- [x] Quit / switch-off mid-night - the Default governs, so switching off ramps back to the Default,
      and `Restore()` on close leaves the display at the Default rather than a value read earlier.
- [x] `--no-schedule` - it must not run the schedule at all now that "off" writes, so it never asks
      the scheduler; the writer still runs so a slider drag lands.
- [x] The file - `daylight=NN` after `strength`, `version=1` unchanged (additive; old files load the
      default).

## Conclusion (findings)
_Pending._

## Learnings
- The "day value" the night light restored was *read off the panel at adoption* (`Observe`), so any
  display the app first met while dimmed (a restart mid-window, a re-attach) had its dim value frozen
  as "daylight". A schedule that "remembers" brightness needs a configured value, not a sample.
- An ephemeral UI value needs an **explicit expiry**, not a "lapses at the next X" rule that spans a
  whole cycle: the user wanted "next day's daylight = Default" for a *night* drag (so the lapse is the
  turn-off), but "the schedule resumes at the next boundary" for a *day* drag (the turn-on). One rule -
  the earlier of the next turn-on and turn-off (`NextBoundary`) - gives both.
- The Default must apply in the **inert** state too (the sunset placeholder with no location, equal
  hours): it is "not dimming", and leaving it silent is exactly the bug the user hit
  (`mode=SunsetToSunrise` in their settings made every Default edit do nothing).
- A user-initiated move must not borrow the night's timescale. EXP17's first cut reused the night fade
  for the day ramp, so changing the Default took **30 minutes** (measured: the panel moved 90 -> 60 over
  the whole half-hour). A value the user just typed has to settle like a slider, not like a bedtime fade;
  the move to the Default is now its own short ramp (`DayMove`/`DayMoveStep`, 20 s), measured at ~21 s
  live (90-84-76-67-60). The lesson: measure the *shipped default* - a staircase's happy path proves nothing.
- The phase stays clock-derived, but a move with **no boundary** (a launch or a Default edit in the
  day) cannot be: it needs its own per-display ramp anchor (`DayRampStart`/`DayRampFrom`/`DayRampTo`).
  Re-anchor it when the target changes; the same staircase code (`Fraction`, one write per step) applies.
- Both fades must start from where the display actually is - `NightFrom` at turn-on, `DayFrom` at
  turn-off - or a slider value in place makes the fade jump first (the fade-up used to lerp from the
  night level and would snap a user's 92 down to 38 before ramping to 70).
- `verify-position.ps1` and `verify-icon.ps1` put the pipe at the start of a continuation line
  (`... | Where-Object`), which **Windows PowerShell 5.1 rejects** ("An empty pipe element is not
  allowed"). Run those two with pwsh: `& .\verify-position.ps1`. The `*-test.ps1` suites run under
  either.
- `verify-settings.ps1` / `verify-persistence.ps1` read the numeric fields in on-screen order (the
  Default field, then strength, then the two clock rows): **adding a settings row shifts the indexes
  and the expected value list** - the order went from `38,5,00,9,00` to `80,38,5,00,9,00`, and a field
  *outside* the section must be excluded from "the On switch gates the body" check.
- A verifier that launches the schedule must pass `--daylight` (equal to the display's reading), or the
  new Day-phase ramp moves the displays before the scenario starts; and any run that is *inert* but not
  `--no-schedule` now holds the Default, so it needs `--monitors` (scripted displays) to stay off the
  user's real monitor - `verify-persistence.ps1` does.
- Once "off" also writes (the Default governs), `--no-schedule` has to *skip the scheduler*, not rely
  on it being quiet - and the skip must gate only the scheduler call, not `writer.Advance()`: gating
  both left a slider drag un-flushed and `verify-control` read the display unchanged. `Apply` takes the
  flag now and always advances the writer.
