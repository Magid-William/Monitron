# EXP14 - Live displays: re-detect the display set on a display change

## Summary
The app reads its displays once at launch. After a projection change (Extend / external-only /
duplicate) it still shows the old set: a vanished display keeps a row, a new one has none. Re-detect on
a display change and reconcile the rows.

## Hypothesis
The display set is captured once and never reconciled. Make it live - re-read it on a display change
and diff it against what is shown - and a change (Extend / Second-screen-only / PC-screen-only) adds
and removes rows within about a second, with no stale row and no write or restore to a display that has
gone.

Falsified by: a change that leaves a row for a vanished display (or misses an added one); a removed
display still being written or restored on close; an added display never getting a row; or a re-read
that changes nothing when it should (or churns every row when one display moved).

## Methodology & blast radius
- Method: extract the launch's one-shot detection into a machine the app can re-run - a `DisplaySet`
  diff (previous ids + freshly detected list -> added / removed / kept) owned by a `DisplayRegistry`
  that seeds the writer and scheduler for an arriving display and forgets a departed one; a
  `SurfaceView` that adds and removes rows and **detaches** each removed row's writer handlers. A
  `WM_DISPLAYCHANGE` hook (`DisplayWatcher`, debounced like `--place-after`) re-runs the same refresh,
  and the tray's Show re-runs it too. `BrightnessController.Detect()` is made re-callable (it must
  destroy and clear its previous batch of physical-monitor handles, not leak them into `Dispose`).
  Verified by driving: `DisplaySet`/`DisplayRegistry` headlessly with a fake controller and no sleeps
  (`display-test.ps1`), and the app end to end (`verify-displays.ps1`) where UI Automation's row set is
  the outside witness to an add or a remove, the app's own `--display-report` must agree, a real
  `WM_DISPLAYCHANGE` is posted to prove the hook re-reads the actual desktop without changing the rows,
  and the read-only EXP02 probe witnesses that no monitor value changed.
- A second half of the original plan - making the surface **open on the monitor the pointer is on** -
  was built, driven, and **rolled back** the same day (see Open questions): the user found it opened in
  the wrong place (a monitor's `0,0`) and did not follow them from one monitor to another. Everything
  about it was reverted: `SystemPlacement`, `PlacementApp` and `Placement` are back at EXP09's state,
  and the `--place-cursor` seam and the `spotlight` reading are gone.
- May touch: `src/Monitron.App/Program.cs`, `Brightness.cs`, `BrightnessController.cs`, `Schedule.cs`,
  a new `DisplaySet.cs` and `DisplayWatcher.cs`, `build.ps1`, a new `verify-displays.ps1` and
  `display-test.ps1`, `experiments-overview.md`, and the header comments naming this experiment.
- Off-limits: the placement and every `--place-*` seam (restored to EXP09, unchanged); the
  settings.ini schema; per-monitor profiles; the theme, icon and tray menu; the user's real displays
  and taskbar (no `SetDisplayConfig`, no moving the taskbar - an alternate topology is injected, never
  made).

## Open questions (known unknowns)
- [x] Issue 1's trigger: `WM_DISPLAYCHANGE` alone (proposed default), assuming a monitor plugin also
      raises it; `WM_DEVICECHANGE`/`RegisterDeviceNotification` only if that proves false.
- [x] Re-detect runs on the UI thread after the debounce - a rare, brief hitch, accepted.
- [x] A detection that finds **no** display is not trusted (a mode switch has a moment with none), so
      the last good set is kept rather than wiping every row.
- [x] The `--monitors` seam's grammar is settled in code (id|name|current items, so a row's identity
      survives the change and add/remove is what the verifier sees).
- [x] Issue 2's meaning: **"remember the last screen"** vs **"follow the pointer"**. The user chose
      follow-the-pointer; it was built and **rolled back** on 2026-10-16 (it opened in a monitor's
      `0,0` and did not reopen on the screen the user had moved to). The whole pointer path is reverted
      and issue 2 moves to **its own next experiment**, where the standing suggestion is "remember the
      last monitor the surface was on" (persisted, mouse-free).
- [x] Out of scope: `WM_DEVICECHANGE`, per-monitor profiles, re-reading a retained display's own value
      after a mode change.

## Conclusion (findings)
`success` (user-declared). The display set is live: a projection change is seen and the rows follow it.
The whole app change is **one new machine and one new hook** plus the seams around them - `DisplaySet.cs`
(the `DisplaySet.Diff` rule and the `DisplayRegistry` that seeds an arriving display and forgets a
departed one), `DisplayWatcher.cs` (the debounced `WM_DISPLAYCHANGE` re-read and the `WindowHook`
helper), a `SurfaceView` that can grow and shrink its rows (and unhooks a removed row from the writer),
a re-callable `BrightnessController.Detect()` (`Release()` gives back the last batch of physical-monitor
handles), and `Forget`/`Count` on the writer and the schedule. Nothing is persisted and no display is
touched by a re-read.

**Run 2026-10-16.** The machine reports **two** active targets (the internal panel beside the AOC).
- Headless `display-test.ps1`: **32 assertions, 0 failed** - the diff (add / remove / keep / agree), the
  registry's seeding and forgetting (the writer's and the schedule's id sets drop a departed display), a
  close's `Restore()` handing a value back to the surviving display alone, and an untrusted empty read.
- End to end `verify-displays.ps1`: **14 checks, 0 failed** - a scripted **2 -> 1** rows when a display
  goes and **1 -> 2** when one appears (UI Automation's row set, with the app's `--display-report`
  agreeing); a **real `WM_DISPLAYCHANGE`** posted at the window re-read the actual desktop
  (`detections` 1 -> 2) without changing the rows or the app; and the read-only EXP02 probe read the
  same 32 across every run.
- Every standing suite re-run green after the location rollback: position **60**, slider **55**, tray
  **55**, theme **42**, settings **30**, icon **33**, control **28**, schedule **35** (all 0 failed), and
  the headless model suites unchanged.

**The finding is the hook.** The first `verify-displays` run showed the re-read never happening: a
`WM_DISPLAYCHANGE` posted at the window produced nothing. The cause is `Theming.Bind` ->
`SystemTheme.ApplyTitleBar` -> `WindowInteropHelper.EnsureHandle()`, which creates the HWND inside the
`SurfaceView` constructor, so **`Window.SourceInitialized` has already fired before `Program`
subscribes** - a hook added on it silently never runs. The same trap means **EXP09's own
`WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE` re-place hook has been inert all along** (its `--place-after`
verifier drove the model, never the message). EXP14 adds its hook on the spot
(`EnsureHandle()` + `HwndSource.FromHwnd()`, what `SystemTheme.Follow` already did); the rollback put
`Place.Follow` back to its inert form, and that is left as a known, recorded defect.

**Boundaries.** The trigger is `WM_DISPLAYCHANGE` alone (`WM_DEVICECHANGE` is not subscribed, so a
monitor that arrives without the desktop geometry moving would not be seen - untested, and not
observed). The re-read runs on the UI thread after a 700 ms debounce. A display that stays is kept as it
is: a mode switch that itself changes a retained display's brightness is not re-read into its row.
A real topology change is **injected, never made** (the user's displays are a verifier's not to
change), so the add/remove path is proven with a scripted set and the real path with a posted message;
the machine's real two displays also exercised the WMI and DDC branches side by side. `verify-slider.ps1`
treats the value it reads at launch as each display's "original"; one re-run found the internal panel at
its thumb-finding value (5) and accepted it, so the panel was restored by hand - a pre-existing verifier
wart, not this experiment's.

## Learnings
- A window message hook added on `Window.SourceInitialized` can silently never run: `Theming.Bind`
  calls `EnsureHandle()` in the `SurfaceView` constructor, so the HWND - and `SourceInitialized` - exist
  before `Program` subscribes. Add a hook on the spot (`EnsureHandle()` + `HwndSource.FromHwnd()`), the
  way `SystemTheme.Follow` and `DisplayWatcher` do. This is why EXP09's re-place hook is inert.
- Prove a message hook with a real message: post one (`PostMessage(hwnd, 0x7E, 0, 0)` for
  `WM_DISPLAYCHANGE`) and read the app's evidence back; "it is wired" is worth nothing.
- A re-callable `BrightnessController.Detect()` must give back what the last read took
  (`DestroyPhysicalMonitors` + `FreeHGlobal` on `_allocations`, then clear the id maps) before reading
  again, or it leaks on every display change.
- A row removed from the surface must unsubscribe from the shared `BrightnessWriter`, or its closures
  stay on the event list for every display that comes and goes.
- An empty detection (0 active paths) is a normal instant of a projection change, not a fact: keep the
  last good display set rather than wiping and rebuilding every row.
- For a window that must reopen where it was, prefer a **remembered** screen (persisted) over a live
  pointer read - choosing the monitor from the mouse opened in a place the user did not expect and did
  not follow them between monitors (this attempt was rolled back).
- PowerShell parses `foreach ($x in Fn())` as an error - a command call is not an expression; assign
  first (`$all = Fn`).
