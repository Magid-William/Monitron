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
     OS reading, the same way `--os-theme` stands in for the theme and `--now` for the clock),
     `--place-report <dir>` (dump the reading plus the computed rect), and `--place-after <sec>
     L,T,R,B` (a work-area change while the window runs - the re-place a `WM_SETTINGCHANGE` would
     call, as `--theme-toggle-after` drives the theme's live path).
  5. **Verification seam, the assertion:** `verify-position.ps1` launches the app, then reads the
     window's `BoundingRectangle` through **UI Automation** and, independently, the clock's rectangle
     through UI Automation (by **ClassName** - its `Name` is localized) and the taskbar's rectangle
     and the monitor's work area through Win32. The claim becomes numbers: the box is inside the work
     area, it does not intersect the taskbar, its bottom edge is at the taskbar's top, its right edge
     is inside the notification area, and its distance from the clock's right edge is recorded. The
     app's own reported rect must equal the read-back. Then the same reading is turned into
     measurements: the unplaced baseline, a work area that changes while the window runs, a narrower
     margin, a top taskbar, a 96 dpi reading, the settings window's clamp, and the second display's
     own work area and taskbar.
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

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11 25H2, build 26200; two displays, both at 125 %).

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitorn.App\build.ps1` | `Monitorn.App.exe` from the in-box `csc`, **0 MB** (`Placement.cs`, `SystemPlacement.cs`, `PlacementApp.cs` added) |
| Machine | `position-test.ps1` | **63 assertions, 0 failed**: both of this machine's displays as fixtures, the four edges from the work area and from the taskbar's own rectangle, the corner each implies, the clamp, the negative-origin arithmetic, the px<->DIP conversion |
| End to end | `verify-position.ps1` | **60 checks, 0 failed**: the placed window against UI Automation, against Win32, against the app's own report, the `--no-place` baseline, a work-area change while it runs, a 40 DIP margin, a top taskbar, a 96 dpi reading, the settings clamp, and the second display's own work area and taskbar |
| Regression | `verify-settings.ps1` / `verify-persistence.ps1` / `verify-theme.ps1` | **30 / 27 / 42**, 0 failed |
| Regression | `model-test` / `writer-test` / `schedule-test` / `persistence-test` / `theme-test` | **38 / 19 / 114 / 42 / 34**, 0 failed |

The numbers, all in the OS's own physical pixels on the primary display
(`\\.\DISPLAY2`, 1920x1080 at 125 % -> a work area of `(0,0)-(1536,816)` DIP):

| what | value |
|---|---|
| taskbar (`Shell_TrayWnd`) | `(0,1020)-(1920,1080)`; the notification area starts at x 1524; the clock is `(1778,1020)-(1865,1080)` |
| unplaced (`--no-place`) | `(96,96)-(596,531)` - the OS cascade, as it has been since EXP03 |
| placed | `(1410,575)-(1910,1010)` = `(1128,460)-(1528,808)` DIP |
| clearance both ways | 10 px = the 8 DIP margin at 120 dpi |
| the window's right edge | 45 px right of the clock's right edge (the Show Desktop strip) |
| second display | `\\.\DISPLAY1` `(-2560,-846)-(0,594)`, its work area ends at 534, its own taskbar `Shell_SecondaryTrayWnd` `(-2560,534)-(0,594)`; the window lands at `(-510,89)-(-10,524)` |

Findings:
- **The hypothesis holds, and the position is derivable from what the OS reports.** No tray icon, no
  owner window, no fixed coordinates: the monitor the window is on (`MonitorFromWindow`), its
  `rcWork`, that monitor's DPI and its taskbar's rectangle are enough. The app's computed rectangle
  and the two outside witnesses agree **exactly** - the DIP rect times the scale equals both the
  `GetWindowRect` and the UI Automation `BoundingRectangle`, so on this window there is no
  invisible-DWM-border offset to explain away.
- **Both predicted resisters are real.** `SizeToContent = Height` means the height is 0 until the
  window has been laid out, so the placement has to run again on `Loaded`/`SizeChanged` (the
  `SourceInitialized` pass finds nothing to place and returns). And the px<->DIP conversion is
  load-bearing: with `--place-dpi 96` the same window landed 480 px further right (1890 vs 1410) and
  half off the screen - what a forgotten conversion looks like.
- **The clamp is insurance, and only the narrow case needs it.** At the default 400 DIP surface,
  centring the settings window on its owner already fits inside the work area (`(1423,600)-(1898,985)`
  against a bottom of 1020), so the clamp did nothing. With `--width 320` the centred window hung
  28 px off the right edge and the clamp put it exactly at the clamped position. Measured both ways
  rather than assumed.
- **"Near the clock" is a number now.** The window's right edge reaches 45 px past the clock's right
  edge - inside the notification area's column, no further right than the Show Desktop strip.
- **The second display is per-monitor correct, not primary-assumed.** A work area at a negative origin
  with its own taskbar: the window landed inside that work area, right-aligned to it, above that
  display's own taskbar (`Shell_SecondaryTrayWnd`), 10 px clear.

Boundaries this run does not cross:
- **Only the bottom edge is real on this machine.** The other three corners are asserted in the model
  and one of them (`top`) physically, through the seam; a real top/left/right taskbar was not driven.
- **`WM_SETTINGCHANGE` itself was not observed arriving from Windows.** Moving the user's real taskbar
  is not a verifier's to do, so the live re-place was driven through the injected reading 4 s into a
  run (`--place-after`) - the analogue of `--theme-toggle-after`.
- **Auto-hide was not exercised** (it would mean changing the user's taskbar). The model's answer is
  "the work area is the whole screen, so it hugs the screen's corner", headless-tested only.
- **The second display's placement is injected, not chosen.** The window is put there by handing the
  app that display's work area; the app is not given a way to *pick* another monitor (it places on
  the monitor it is on, which is the user's stated rule).
- **Not the tray shell.** No icon, no autostart, no z-order or activation change: the surface is
  still an ordinary taskbar window and still restores the day value when it closes.
- **`verify-control.ps1` and `verify-schedule.ps1` could not run to completion, and it is not this
  change.** Both assert exactly one slider, and this machine now reports **two** active targets (an
  internal panel, `Display 1`, besides the AOC `Q27G3G3R3`); the independent `probe.exe` says
  "active targets: 2", and the same two failures reproduce on EXP08's own build with EXP09 checked
  out. They need relaxing to N rows in a later experiment.

## Next experiment (suggested)
- **Relax the two single-row verifiers to N displays** - `verify-control.ps1` and
  `verify-schedule.ps1`. Both hardcode "exactly one slider" and stop before driving anything on a
  machine with two active targets, which is this machine now (an internal panel beside the AOC), so
  the brightness path and the scripted night currently have **no** working end-to-end regression. It
  is a verifier change, not a product change: find the rows by name/id, drive the one bound to a real
  external monitor, and keep the probe as the witness. Small, and it un-blocks the regressions every
  later experiment should be able to lean on.
- The overview's standing step is unchanged and still comes after this: the **tray shell + autostart**
  (which also decides what closing does), then a stored `theme=` preference plus a switch, then real
  sunset/sunrise, then per-monitor profiles. The flyout corner this experiment proved is the
  geometry that shell will open into.

## Conclusion (findings)
_Pending._

## Learnings
- On this build (Windows 11 25H2, 26200) the tray clock is a UI Automation child of **`Shell_TrayWnd`
  itself**, ClassName `SystemTray.OmniButtonLeft`; `TrayClockWClass` no longer exists (an empty rect)
  and `TrayNotifyWnd` is a childless **leaf sibling** of it, so searching inside the tray pane finds
  nothing. Match the clock by ClassName - its `Name` is localized ("Clock 4:42 PM ...").
- In a PowerShell scriptblock turned into a native callback, `ClassName $h -eq 'X'` binds `-eq` as a
  *parameter name* of the `ClassName` function, matches nothing and reports no error. Read it into a
  variable first (`$c = ClassName $h; if ($c -eq 'X')`). It cost a bogus "taskbar" from another app.
- `SystemParameters.WorkArea` is the primary monitor's work area in DIPs, so it cannot place a window
  on the monitor the window is actually on; `MonitorFromWindow` + `GetMonitorInfo` + the monitor's own
  DPI can, negative origins included.
- Windows 11 gives **every** display its own taskbar (`Shell_SecondaryTrayWnd`, with its own work area
  and its own clock strip) and only the primary has the tray clock. `FindWindow("Shell_TrayWnd")` is
  therefore the wrong taskbar for a window on a secondary display.
- Recorded geometry of this machine, useful as fixtures: primary `\\.\DISPLAY2` `(0,0)-(1920,1080)`,
  work area `(0,0)-(1920,1020)` px = `(0,0)-(1536,816)` DIP at 120 dpi, taskbar `(0,1020)-(1920,1080)`,
  clock `(1778,1020)-(1865,1080)`; secondary `\\.\DISPLAY1` `(-2560,-846)-(0,594)`, work area ends at
  534.
- `Window.SizeToContent = Height` leaves `ActualHeight` at 0 until layout, so a placement attached to
  `SourceInitialized` alone silently does nothing; place on `Loaded` **and** on `SizeChanged`, and
  keep `WindowStartupLocation = Manual` or WPF's own centring/cascade fights the coordinates.
- The occupied taskbar area and the notification area are not where the *edges* are: the clock's right
  edge sits ~55 px inside the screen's (~44 DIP at 125 %), so "flush to the work area's right edge"
  still reads as "beside the clock". Both are worth asserting, they are different numbers.
- `verify-control.ps1` / `verify-schedule.ps1` assume **exactly one** active target; on a machine with
  an internal panel plus an external one they stop before driving anything. Check
  `probe.exe`'s "active targets" before reading their result as a regression.
