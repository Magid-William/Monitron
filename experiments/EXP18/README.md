# EXP18 - The tray icon survives a taskbar replacement

## Summary
The icon is added once at launch and `TaskbarCreated` is ignored, so it is invisible to a taskbar
replacement that cannot enumerate existing icons (komorebi-bar) and is lost after an Explorer restart
or a primary-DPI change. Re-add it when the message arrives.

## Hypothesis
The shell and taskbar replacements broadcast `TaskbarCreated` when every tray icon has been dropped;
komorebi-bar's `systray-util` cannot enumerate existing icons on Windows 11, so it broadcasts it at its
own startup and shows whoever re-adds. If the app registers that message on the tray window and
re-adds the icon from the same `NOTIFYICONDATA` it uses at launch - falling back to `NIM_MODIFY` when
`NIM_ADD` is refused because the real shell still holds the icon - then the icon appears in
komorebi-bar, survives an Explorer restart and a primary-DPI change, and keeps its click routing.

Falsified by: the icon still missing from komorebi-bar after the bar restarts; the broadcast not
re-adding the icon; the icon vanishing from the shell after it; or the re-add costing the icon its
id, tooltip, or click handling.

## Methodology & blast radius
- Method: the tray's shell half (`TrayIcon.cs`) registers `TaskbarCreated` with
  `RegisterWindowMessage` before the first `NIM_ADD`, and its WndProc hook handles that message
  before its own callback message: re-add from one shared `NotifyData()` (id 1, the same icon and
  tooltip), `NIM_ADD` first and the same struct as `NIM_MODIFY` if the shell refuses the add. An
  `adds` counter in the existing `--tray-report` file is the app's testimony that the handler ran.
  The pure decision (`TrayShell.IsTaskbarCreated`, defensive about a failed registration returning 0)
  lives in `Tray.cs` and is driven by `tray-test.ps1`. The end-to-end claim is driven by
  `verify-tray.ps1`: it launches the real autostart state (`--hidden`), posts the registered message
  to the app's own top-level HWND (the same id a broadcast carries, without poking every other tray
  app), and asserts the app's `adds` counter moved and the shell still answers
  `Shell_NotifyIconGetRect` for that window with a real rectangle. The real broadcast is proven by
  restarting komorebi-bar against the running app and looking at the bar.
- May touch: `src/Monitron.App/TrayIcon.cs`, `Tray.cs`, `TrayTests.cs`, `verify-tray.ps1`,
  `build.ps1` (header comment), this record, `experiments-overview.md`. `Program.cs` only if a
  comment needs naming.
- Off-limits: the schedule, brightness, the settings schema, the theme/menu layout, placement, the
  `--no-tray` lifecycle, per-monitor code, `docs/prior-art/`, and any workaround for komorebi-bar's
  failure to enumerate existing icons (no polling, no extra registration, no auto-restart).

## Open questions (known unknowns)
- [x] How the verifier triggers the message - a targeted `PostMessage` of the registered id to the
      app's own HWND: the same message id a broadcast carries, deterministic, and it does not poke
      every other tray app on the desktop. The `HWND_BROADCAST` path is proven by restarting
      komorebi-bar, not by the script. (assumption, default taken)
- [x] What the app reports - `adds=<n>` in the existing `--tray-report` file (1 at launch, +1 per
      `TaskbarCreated`); no new flag. (assumption, default taken)
- [x] A refused re-add - the app keeps running and tries again on the next broadcast; the tray is
      never turned off after startup. (assumption, default taken)
- [x] Unit test of the message match - three assertions in `tray-test.ps1`, so the
      "0 must never match" guard is asserted. (assumption, default taken)
- [x] Scope - only the standard `TaskbarCreated` contract; komorebi-bar's inability to enumerate
      existing icons is its own problem. (assumption, default taken)

## Conclusion (findings)
_Pending._

## Learnings
- `RegisterWindowMessage("TaskbarCreated")` returns the same id in the verifier as in the app - post it to
  the app's own HWND (`PostMessage`) instead of `HWND_BROADCAST`, which pokes every tray app on the desktop.
- The real repro is a bar restart: `Stop-Process -Name komorebi-bar` (komorebi restarts it) makes the bar
  re-broadcast; the fixed build reappears in the bar, the pre-fix build does not. Shots in
  `%TEMP%\monitron-exp18\bar-{before,after-restart,prefix-after-restart,fixed-final}-right.png`.
- komorebi-bar answers `Shell_NotifyIconGetRect` from a `Shell_TrayWnd`-class window of its own with
  implausible rects (e.g. `2029,2029-2717,2717`), so use that call as a presence witness, not a place one,
  and never compare its exact numbers with the app's reading across runs.
- A pre-fix binary for a red/green comparison: `git stash push -- <changed files>`, `build.ps1`, copy the
  exe, `git stash pop`, rebuild, then `verify-tray.ps1 -Exe <copy>` - use when a new check must be shown to
  fail for the reason it names.
- `verify-tray.ps1`'s "the shell no longer has an icon for that window" already fails on main on this
  machine (komorebi keeps answering for the dead HWND); every other old check passes on the pre-fix build,
  so only the new scenario is attributable to this change.
