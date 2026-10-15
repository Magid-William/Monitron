# EXP07 - Persist the schedule settings

## Summary
Put the six settings the window edits and the scheduler reads into a file that survives a restart:
load it at launch, save a change as it is made, and fall back to the built-in defaults on a
missing, partial or corrupt file. Settings only - not per-monitor state.

## Hypothesis
A small, dependency-free, versioned `key=value` settings file behind `ScheduleSettings` makes the
schedule survive a process restart: launching loads the file into the same object the window edits
and the scheduler reads, editing saves it, and a missing/partial/corrupt file yields the model's own
defaults without crashing.

**Falsified if:** an edited value does not come back after relaunch; a missing file does not give the
built-in defaults; a corrupt or partial file crashes startup or discards the keys that *are* valid; a
value written can be read back as a different value; the user's real settings file is written when a
verifier points elsewhere; or the model touches the store when a value was refused.

## Methodology & blast radius
- Method:
  1. `src/Monitorn.App/SettingsPersistence.cs`:
     * `SettingsCodec` - pure `Serialize(ScheduleSettings)` / `TryParse(string, ScheduleSettings)`.
       It writes *through the model's own transitions* (`TrySetStrength`, `TrySetTurnOn/Off`,
       `SetEnabled`, `ToggleGradualFade`, `SetMode`), so validation stays in one place - a
       `strength=999` line leaves the default untouched. Format: `;` comments, `version=1`, then
       `enabled`, `strength`, `gradualFade`, `mode`, `turnOn`/`turnOff` as 24-hour `HH:mm`
       (reusing `ScheduleSettings.TryParseClock`). Culture-invariant; unknown keys ignored.
     * `SettingsStore` - resolves the path (`%APPDATA%\Monitorn\settings.ini`), reads tolerantly,
       writes atomically (temp file, then replace), and reports a failure without throwing at the
       caller.
  2. `ScheduleSettings.cs`: one `Changed` event raised by each transition, and only when a value was
     actually accepted/changed - the single hook the store needs.
  3. `Program.cs`: a `--settings-file <path>` verification seam (so a verifier never writes the real
     file); load before the window and scheduler are built; subscribe `Changed` -> save.
  4. `SettingsWindow.cs`: the footer stops saying "not saved yet".
  5. Tests: `SettingsPersistenceTests.cs` + `persistence-test.ps1` (headless; console exe like the
     other `*-test.ps1`, since a winexe cannot print).
  6. Verify by driving: `verify-persistence.ps1` (UI Automation: edit, close, assert the file,
     relaunch, assert the window reports the edit; and a corrupt file starts on the defaults).
  7. Regressions: `model-test` / `writer-test` / `schedule-test` / `verify-settings` /
     `verify-control` / `verify-schedule`. `verify-settings.ps1` now points at a temp file too, so
     driving it cannot touch the user's settings.
- May touch: branch `EXP07` off `EXP06`; `experiments/EXP07/README.md`; the overview row;
  `src/Monitorn.App/` (`SettingsPersistence.cs`, `SettingsPersistenceTests.cs`,
  `persistence-test.ps1`, `verify-persistence.ps1`, `Program.cs`, `ScheduleSettings.cs`,
  `SettingsWindow.cs`, `build.ps1`, and `verify-settings.ps1`'s default args).
- Off-limits: the registry; per-monitor profiles and monitor identity across reconnect
  (`docs/prior-art/apis-and-identity.md` is a later experiment); tray shell and autostart; real
  sunset/sunrise by location; VCP `0x60`/`0xD6`; `src/probe/**` beyond running it read-only;
  anything outside `D:\Playground\Projects\Apps\Monitorn\`; downloads.
- Escalation budget: every verification run points at a file under `%TEMP%\monitorn-exp07\`, and the
  verifier asserts the real `%APPDATA%\Monitorn\settings.ini` was not created during the run. No
  monitor write beyond what EXP06 already does; the display ends at its original value on every path.

## Open questions (proposed defaults adopted unless the user says otherwise)
- [x] **What is persisted** - the six user settings, not per-display runtime state (day baseline,
      override). *Assumption: a killed-mid-night restore needs monitor identity, its own experiment.*
- [x] **Where** - `%APPDATA%\Monitorn\settings.ini` per user, not `HKCU` and not portable. *Assumption:
      the overview already calls it "a file behind this window"; a file is inspectable and drivable.*
- [x] **Format** - hand-rolled `key=value`, unknown keys ignored, `version=1`. *Assumption: no library
      may be downloaded and the in-box compiler has no serializer.*
- [x] **When saved** - on every real edit (`Changed`), not only on close. *Assumption: the file is a
      few hundred bytes; a crash should not lose the session's edits.*
- [x] **Failure handling** - atomic write; a failed write is non-fatal and surfaced, never thrown.
      *Assumption.*
- [x] **Does closing still restore the day value?** - yes, unchanged from EXP06. *Assumption: there is
      still no resident tray, so persistence is about settings surviving, not a schedule surviving a
      close.*
- [x] **A verifier must not touch the real file** - add `--settings-file`; `verify-settings.ps1` gets
      a temp default. *Assumption.*
- [x] **Monitor identity / per-monitor profiles** - out of scope. *Assumption.*

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11, the AOC `Q27G3G3R3` over DDC/CI). Every run used a
temp settings file; the real `%APPDATA%\Monitorn\settings.ini` was never created (it is absent).

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitorn.App\build.ps1` | `Monitorn.App.exe` from the in-box `csc`, **0 MB** (`SettingsPersistence.cs` added to the source list) |
| Machine | `src\Monitorn.App\persistence-test.ps1` | **42 assertions, 0 failed** - round trip of every field, a missing file, partial/corrupt/unknown-key files, a refused `strength=999`, an unwritable path, and the `Changed` event (fires on a real edit, not on a refused one or a no-op) |
| End to end | `src\Monitorn.App\verify-persistence.ps1` | **27 checks, 0 failed** - a first run shows the defaults and writes nothing; driving strength to 55 and gradual fade off writes `strength=55`/`gradualFade=false`; a restart shows 55 again and does **not** rewrite the file (hash unchanged); a corrupt file starts on the defaults; **the surface's own gear writes the same file and a fresh surface shows 42**; the real file is untouched |
| Regression | `model-test` / `writer-test` / `schedule-test` | **38 / 19 / 114**, 0 failed |
| Regression | `verify-settings.ps1` | **30 checks, 0 failed** - now on a temp file, which also makes its default-value assertions deterministic |
| Regression | `verify-control.ps1 -AppArgs '--no-schedule'` | **11 checks, 0 failed**: the slider still drives the monitor (probe following) |
| Regression | `verify-schedule.ps1` | **24 checks, 0 failed**: a scripted night still runs and leaves the display at its original 70 |

Findings:
- **The hypothesis holds.** A `key=value` file behind `SettingsSchedule` makes the schedule survive a
  restart, on both paths that edit it: the standalone `--settings` window **and** the surface's gear.
  The values come back exactly, and the surface path writes the same file.
- **A first run and a corrupt run both land on the defaults, and neither crashes.** The codec writes
  *through the model's transitions*, so `strength=999` is refused by the same rule the window uses
  and the default stands; an unrecognized mode is left as it is; a garbage document applies nothing.
- **The write is atomic and quiet.** A save goes through `<file>.tmp` then a replace, so no `.tmp` is
  left behind and an interrupted write cannot truncate the file. Opening and closing with no edit
  does not touch the file at all - `Changed` fires only on a real transition, so the window's own
  re-render (which re-assigns the value it already has) cannot cause a write.
- **The command-line seams are not persisted.** The file is loaded first and `--strength/--on/--off/
  --no-schedule` are applied after, before the app subscribes to `Changed`, so a verification flag
  can neither be rewritten into the file nor read back out of it on the next run.
- **Driving it surfaced a coupling:** `--no-schedule` switches the feature off, which greys the whole
  settings body (the On switch gates it, EXP04), so a verifier cannot drive the gear with that flag.
  The verifier instead launches with an inert window (`--on 00:00 --off 00:00`, turn-on == turn-off):
  the feature stays on and the body stays live, but no monitor is touched.

Boundaries this run did not exercise:
- **Only the six user settings are persisted.** A display's captured day baseline is not: it is read
  again at each launch. If the process is killed mid-night (not closed), the display is left dimmed
  and the next launch would read that dimmed value as its "day" value - the honest fix needs the
  resident tray and monitor identity, and is a later experiment.
- **Closing still restores the day value** (unchanged from EXP06): there is still no tray or autostart,
  so the schedule is not expected to keep running once the window is gone.
- Sunset/sunrise is still inert (no location) and the fade is still a constant.
- The internal-WMI path is again only implemented, not exercised (this machine's one active target
  is external). The settings file is not per-kind and does not touch brightness.

## Conclusion (findings)
**`success`** (2026-10-15, user-declared). The user asked for the schedule to survive; this is it -
the six settings the window edits persist to `%APPDATA%\Monitorn\settings.ini`, are read back at
launch into the same object the scheduler reads, and are written on every real edit.

What this settles:
- **The hypothesis holds, on both paths that edit the settings.** The standalone window and the
  surface's gear write the same file, and a restart shows the edited values exactly - proven by
  driving the real window (27 UI Automation checks) and headlessly (42 assertions).
- **A missing, partial, corrupt or unknown-version file is not an error.** The built-in defaults show,
  the keys that *are* valid are kept, and the app starts. The codec writes through the model's own
  transitions, so a bad value is refused by the one rule that already knows the range.
- **The write cannot fire by accident.** `Changed` fires only on a real transition, so the window
  re-rendering itself, a refused value or a no-op set writes nothing - and opening and closing with
  no edit leaves the file byte-identical.
- **The verification seams stay out of the file.** `--strength/--on/--off/--no-schedule` are applied
  after the load and before the app subscribes, so no verifier can rewrite or read back a flag; the
  real per-user file was never created by any run.

What it deliberately does not settle - the boundary of this experiment:
- **Only the six user settings persist.** A display's captured day baseline is not, so a process
  killed mid-night leaves the display dimmed and the next launch would read that as its "day" value.
  The fix needs the resident tray and monitor identity: later experiments.
- **Closing still restores the day value** (unchanged from EXP06), because there is still no tray or
  autostart; the schedule is not expected to keep running once the window is gone.
- Sunset/sunrise is still inert (no location), the fade is still a constant, and the internal-WMI
  path is still only implemented, not exercised.

Next, in the order the seams suggest: the **tray shell + autostart** that makes an overnight schedule
useful with no window open (and decides what closing should do), then real **sunset/sunrise** by
location, then per-monitor profiles and identity that survives reconnect
(`docs/prior-art/apis-and-identity.md`).

Evidence: the three commands above - `persistence-test.ps1`, `verify-persistence.ps1` and the
regression set; shots in `%TEMP%\monitorn-exp07\`.

## Learnings
- `src\Monitorn.App\persistence-test.ps1` and `verify-persistence.ps1` - the headless and the
  end-to-end pass over the settings file (round trip, missing/corrupt/partial, the `Changed` hook,
  and a real restart). Run them whenever the format, a key or the hook changes; the verifier must
  never be handed the real `%APPDATA%` path (it asserts that file is unchanged).
- Write a settings codec *through the model's own transitions* (`TrySetStrength`, `TrySetTurnOn/...`):
  validation stays in one place, so a corrupt file is refused by the same rules the UI enforces and
  the codec needs no knowledge of the ranges.
- `SettingsStore.DefaultPath()` = `%APPDATA%\Monitorn\settings.ini`; a save is `File.WriteAllText` to
  `<file>.tmp` then `File.Replace` (or `File.Move` when the file is new). A failed read or write is
  reported, never thrown. `Changed` must not fire on a refused value or a no-op set, or the window's
  own re-render (which re-assigns the value it already has) would write the file.
- PowerShell: `Check (cond, "msg")` is a trap - the comma makes a 2-element array, which is always
  truthy, so every check "passes". The verifiers' form is `Check (cond) "msg"`.
- `--no-schedule` disables the settings body (the On switch gates it, EXP04). To launch a live-but-
  inert surface for a UI verifier, pass turn-on == turn-off (`--on 00:00 --off 00:00`) instead.
