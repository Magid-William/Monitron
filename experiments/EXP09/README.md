# EXP09 - The surface sits as a bottom-right flyout, near the clock and above the taskbar

## Summary
The surface is a plain window with no position, so Windows cascades it top-left. Place it where a
tray flyout lives - bottom-right of **its own monitor's** work area, above the taskbar - and assert
the position from outside: the window's UI Automation bounding rectangle against the clock's own
rectangle and the taskbar's.

## Hypothesis
The flyout position is derivable from what the OS already reports - the work area of the monitor the
window was launched on and that monitor's taskbar rectangle - plus a per-monitor px->DIP conversion,
with no tray icon and no owner window. Only two things resist:

1. `SizeToContent = Height` means the height is **not known before layout**, so a window placed at
   construction time lands wrong and then grows down over the taskbar;
2. mixed DPI / multi-monitor coordinates - a negative-origin secondary display, 125 % scaling.

**Falsified if:** the window overlaps the taskbar or leaves the work area; its bottom edge is not
above the taskbar's top; its right edge is not in the notification area beside the clock; the window
is placed on the primary monitor's work area instead of its own monitor's; the px<->DIP conversion
lands wrong at 125 %; the rect the app reports differs from the rect UI Automation reads back; the
settings window is pushed off-screen by the move; or the position cannot be re-derived when the work
area changes.

## Methodology & blast radius
- Method:
  1. `src/Monitorn.App/Placement.cs` - a **pure model, no WPF**: `TaskbarEdge`
     (`Bottom|Top|Left|Right`), `Dips.FromPixels`/`ToPixels`, `PlacementGeometry.EdgeOf` (which side
     of a monitor a taskbar or a shrunken work area is docked on), and
     `PlacementGeometry.For(workArea, edge, width, height, margin)` - hug the corner the notification
     area sits in (bottom->bottom-right, top->top-right, left->bottom-left, right->bottom-right),
     then clamp so the box never crosses the work area's top/left. `PlacementTarget.From(monitor,
     workArea, taskbar, dpi)` is the pure parse of one OS reading. Headless `position-test.ps1`.
  2. `src/Monitorn.App/SystemPlacement.cs` - the thin OS read: `MonitorFromWindow` (the window's own
     monitor, `MONITOR_DEFAULTTONEAREST` - the monitor it was launched on, as today), `GetMonitorInfo`
     (`rcMonitor`, `rcWork`), `GetDpiForMonitor`, and the taskbar's own rectangle from
     `Shell_TrayWnd`. A `PlacementReading` is what the OS said; a scripted reading stands in for it.
  3. `src/Monitorn.App/PlacementApp.cs` - `Place.Attach(window, read, margin)`: place once the window
     has a handle and again on `Loaded` and on `SizeChanged` (the `SizeToContent` trap), and re-place
     on `WM_SETTINGCHANGE` / `WM_DISPLAYCHANGE` / `WM_DPICHANGED`; plus `Place.Clamp(window)` for the
     settings window, which keeps `CenterOwner` but must not hang off the screen.
  4. `src/Monitorn.App/Program.cs` / `SettingsWindow.cs` - the surface is placed after layout (never
     the settings window, which keeps its owner-centred rule and only gets clamped). Verification
     seams: `--no-place` (the pre-fix baseline the change is measured against), `--place-margin N`,
     `--place-edge bottom|top|left|right`, `--place-dpi N`, `--place-work L,T,R,B` (stand in for the
     OS reading, the same way `--os-theme` stands in for the theme and `--now` for the clock), and
     `--place-report <dir>` (dump the reading plus the computed rect).
  5. **Verification seam, the assertion:** `verify-position.ps1` launches the app, then reads the
     window's `BoundingRectangle` through **UI Automation** and, independently, the clock's rectangle
     through UI Automation (by **ClassName** - its `Name` is localized) and the taskbar's rectangle
     and the monitor's work area through Win32. The claim becomes numbers: the box is inside the work
     area, it does not intersect the taskbar, its bottom edge is at the taskbar's top, its right edge
     is inside the notification area, and its distance from the clock's right edge is recorded. The
     app's own reported rect must equal the read-back.
  6. Tests: `PlacementTests.cs` + `position-test.ps1` (headless: the edge rule, the four corners, the
     clamp, the conversion, and this machine's recorded rectangles). `verify-position.ps1` (end to
     end: the real window, the baseline, a driven edge change, the settings clamp).
  7. Regressions: `model-test` / `writer-test` / `schedule-test` / `persistence-test` and
     `verify-control` / `verify-settings` / `verify-schedule` / `verify-persistence` / `verify-theme`.
- May touch: branch `EXP09` off `EXP08`; `experiments/EXP09/README.md`; the overview row;
  `src/Monitorn.App/` (`Placement.cs`, `SystemPlacement.cs`, `PlacementApp.cs`, `PlacementTests.cs`,
  `position-test.ps1`, `verify-position.ps1`, `Program.cs`, `SettingsWindow.cs`, `build.ps1`).
- Off-limits: **any monitor write** (the verifier runs `--no-schedule`); DDC/CI and WMI; the theme;
  the schedule machine and its timings; the `settings.ini` format; the tray icon, autostart,
  z-order, activation and `ShowInTaskbar` (the tray shell's business, still the standing step);
  per-monitor profiles; the registry is **read only**; anything outside
  `D:\Playground\Projects\Apps\Monitorn\`; downloads.
- Escalation budget: every run pins `--no-schedule` and `--settings-file <temp>` and writes into
  `%TEMP%\monitorn-exp09\`; the verifier asserts the real `%APPDATA%\Monitorn\settings.ini` is
  unchanged (content hash, whether or not it exists) and that no monitor value changed. **The user's
  real taskbar is never moved and the real display layout is never changed**: the edge, the DPI and
  the work area are injected through the seams, the way the clock and the OS theme are injected.

## Open questions (answered or adopted defaults)
- [x] **Which monitor** - the monitor the window was launched on, read from the window's own handle
      (`MonitorFromWindow`), i.e. exactly today's behavior kept; **the user's call**, not the
      taskbar's monitor and not "the primary". Each monitor's own work area already excludes its own
      taskbar, so the rule is per-monitor either way.
- [x] **Exact anchor** - the **user's call**: the work area's bottom-right corner with an 8 DIP
      margin (right edge flush with the taskbar's right edge), *not* the clock's own right edge. The
      verifier still reports the clock's rectangle, so "near the clock" is a number.
- [x] **Margin / inset** - 8 DIP. *Assumption.*
- [x] **Topmost** - no: the flyout stays an ordinary window; z-order and activation belong to the
      tray shell. *Assumption.*
- [x] **Follow work-area changes live** - yes (taskbar moved or resized, auto-hide, display change,
      DPI change), driven through the injected reading. *Assumption.*
- [x] **The settings window after the move** - keeps `CenterOwner`, then clamped into the work area
      so it stays fully on screen. *Assumption: without this it hangs off the bottom-right.*
- [x] **All four taskbar edges** - implemented and headless-tested, though Windows 11 only allows a
      bottom taskbar here, so only bottom is drivable end to end. *Assumption.*
- [x] **Auto-hide taskbar** - the work area equals the full screen, so the window sits at the
      screen's bottom-right over where the taskbar appears; asserted only as "inside the work area".
      *Assumption.*
- [x] **Does this displace the tray shell?** - **the user's call**: no. No tray icon, no autostart, no
      z-order change; the tray shell stays the standing next step. *One experiment, one hypothesis.*
- [x] **Spike or full** - full: the pure model and the verifier are small, and the position needs a
      number either way. *Assumption.*

## Conclusion (findings)
_Pending._

## Learnings
- On this build (Windows 11 25H2, 26200) the tray clock is **not** a `TrayClockWClass` child of
  `Shell_TrayWnd` (that lookup returns an empty rect); it is a UI Automation child of the taskbar,
  ClassName `SystemTray.OmniButtonLeft`, and its `Name` is localized ("Clock 4:42 PM ..."). Match it
  by ClassName.
- Recorded geometry of this machine: taskbar `Shell_TrayWnd` `(0,1020)-(1920,1080)`; primary work
  area `(0,0)-(1920,1020)` px = `(0,0)-(1536,816)` DIP at 125 % (120 dpi); the second display is at a
  negative origin `(-2560,-846)-(0,594)` with its own taskbar and work area. Useful fixtures.
- `SystemParameters.WorkArea` is the primary monitor's work area in DIPs, so it cannot place a
  window on the monitor the window is actually on; `MonitorFromWindow` + `GetMonitorInfo` + the
  monitor's own DPI can.
