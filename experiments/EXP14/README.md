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
  was built, driven, and **rolled back** (see Open questions): the user found it opened in the wrong
  place (a corner of a monitor) and did not follow them from one monitor to another. Everything about
  it was reverted: `SystemPlacement`, `PlacementApp` and `Placement` are back at EXP09's state, and the
  `--place-cursor` seam and the `spotlight` reading are gone. What is left of that attempt is this
  record and the two Learnings below.
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
      follow-the-pointer; it was built and **rolled back** on 2026-10-16 because it opened in odd
      places (a monitor's `0,0`) and did not reopen on the screen the user had moved to. The whole
      pointer path is reverted; issue 2 stays open, and the standing suggestion is to try
      **"remember the last monitor the surface was on"** instead (persisted, mouse-free) as its own
      experiment.
- [x] Out of scope: `WM_DEVICECHANGE`, per-monitor profiles, re-reading a retained display's own value
      after a mode change.

## Conclusion (findings)
_Pending._

## Learnings
- A window message hook added on `Window.SourceInitialized` can silently never run: this app's
  `Theming.Bind` calls `SystemTheme.ApplyTitleBar` -> `WindowInteropHelper.EnsureHandle()` in the
  `SurfaceView` constructor, so the HWND - and `SourceInitialized` - exist **before** `Program`
  subscribes. This is why EXP09's own `WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE` re-place hook is inert
  (`--place-after` drives the model, not the message). Add a hook on the spot with `EnsureHandle()` +
  `HwndSource.FromHwnd()` (what `SystemTheme.Follow` does, and what `DisplayWatcher` does), or it will
  not run. (EXP14 briefly fixed `Place.Follow` this way, then reverted it with the placement rollback.)
- A message hook is only proven by a real message: post one (`PostMessage(hwnd, 0x7E, 0, 0)` for
  `WM_DISPLAYCHANGE`) and read the app's own evidence back; an "it is wired" claim is worth nothing.
  Use when a feature hangs off a Win32 message rather than a timer or a call.
- `BrightnessController.Detect()` now re-runs, so it must give back what the last read took
  (`Release()`: `DestroyPhysicalMonitors` + `FreeHGlobal` on each `_allocations` array, then clear the
  id maps) *before* reading again; the arrays were freed only in `Dispose` and would otherwise leak on
  every display change.
- A row subscribes to the shared `BrightnessWriter`'s `Written`/`Failed`; a row that is removed must
  unsubscribe (`SurfaceView.Remove` -> the row's `Detach`), or the writer's event list keeps dead
  closures for every display that ever came and went.
- An empty detection (0 active paths) is a normal instant of a projection change, not a fact: keep the
  last good display set instead of wiping every row and rebuilding it a moment later.
- Windows: the monitor that contains the origin `(0,0)` is the primary; a secondary display's
  `Shell_SecondaryTrayWnd` has no `TrayNotifyWnd`. Use when a reading assumes "the taskbar with the
  clock" - it is the primary's.
- Choosing the surface's screen from the mouse pointer is a poor UX here: it opened in a monitor corner
  the user did not expect and did not follow them between monitors, so it was rolled back. Prefer a
  **remembered** screen (persisted) over a live pointer read when a window must reopen where it was.
- PowerShell parses `foreach ($x in Fn())` as an error - a command call is not an expression. Assign
  first (`$all = Fn; foreach ($x in $all) { ... }`). Use when a loop's source is a function.
- `verify-slider.ps1` treats the value it reads at launch as each display's "original"; re-running it
  over a machine already left at its `min+5` (the value it uses to find the thumb) accepts that as the
  baseline. The panel was restored by hand when that happened during the standing-run re-run.
