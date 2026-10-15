# EXP11 - The tray shell: a notification-area icon, Run on Windows startup, and Quit

## Summary
Give Monitron a notification-area icon so it can run with no window open: a context menu with
Show/Hide, a checkable "Run on startup", and Quit. The schedule keeps dimming while the surface is
hidden; Quit gives every display back.

## Hypothesis
The app can live entirely in the notification area. Falsifiable as: with the surface hidden the
schedule still drives the displays; the tray menu's Show/Hide toggles the surface; "Run on Windows startup"
writes and removes an `HKCU\...\Run` entry `"<exe>" --hidden` and reads back checked; and Quit
restores every display to its day value and exits. Proven by driving the real menu through UI
Automation plus outside witnesses (the icon in the shell's notification area, the Run key,
`IsWindowVisible`, and the monitors' brightness via the read-only probe / an independent WMI read).

## Methodology & blast radius
- Method: the repo's shape - a **pure model** (`TrayModel` for the Show/Hide state, `StartupEntry`
  for the Run command/naming decision), a **thin OS adapter** (`RegistryStartup` behind
  `IStartupStore`, and the `Shell_NotifyIcon` host in `TrayIcon.cs`), and **seams** so a verifier
  drives the real thing without touching the user's desktop or their autostart:
  `--no-tray` (the pre-tray lifecycle, for the regressions), `--hidden` (start tray-only - what the
  Run entry uses), `--tray-menu` (open the menu at once), `--startup-value <name>` (a throwaway Run
  value name), `--tray-report <dir>` (the app's own reading).
- The window's **close now hides** to the tray; **Quit** is the only exit, and it runs the same
  restore path the old `Closing` handler did. `--no-tray` keeps exit-on-close.
- May touch: new `src/Monitron.App/Tray.cs`, `TrayIcon.cs`, `AutoStart.cs`, `TrayTests.cs`,
  `tray-test.ps1`, `verify-tray.ps1`; edits to `Program.cs`, `build.ps1`, `Theming.cs` (a
  ContextMenu/MenuItem template so the menu follows the EXP08 palette), the existing verifiers (a
  `--no-tray` in the arg line where they close the window and expect exit), `experiments-overview.md`
  and `README.md` if it describes launch behaviour.
- Off-limits: real sunset/sunrise, per-monitor profiles, the stored `theme=` preference,
  single-instance, an installer or code signing, and any Windows "Startup apps" integration beyond
  the Run key.

## Open questions (known unknowns)
- [x] What the window's X does - default **hide to the tray**; `--no-tray` keeps exit-on-close.
- [x] Whether autostart starts hidden - default **yes**; the Run value is `"<exe>" --hidden`.
- [x] Where the tray icon comes from - default **drawn at runtime** (`System.Drawing`
      `Bitmap.GetHicon()`), reused as the window icon; no binary asset.
- [x] Whether the context menu is themed - default **yes**, with the existing palette.
- [x] Whether the Run key is also stored in `settings.ini` - default **no**; the registry is the
      source of truth.
- [x] How the verifier opens the menu - default: try the real notification-area button through UI
      Automation first, fall back to `--tray-menu`, one scenario per closing action.
- [x] Two instances - **out of scope** this experiment.

## Conclusion (findings)
`success` (user-declared). The standing step since EXP07, and the app now lives in the notification
area. A tray icon (drawn at runtime with `System.Drawing`, no binary asset, reused as the window
icon) carries a three-line ContextMenu: **Show/Hide**, a checkable **"Run on Windows startup"**, and
**Quit**. The shell half is `TrayIcon.cs` (`Shell_NotifyIcon` on the surface's own HWND, so the icon
survives a Hide and is gone exactly when the app exits); the pure halves are `Tray.cs` (the Show/Hide
state) and `AutoStart.cs` (the Run command, whether an existing value is ours, and enable/disable over
an `IStartupStore`). **Closing the window now hides to the tray** - the schedule keeps dimming with no
window open, which is what makes an overnight dim useful - and **Quit** is the only exit: it runs the
restore path the old `Closing` handler had, so every display gets its day value back. `--no-tray`
keeps the pre-tray lifecycle for the older verifiers, `--hidden` is what the Run entry launches with,
and `--tray-menu`/`--startup-value`/`--tray-report`/`--quit-after` are the other seams.

**Run 2026-10-15:** 35 headless assertions (`tray-test.ps1`) and 55 end-to-end checks
(`verify-tray.ps1`), with every regression green (38 / 19 / 114 / 42 / 34 / 63 / 35 headless;
60 / 27 / 30 / 42 / 28 / 35 / 55 end to end). The icon's presence is witnessed by the shell itself -
`Shell_NotifyIconGetRect`, not the app's word - and Quit's restore by the read-only probe: a scripted
night dimmed the AOC to 76 and Quit returned it to its day 78. The Run entry is witnessed outside the
app with `Get-ItemProperty` on `HKCU\...\Run`: toggling the line writes exactly
`"<exe>" --hidden`, unchecking removes it, and the user's real autostart is never touched (the run
uses a throwaway value name). The menu follows the app theme, measured as luminance like EXP08: 0.15
dark vs 0.96 light.

Two defects were found only by driving the finished menu: UI Automation's `TogglePattern.Toggle()`
flips a checkable `MenuItem`'s `IsChecked` but raises `Checked`, **not** `Click`, so a handler on
`Click` never ran; and the OS `ContextMenu` template paints `SystemColors`, so assigning
`ContextMenu.Background` alone left the menu light - it needed its own `ControlTemplate`. The user
then caught a third by looking: a check mark set to `Visibility.Collapsed` takes no space, so the
reserved column collapsed and the text sat at the left edge; it is `Hidden` now, and `verify-tray`
asserts the menu does not resize when a line is checked.

Boundaries: Windows puts a new icon in the hidden-icons overflow by default, so the outside witness is
the shell's rectangle for that window rather than a taskbar button (that would need the overflow
flyout opened); the tray attaches to the full surface, not the `--settings` spike; the Run key is the
single source of truth (no `settings.ini` key); a logoff/shutdown restores via `SessionEnding`, and a
refused icon falls back to the ordinary window lifecycle; the icon is a drawn glyph, not a designed
logo; single-instance is out of scope. Nothing is persisted beyond the Run key, and `settings.ini` is
untouched. No learning was promoted into `AGENTS.md` (that budget was spent in EXP10).

## Learnings
- `Shell_NotifyIconGetRect` (verifier side, with the surface HWND + uID 1) - use it as an outside
  witness that a tray icon is registered: Windows puts new icons in the hidden-icons overflow, so a
  UI Automation search of `Shell_TrayWnd` finds no button for them.
- Handle a checkable `MenuItem`'s change on `Checked`/`Unchecked`, never on `Click` - use when an
  automation tool drives it: `TogglePattern.Toggle()` flips `IsChecked` but does not raise `Click`;
  guard the refresh assignment so reading the state back does not fire the handler.
- Give a `ContextMenu` its own `ControlTemplate` - use when theming a menu: the OS template paints
  `SystemColors`, so `ContextMenu.Background` alone leaves it light.
- A menu's check column must be `Visibility.Hidden`, never `Collapsed`: `Collapsed` removes the
  element from layout, so the text slides left and jumps right only when checked.
- `Grid.SetColumn` does not accept a `FrameworkElementFactory`; use `SetValue(Grid.ColumnProperty, n)`
  or a horizontal `StackPanel` with a fixed-width check.
- Pass `--no-tray` in a verifier that closes the window and waits for exit - use that seam whenever a
  test needs the pre-tray lifecycle instead of close-to-tray.
