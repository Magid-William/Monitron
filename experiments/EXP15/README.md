# EXP15 - The surface follows the tray: it opens on the monitor that holds the notification area

## Summary
After a projection change moves the tray to another monitor, the surface still opens on the old one.
It takes its monitor from the window's own (stale) position. Make it take the monitor from the
notification area, and re-place on a real display change.

## Hypothesis
The surface's target monitor is derived from `MonitorFromWindow(window)`, and a hidden surface keeps
its old `Left/Top`, so a tray that moved (the primary/notification area now on another monitor) does
not move the surface - a Show re-places it on the monitor it was already on. If the target monitor is
instead taken from the **notification area's taskbar** (`Shell_TrayWnd`'s monitor), re-read on every
placement (launch, Show, and a real `WM_DISPLAYCHANGE`/`WM_SETTINGCHANGE`/`WM_DPICHANGED`), then after
the change the surface opens in the tray's new monitor's corner.

Falsified by: a change that leaves the surface on the old monitor; the surface landing on a monitor
whose taskbar carries no tray; a fresh launch landing anywhere but the tray's corner; the fallback
(no taskbar readable) opening off-screen or crashing; or the window's monitor still deciding when the
tray is elsewhere.

## Methodology & blast radius
- Method: move the placement anchor from the window to the notification area.
  - `SystemPlacement.cs` stays the only OS reader: it finds the main taskbar (`Shell_TrayWnd`) and
    reads *its* monitor; the window's monitor is the fallback when no taskbar exists.
  - `Placement.cs` gains the pure rule (`PlacementChoice.Choose`) that decides tray vs window, so the
    choice is driven headlessly.
  - `PlacementApp.cs`: the re-place hook is added **on the spot** through `WindowHook.Add` (the EXP14
    pattern), because the surface's handle already exists - EXP09's `SourceInitialized` hook has been
    inert all along. The app's own `--place-report` gains an `anchor=` line and a `placements=` counter.
  - `Program.cs` wires it and adds the seams: `--place-anchor tray|window` (default tray; `window` is
    the EXP09 baseline), `--place-monitor L,T,R,B` (as if the tray's monitor were this), with the
    existing `--place-work` / `--place-dpi` / `--place-edge` / `--place-after`.
- Verified by driving, once when done: pure rule in `position-test.ps1`; end to end in
  `verify-position.ps1` - with the window left on the primary and the tray scripted onto the second
  display (a negative origin), the surface must land in that display's corner, seen by UI Automation's
  bounding rectangle and Win32's `GetMonitorInfo`, with the app's `--place-report` agreeing and the
  read-only EXP02 probe showing no monitor value changed; and a real posted `WM_DISPLAYCHANGE` must
  re-place (the `placements` counter rises).
- May touch: `src/Monitron.App/Placement.cs`, `SystemPlacement.cs`, `PlacementApp.cs`, `Program.cs`,
  `PlacementTests.cs`, `position-test.ps1`, `verify-position.ps1`, `experiments/EXP15/README.md`,
  `experiments-overview.md`.
- Off-limits: the brightness/display/schedule machines; the settings schema; the theme, icon and tray
  menu; `--no-place`'s meaning; the other `--place-*` seams' existing meaning; the user's real taskbar
  and displays (a topology is injected, never made).

## Open questions (known unknowns)
- [x] Which monitor is "the right monitor"? - the **notification area's** monitor (the main taskbar
      `Shell_TrayWnd`). It is where the tray/clock lives, mouse-free and deterministic, and it
      self-corrects when the tray moves. (EXP14's "remembered screen" would keep the old screen - the
      very symptom; the pointer approach was tried and rolled back in EXP14.)
- [x] When the taskbar is on every display, which one? - the main taskbar (`Shell_TrayWnd`) wins; a
      `Shell_SecondaryTrayWnd` is only the fallback for a monitor's taskbar rectangle.
- [x] Persist a remembered monitor? - no; always re-read the tray (a remembered value pins the surface
      to a monitor the tray may have left).
- [x] Fix the inert `Follow` hook here too? - yes, via `WindowHook.Add` on the spot; proven by posting
      a real `WM_DISPLAYCHANGE` and reading the `placements` counter back (2 -> 3).
- [x] The seam's grammar - `--place-anchor tray|window` plus `--place-monitor L,T,R,B`, reusing
      `--place-work`/`--place-dpi`/`--place-edge`; the geometry overrides apply only while the tray is
      the anchor, so `--place-anchor` alone is what a verifier varies.

## Conclusion (findings)
_Pending the user's declaration._

## Learnings
- The surface's monitor came from `MonitorFromWindow(window)` - the surface's own monitor, which a
  hidden window keeps stale - so a tray that moved to another monitor did not move the surface.
  Take it from the notification area instead: `FindWindow("Shell_TrayWnd", null)` then
  `MonitorFromWindow(bar)`, which tracks the tray. `PlacementChoice.Choose(trayFound, askedForWindow)`
  is the pure rule; `SystemPlacement.Read(hwnd, preferTray)` is the only OS read.
- `WindowHook.Add` (EXP14) is the way to add a window-message hook: `SourceInitialized` has already
  fired because `Theming.Bind` calls `EnsureHandle()` in the surface's constructor. This is why EXP09's
  own re-place hook had never run.
- Prove a re-place hook with a real message and an app counter: post `WM_DISPLAYCHANGE`
  (`PostMessage(hwnd, 0x007E, 0, 0)`) and read `--place-report`'s `placements=` before and after
  (2 -> 3). "It is wired" is worth nothing.
- Gate the `--place-monitor`/`--place-work`/`--place-dpi`/`--place-edge` overrides on the reading's own
  anchor (`real.Anchor == "tray"` in `PlacementScript.Apply`), so `--place-anchor tray|window` is the
  single variable an end-to-end run changes; the window anchor reads the real desktop and the seams
  are inert.
