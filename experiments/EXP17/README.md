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
    is an **ephemeral** stand-down with an expiry at the next boundary (`NextBoundary` = the earlier of
    the next turn-on and turn-off; the kept times when there is no window). A changed Default ends the
    stand-down while not dimming, so "editing the Default moves the screens" holds.
  - The Default governs in **Day, Off and Inert** (so switching the night light off, the sunset
    placeholder, and changing the Default all move the displays). Only the night phases dim.
  - A fade begins where the display is: `NightFrom` captured at turn-on, `DayFrom` at turn-off - so a
    slider value does not jump the fade back to a level the display is not at.
  - Every move to a value runs at a **constant speed in fixed-size steps** - `daySpeed` (10 points/s) and
    `dayGrain` (2 points) - not the night's 30-minute fade: the duration follows the distance, so a
    one-point move is a single write and a long one glides, and every step is the same size, so
    smoothness does not depend on the distance. `Program.cs` carries `DaySpeed`/`DayGrain`, with
    `--day-speed` / `--day-grain` for a verifier. The night fade is unchanged.
  - The **slider** uses that same glide: the knob jumps to the click (`IsMoveToPointEnabled`, EXP13) while
    the display catches up, and the row keeps the knob at the user's value (not the in-flight one) until
    it lands. `Request` no longer applies the value, it starts the glide; the steps go through
    `writer.Apply` (due at once) because they are 200 ms apart and the slider's 250 ms settle would
    swallow them. Under `--no-schedule` there is no scheduler to glide it, so the row writes directly.
  - The knob lands on the *instruction*, not on a fading value: `Goal` is the Default by day (and null
    while the schedule is dimming or a slider value is still on its way), so a Default edit lands the knob
    on the new Default at once and the dimming catches up - the same shape as a click on the track. The
    night fade still moves the knob with the fade.
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
- A user-initiated move must not borrow the night's timescale, and it must not be a *fixed duration*
  either. Reusing the night fade made a settings edit take 30 minutes; a fixed 5-second ramp then made a
  **one-point** edit wait 5 seconds while a 70-point one jumped 7 points per write. The right shape is a
  **constant speed in fixed-size steps** (10 points/s, 2 points per step): the duration follows the
  distance, and smoothness does not. Measured live: 90 -> 91 lands in one write (~0.3 s); 91 -> 20 glides
  in ~7.6 s in small even steps. The lesson: measure the *shipped default* and poke both ends of the
  range - one happy path proves nothing.
- The writer's 250 ms settle is a *slider* debounce; a glide step 200 ms after the last would be
  coalesced away and the move would jump. So a value the *schedule* glide decides goes through
  `writer.Apply` (due at once) while a slider drag keeps `writer.Request`. The settle is about input, not
  output - and the slider's own glide rides the scheduler's steps, not the debounce.
- The phase stays clock-derived, but a move with **no boundary** (a launch, a Default edit, or a slider
  the user just moved) cannot be: it needs its own per-display ramp anchor (`DayRampStart`/`DayRampFrom`/
  `DayRampTo`). Re-anchor it when the target changes; one glide (`GlideAt`) serves both the Default move
  and the slider.
- Count the glide's steps in **ticks, not seconds**: `0.2 s * 10 / 2` in doubles lands just under 1.0, so
  a 200 ms step quietly produced 2- and 4-point jumps in a deterministic test - the very choppiness the
  glide exists to remove. `elapsed.Ticks / ticksPerStep` is exact.
- A smoothed slider needs the knob to *lead*. The row used to copy every write's outcome back onto the
  slider ("cannot show one value while the monitor holds another"), which would make the knob crawl after
  the click; it now holds the knob at the user's value while a glide is pending and follows the schedule
  again once it lands.
- ...and the same is true of the *settings*: after a Default edit the knob crawled along with the dimming,
  because it only ever saw the in-flight writes. Giving the schedule a `Goal` - the instruction it is
  headed to, null while it is fading - lets the row land the knob on the new Default at once, which is
  what "behave like the click on the slider" means. A smoothed move still needs a control that shows the
  destination, not the journey.
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
