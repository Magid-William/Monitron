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
again**. Changing the Default moves the screens (while the schedule is not dimming). Every change of a
display's *wanted* value glides at a constant speed in fixed-size steps - the Default, a slider, the
night dimming at turn-on, the restore at turn-off, a strength or an hours edit - so nothing jumps, and
there is no separate "fade" (and so no gradual-fade switch).

Falsified by: the morning landing on a slider value rather than the Default; a drag changing the
Default; the Default being ignored when the night light is off or in the sunset placeholder; any
change jumping in one write; a one-point change waiting out a long move; or a glide starting from a
stale value instead of where the display actually is.

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
  - The phase is only **Day / Night / Off / Inert**: the night is the strength, everything else is the
    Default. There is no fade phase and no gradual-fade switch.
  - **Every** change of a display's wanted value glides at a **constant speed in fixed-size steps** -
    `daySpeed` (10 points/s) and `dayGrain` (2 points): the duration follows the distance, so a one-point
    change is a single write and a long one glides, and every step is the same size, so smoothness does
    not depend on the distance. That covers the Default, a slider, the night dimming at turn-on, the
    restore at turn-off, and a strength or hours edit. `Program.cs` carries `DaySpeed`/`DayGrain`, with
    `--day-speed` / `--day-grain` for a verifier.
  - The glide re-anchors whenever the wanted value changes, so a phase change or a settings edit
    mid-move simply retargets from where the display actually is - it never jumps to the old target first.
  - The **slider** uses that same glide: the knob jumps to the click (`IsMoveToPointEnabled`, EXP13) while
    the display catches up, and the row keeps the knob at the user's value (not the in-flight one) until
    it lands. `Request` no longer applies the value, it starts the glide; the steps go through
    `writer.Apply` (due at once) because they are 200 ms apart and the slider's 250 ms settle would
    swallow them. Under `--no-schedule` there is no scheduler to glide it, so the row writes directly.
  - The knob lands on the *instruction*, not on an in-flight value: `Goal` is the night level inside the
    window and the Default otherwise (null while a slider value stands), so a Default edit or a strength
    change lands the knob at once and the dimming catches up - the same shape as a click on the track.
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
- Off-limits: the night strength's meaning, sunset mode's lack of a location; the user's real
  `%APPDATA%\Monitron\settings.ini`, autostart entry and displays (verifiers pass a throwaway settings
  file, and `--monitors` where a run would otherwise move a real monitor); no new dependency (in-box
  `csc` only).

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
      and the sunset placeholder all hold the Default, and the display glides to it.
- [x] Editing the Default while a slider value stands - **the Default wins** while not dimming: it ends
      the stand-down and glides to the new Default. During the night it changes nothing until the morning.
- [x] Does the scheduled night keep a long fade? - **no** (the user's answer: "no long fade at all, i
      dont see it as a useful option"). One glide law everywhere, so the gradual-fade switch is gone.
- [x] Quit / switch-off mid-night - the Default governs, so switching off glides back to the Default,
      and `Restore()` on close leaves the display at the Default rather than a value read earlier.
- [x] `--no-schedule` - it must not run the schedule at all now that "off" writes, so it never asks
      the scheduler; the writer still runs so a slider drag lands.
- [x] The file - `daylight=NN` after `strength`, `version=1` unchanged (additive; old files load the
      default).

## Conclusion (findings)
**Success** (user-declared). The hypothesis held, through six corrections the user drove:

1. **The Default is the only day value.** `Observe` records a display's reading, but the day value is
   `Settings.Daylight` scaled into that display's range, so a screen met while dimmed no longer comes back
   dim, and `Restore()` on close hands the Default back.
2. **A slider drag is ephemeral.** It glides the display and stands the schedule down for that display
   only until the next boundary, and it never changes the Default. (The first cut - "a drag becomes that
   display's day value" - was reversed by the user, and the per-display day value deleted.)
3. **The Default governs in every non-night state, including the sunset placeholder.** The user's own
   `mode=SunsetToSunrise` makes the schedule inert; leaving the Default silent there was the real bug
   behind "changing the Default does nothing".
4. **A move glides at a constant speed in fixed-size steps** (10 points/s, 2 points per step), not over a
   fixed duration. The journey: the night fade (30 min) -> a 1-minute ramp -> 20 s -> 5 s -> the fixed
   *shape*, which is what satisfied both ends (a one-point edit is one write; a 70-point one glides in
   even steps).
5. **The slider uses the same glide**, so a click lands the knob and the dimming catches up; the row had
   to stop copying each in-flight write back onto the knob, and the scheduler grew `Goal` so a Default
   edit lands the knob at once too instead of animating it.
6. **One glide law replaced the fade.** The night dims and restores by gliding, and so does a Strength,
   On or mode edit. The fade, `FadingIn`/`FadingOut`, the `Since`/`Fraction` helpers, the
   `--fade-seconds`/`--step-seconds` seams and the gradual-fade setting were all deleted.

Evidence (measured, never described):
- Headless: writer 23, schedule 142, display 32, model 45, persistence 46, theme 34, icon 49, tray 35,
  position 70 - 0 failed.
- Live, on the real panel, with the read-only `probe.exe` as the outside witness: a Default edit 90 -> 91
  landed in one write (~0.3 s) and 91 -> 20 glided in ~7.6 s; a slider set to 20 left the knob at 20 while
  the panel glided down; a Default edit 90 -> 30 landed the knob on 30 within ~0.5 s while the panel
  glided; the scripted turn-on dimmed 90 -> 50 by gliding and a Strength change to 20 retargeted it
  mid-move.
- E2E: the `verify-*` suites ran green at each earlier step (settings 34, persistence 31, theme 42,
  control 19, slider 30, schedule 29, tray 55, displays 14, position 57, icon 33). After the last two
  changes they were updated for the removed setting/seam and for a move the probe can no longer sample as
  a staircase, and parse-checked - but not run, per the user's standing instruction for this experiment.

## Learnings
- A schedule that "remembers" brightness needs a **configured** value, not a sample. The day value was
  the reading taken at adoption (`Observe`), so a display met while dimmed froze its dim value as
  "daylight"; keep the reading only for what to hand back on exit.
- A value that governs a state must apply in **every** state that is not the one it is about. Leaving the
  Default silent in the inert/sunset state was the whole bug the user hit (`mode=SunsetToSunrise`).
- An ephemeral UI value needs an **explicit expiry**. One rule - the earlier of the next turn-on and
  turn-off (`NextBoundary`) - gave both behaviours the user wanted: a night drag undone by the morning,
  and a day drag that still lets the evening dim happen.
- A user-initiated move must not borrow another timescale, **and must not be a fixed duration**. A
  constant **speed** in **fixed-size steps** is the shape that satisfies both ends of the range: a
  one-point edit is one write (90 -> 91 in ~0.3 s) and a 70-point one glides (91 -> 20 in ~7.6 s). This
  took four iterations (30 min -> 1 min -> 20 s -> 5 s -> the shape); measure the *shipped default* at both
  ends of the range, because one happy path proves nothing.
- One glide law for the whole product beats two. A separate night fade meant two notions of "a move" (a
  clock-anchored fade, a distance-based glide) plus a switch to choose between them; folding everything
  into the glide deleted the fade, its phases, `Since`/`Fraction`, the fade seams and the gradual-fade
  setting, and turned a settings edit mid-night into a plain retarget.
- The writer's 250 ms settle is a *slider* debounce. A paced glide step would be coalesced away, so
  scheduler values go through `writer.Apply` (due at once) and only slider drags use `writer.Request`.
- The clock can derive the **phase** but not the **value**. Every move needs its own per-display anchor
  (`RampFrom`/`RampStart`/`RampTo`) and must re-anchor when the wanted value changes, or it jumps to the
  old target first. One `GlideAt` then serves the Default, a slider, the night dim, the restore and an
  edit alike.
- Count glide steps in **ticks, not seconds**: `0.2 s * 10 / 2` in doubles lands just under 1.0, so a
  200 ms step quietly produced 2- and 4-point jumps - the very choppiness the glide exists to remove.
  `elapsed.Ticks / ticksPerStep` is exact.
- A smoothed control must show the **destination**, not the journey. The row had to stop copying each
  in-flight write onto its slider, and the scheduler needed a `Goal` so a Default edit lands the knob at
  once rather than animating it.
- Commands that worked: `powershell -File .\schedule-test.ps1` (headless; every `*-test.ps1` runs under
  either PowerShell), and `& .\probe.exe` for an outside read of the real panel.
- `verify-position.ps1` / `verify-icon.ps1` put the pipe at the start of a continuation line, which
  **Windows PowerShell 5.1 rejects** ("An empty pipe element is not allowed") - run them with pwsh:
  `& .\verify-position.ps1`.
- `verify-settings.ps1` / `verify-persistence.ps1` read the numeric fields in on-screen order, so **a new
  settings row shifts the indexes and the expected list** (`38,5,00,9,00` -> `80,38,5,00,9,00`), and a
  field *outside* the section must be excluded from "the On switch gates the body".
- A verifier that holds the Default but must not touch a real monitor needs `--monitors` (scripted
  displays); and once "off" also writes, `--no-schedule` must *skip the scheduler call* while still
  advancing the writer, or a slider drag is left un-flushed.
