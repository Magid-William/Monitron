# Experiments overview

One experiment = one hypothesis. Status is `todo | inProg | success | failed | abandoned`;
only the user declares the final status.

| EXP | Title | Status | Branch | Started | Concluded | Record |
|---|---|---|---|---|---|---|
| EXP01 | Prior art: monitor brightness control & scheduled dimming | failed | EXP01 | 2026-10-14 | 2026-10-14 | [experiments/EXP01/README.md](experiments/EXP01/README.md) |
| EXP02 | Detect monitors and control their brightness (sampling spike) | success | EXP02 | 2026-10-15 | 2026-10-15 | [experiments/EXP02/README.md](experiments/EXP02/README.md) |
| EXP03 | Per-monitor control surface (slider + gear), UI only | success | EXP03 | 2026-10-15 | 2026-10-15 | [experiments/EXP03/README.md](experiments/EXP03/README.md) |
| EXP04 | Dummy "Schedule night light" settings window, UI only | success | EXP04 | 2026-10-15 | 2026-10-15 | [experiments/EXP04/README.md](experiments/EXP04/README.md) |
| EXP05 | Bring brightness to life: real detection + real writes on the surface | success | EXP05 | 2026-10-15 | 2026-10-15 | [experiments/EXP05/README.md](experiments/EXP05/README.md) |
| EXP06 | Apply the night-light schedule (scheduler FSM + real monitors) | success | EXP06 | 2026-10-15 | 2026-10-15 | [experiments/EXP06/README.md](experiments/EXP06/README.md) |
| EXP07 | Persist the schedule settings (a file behind the window) | success | EXP07 | 2026-10-15 | 2026-10-15 | [experiments/EXP07/README.md](experiments/EXP07/README.md) |
| EXP08 | Dark mode: the app's windows follow the Windows app theme | success | EXP08 | 2026-10-15 | 2026-10-15 | [experiments/EXP08/README.md](experiments/EXP08/README.md) |
| EXP09 | Screen position: the surface as a bottom-right flyout, near the clock | success | EXP09 | 2026-10-15 | 2026-10-15 | [experiments/EXP09/README.md](experiments/EXP09/README.md) |
| EXP10 | Raise the brightness regressions to N displays | success | EXP10 | 2026-10-15 | 2026-10-15 | [experiments/EXP10/README.md](experiments/EXP10/README.md) |
| EXP11 | The tray shell: notification-area icon, Run on Windows startup, Quit | success | EXP11 | 2026-10-15 | 2026-10-15 | [experiments/EXP11/README.md](experiments/EXP11/README.md) |

**EXP01:** status `failed` is the agent's recommendation, pending the user's declaration —
the hypothesis ("no mainstream tool combines DDC/CI brightness with a user schedule") was
falsified by prior art (Twinkle Tray already does it). Recommend **EXP02 = DDC/CI hardware
probe + `Monitorn.Core` skeleton**.

**EXP02:** `success`. Detection and reversible brightness control proven on this machine —
external over DDC/CI (`dxva2`, VCP `0x10`), internal over WMI — built and run with only the
in-box .NET Framework 4.8 toolchain (0 MB). Recommend **EXP03 = scheduler FSM** (injected clock
+ fake `IBrightnessController`), with the product-stack decision (lean .NET Framework 4.8 WPF)
as its own call.

**EXP03:** `success` is the agent's **recommendation, pending the user's declaration** — the
hypothesis held (the surface renders and drives, see the record). UI-only spike: the per-monitor
control surface (name + 0-100 slider + gear, one row per monitor) from hardcoded sample data, in a
net48 WPF window built by the in-box `csc` (0 MB), code-only (no XAML). No DDC/CI, no WMI, no
writes. It also settles the stack EXP02 left open: **no .NET SDK is installed**, so the UI builds
against the in-box WPF assemblies rather than as `net8.0-windows`. Two consequences are open for
the user: `README.md` still claims **.NET 8**, and `src/probe/Probe.cs:107` no longer compiles (its
`SetMonitorBrightness` declaration lost its method name). Recommend **EXP04 = scheduler FSM**
(injected clock + fake `IBrightnessController`), the experiment EXP03 displaced.

**EXP04:** `success`. The dummy "Schedule night light" settings window is built and driven: strength,
gradual fade, and the two timing options (sunset-to-sunrise or set hours with turn-on/turn-off), in
the same in-box net48 code-only WPF (0 MB), opened by the gear that EXP03 left inert. Proven by
driving, not describing — **38** assertions on the headless `ScheduleSettings` model, **30** UI
Automation checks on the real window (mode coupling, the On switch gating the body, `999` refused),
and **6** that the gear opens it by keyboard and by automation. Two defects were found only by
looking: committing text on `LostFocus` made the fields undrivable, and a window without a stated
`WindowStartupLocation` cascaded instead of centring on the surface. Still nothing saved, nothing
applied, no monitor touched; the sunset range is a placeholder. Note this makes EXP03's "the gear is
inert" obsolete. Recommend **EXP05 = scheduler FSM** (injected clock + fake `IBrightnessController`),
which this window now supplies the settings shape for — then real detection into the EXP03 rows, the
tray shell, and persistence. One learning from it was **promoted into `AGENTS.md`** (the one
promotion allowed, approved by the user): UI claims are verified by driving the window through UI
Automation patterns and asserting the state it reports, never by `SendKeys`.

**EXP05:** `success` is the agent's **recommendation, pending the user's declaration**. Joins EXP02's
actuator to EXP03's surface: the app detects the **active rendering targets**, reads each one's real
brightness into its row, and a slider drives the real monitor over DDC/CI (or WMI for an internal
active panel), **debounced** so the final value wins. Rows are active targets only and the write
model is debounce, both the user's call; the two repo facts EXP04 flagged (`README.md`'s `.NET 8`
claim and `src/probe/Probe.cs:107`) are fixed here as the user asked. **Run 2026-10-15:** the one
active target (AOC `Q27G3G3R3`) is detected and shown at its real 30; a slider drove it
**30 -> 20 -> 30** with the independent read-only `probe.exe` following each step; 19 headless
debounce assertions and 11 UI Automation checks, all passing. It is the first experiment here whose
claim rests on an outside witness rather than a screenshot, and the first to prove the
`IBrightnessController` seam. The internal-WMI path is implemented but not exercised (no internal
*active* target on this machine). Nothing is persisted and no schedule is applied - the schedule was
the next experiment, which branched off this tip and kept this actuator.

**EXP06:** `success`. Applies the schedule EXP02→05 kept deferring. `NightLightScheduler`
(`Schedule.cs`) derives its phase from an injected clock - day, fade in, night, fade out - fades
every active display to the strength at turn-on, holds, and hands the captured day value back at
turn-off, with the thesis' "a manual move ends the schedule for the night" rule. The settings window
and the scheduler share one `ScheduleSettings`, so an edit applies on the next tick, and a status
line on the surface names the phase. **Run 2026-10-15:** 114 headless assertions driving the machine
with a clock the test moves, and 24 checks end to end - a scripted night (`--now`/`--rate 30`, so a
whole night takes ~50 s) watched by the independent probe on the real AOC: **41 -> 40 -> 37 -> 35 ->
33 -> 31**, held, a manual move to 41 **not** undone until the next turn-on, and the display left at
its original 41 when the app closed. Driving it surfaced a UI defect the code did not show: the row's
slider did not follow the schedule's own write, so the row could show one value while the monitor
held another (the writer's `Written` event now syncs it, guarded against reading its own write as a
user move). Nothing is persisted yet; sunset mode is inert by construction (turn-on == turn-off), as
EXP04 left it. Recommend **EXP07 = persistence** - the settings and the schedule surviving a reboot,
behind this window - then the tray shell + autostart that makes an overnight dim useful with no
window open; real sunset/sunrise by location and per-monitor profiles after that.

**EXP07:** `success` (user-declared). The follow-on EXP06 asked for: the six schedule settings
(switch, strength, gradual fade, mode, turn-on, turn-off) now live in `%APPDATA%\Monitorn\settings.ini`,
loaded at launch into the same object the window edits and the scheduler reads, and written on every
real edit. `SettingsPersistence.cs` splits a pure `SettingsCodec` (text <-> model, writing *through
the model's transitions*, so a corrupt `strength=999` is refused by the same rule the UI enforces)
from a thin `SettingsStore` (tolerant read, atomic temp-then-replace write, failure reported not
thrown); `ScheduleSettings` gained a `Changed` event that fires only on a real transition, which is
what keeps the window's own re-render from writing the file. **Run 2026-10-15:** 42 headless
assertions and 27 UI Automation checks - a first run shows the defaults and writes nothing; strength
55 + gradual fade off is written, survives a restart, and is not rewritten by opening and closing; a
corrupt file starts on the defaults; and the surface's own gear writes the same file. Regressions:
38 / 19 / 114 headless, 30 / 11 / 24 end to end, all passing. Monitor state and per-monitor profiles
stay out of scope (they need identity); the real per-user file was never created by any run.
Recommend **EXP08 = the tray shell + autostart** (which also decides what closing does), then real
sunset/sunrise by location, then per-monitor profiles.

**EXP08:** `success` (user-declared). Dark mode, as the app's own UI theme rather than a monitor
feature: the user declared it, so it displaced the tray shell for one experiment. The light palette
was duplicated in `Program.cs` and `SettingsWindow.cs`; `Theme.cs` is now the one palette (two pure
models - the colors as plain `0xAARRGGBB`, and the rule that turns *what Windows says* plus *what the
user asked for* into light or dark), `SystemTheme.cs` reads `AppsUseLightTheme`, follows a colour-set
change live through a `WM_SETTINGCHANGE` hook, and asks DWM (`DwmSetWindowAttribute(20)`) for a dark
title bar, and `Theming.cs` paints a window from shared mutable brushes plus a template each for the
controls whose look the OS theme owns. **Run 2026-10-15:** 34 headless assertions and 42 end-to-end
checks, with the regressions green (38 / 19 / 114 / 42 headless; 30 / 11 / 25 / 27 end to end). The
claim is asserted on **rendered pixels**, not a screenshot - UI Automation cannot see color, so
`--theme-report` renders each window and samples named regions of the real visual tree - and the same
report with `--theme-no-templates` is the baseline that *measures* the hypothesis: a pure brush swap
leaves the Slider's track and the CheckBox/RadioButton marks light (0.43 / 0.36 / 0.29 of pixels)
while a TextBox's and a Button's face already go dark from a property assignment. Two defects were
found only by driving it: a brush used as a `Setter` value in a sealed `ControlTemplate` is frozen,
so the first live switch crashed; and a report written in the same dispatcher turn rendered the
pre-change tree. Boundaries: only this app's controls were templated (no ComboBox/ScrollBar/ListBox),
high contrast is unaudited, `WM_SETTINGCHANGE` arriving from Windows itself was not observed (the
live path was driven through an injected OS reading), the light *look* is not byte-identical to
pre-EXP08 because four control kinds are now drawn by our templates, and nothing is persisted - the
theme follows the OS. `verify-schedule.ps1` was also given a temp settings file, the same correction
EXP07 made to `verify-settings.ps1`, because an inherited `gradualFade=false` made its fade phase
unobservable. Recommend **EXP09 = the tray shell + autostart** (still the standing step), then a
stored `theme=` preference plus a switch in the settings window - small now - then real
sunset/sunrise, then per-monitor profiles.

**EXP09:** `success` (user-declared). The screen position, the user's ask - it took the number EXP08
had recommended for the tray shell, which stays the standing step, now EXP10+. The surface no longer
cascades: it opens in the notification area's corner of **the monitor it is on** (`MonitorFromWindow`
+ `GetMonitorInfo`'s `rcWork` + `GetDpiForMonitor` - not `SystemParameters.WorkArea`, which is only
the primary's and only in DIPs), 8 DIP in from that monitor's work-area right edge and 8 DIP above its
taskbar. `Placement.cs` is the pure rule (which edge a taskbar is docked on, the corner that implies,
the clamp that keeps the box on screen); `SystemPlacement.cs` is the only part that reads the OS;
`PlacementApp.cs` attaches it to the window on `Loaded` and re-runs it on `SizeChanged` (because
`SizeToContent = Height` makes the height arrive after the first layout) and on
`WM_SETTINGCHANGE`/`WM_DISPLAYCHANGE`/`WM_DPICHANGED`; the settings window keeps `CenterOwner` and
gains a work-area clamp. The reading is injectable (`--place-edge`, `--place-dpi`, `--place-work`,
`--place-after`, `--place-report`; `--no-place` is the baseline), so a verifier drives another taskbar
edge, another DPI or a work area that shrinks while the window runs without touching the user's
desktop. **Run 2026-10-15:** 63 headless assertions and 60 end-to-end checks, asserted from outside
the app - UI Automation's bounding rectangle for the window *and for the clock* (a taskbar button,
ClassName `SystemTray.OmniButtonLeft`; `TrayClockWClass` is gone on 25H2 and `TrayNotifyWnd` is a
childless leaf) and Win32's work area and taskbar - with the app's own report agreeing with both
exactly, pixel for pixel. Placed `(1410,575)-(1910,1010)` px: the 8 DIP margin both ways and 45 px
right of the clock's right edge; unplaced, the old cascade `(96,96)-(596,531)`. The second display
(a negative origin, its own `Shell_SecondaryTrayWnd`) is respected per monitor, and both windows stay
on screen - the clamp is a no-op at the default width and the fix when the settings window is wider
than the surface (measured at `--width 320`). Regressions 38 / 19 / 114 / 42 / 34 headless and
30 / 27 / 42 end to end. Boundaries: only the bottom taskbar is real on this machine (the other edges
are the model's, one of them driven through the seam), `WM_SETTINGCHANGE` itself is wired but not
injected, auto-hide is untested, and the second display's placement is injected rather than chosen.
**`verify-control.ps1` and `verify-schedule.ps1` could no longer run to completion**: this machine now
reports **two** active targets (an internal panel beside the AOC) and both assert exactly one row -
proven not to be this change (identical failures on EXP08's own build, and the independent probe says
"active targets: 2"), so the brightness path and the scripted night currently have no working
end-to-end regression. Recommend **EXP10 = relax those two verifiers to N displays**, then the
standing **tray shell + autostart** - the corner this experiment proved is the geometry that shell
opens into - then the stored `theme=` preference, real sunset/sunrise, per-monitor profiles.

**EXP10:** `success` (the agent's recommendation, pending the user's declaration). The experiment
EXP09 named first: the two single-row verifiers, relaxed to N displays, because this machine now
reports **two** active targets (an internal panel beside the AOC) and both asserted exactly one row -
which left the brightness path and the scripted night with no working end-to-end regression.
`Program.cs` gains one seam (each row's slider and gear carry `monitor.Id` - `DDC:<device path>` /
`WMI:<InstanceName>` - as their UI Automation id, so a row is bound to a display instead of guessed
at), and each verifier drives **every** row against its **own** outside witness: the read-only EXP02
probe for the DDC row, an independent `Get-CimInstance` WMI read for the panel row (`Display 1`, EDID
`BOE0764`). **Run 2026-10-15:** `verify-control` 28 checks (22 with `-SkipInternal`) and
`verify-schedule` 35, 0 failed, with the regressions green (38 / 19 / 114 / 42 / 34 / 63 headless;
30 / 27 / 42 / 60 end to end). The panel was driven 67 -> 57 -> 67 and the AOC 57 -> 47 -> 57; a
scripted night at strength 47 faded both (the AOC staircase 57 -> 56 -> 54 -> 52 -> 48 -> 47), held
both, honoured a manual move on the AOC **while the panel stayed held** (per-display override, which
a one-row run could not have observed), ramped the panel back to its own 67, left the AOC its 57 and
every display at its original value. **The WMI path is exercised for the first time** - EXP05's
boundary (implemented, never exercised) - and two defects were found only by driving: a reused `$p`
loop variable clobbered the process object and left a night running on the desktop, and a
non-overridden display is mid-ramp at turn-off so its restore needs a poll. Boundaries: the panel's
staircase is not sampled (a WMI read is slower than a fade step), a row is a count-matched witness
rather than an identified one (the probe names a device, not a row), and `verify.ps1`'s stale
`--rows` help line is left alone. One learning from it was **promoted into `AGENTS.md`** (the one
promotion allowed, approved by the user, so that budget is spent): *verify once, when the experiment
is done - not after every edit*. Recommend **EXP11 = the tray shell + autostart** - the standing
step, now with its regressions back and the flyout corner EXP09 proved - then the stored `theme=`
preference, real sunset/sunrise, per-monitor profiles.

**EXP11:** `success` (user-declared). The standing step since EXP07, and the app now lives in the
notification area: a runtime-drawn icon with a three-line menu - **Show/Hide**, a checkable **"Run on
Windows startup"**, and **Quit**. Closing the window now hides to the tray, so the schedule keeps
dimming with no window open; Quit is the only exit and runs the restore path. `TrayIcon.cs` is the
shell half (`Shell_NotifyIcon` on the surface's own HWND, so the icon survives a Hide and is gone
exactly when the app exits), `Tray.cs` the pure Show/Hide state, and `AutoStart.cs` the Run command
and its enable/disable over an injectable store; `Theming.cs` gained a ContextMenu/MenuItem template
so the menu follows the EXP08 palette. Seams: `--no-tray` (the pre-tray lifecycle, which every older
verifier now passes), `--hidden` (what the Run entry launches), `--tray-menu`, `--startup-value`,
`--tray-report`, `--quit-after`. **Run 2026-10-15:** 35 headless assertions and 55 end-to-end checks,
regressions green (38 / 19 / 114 / 42 / 34 / 63 / 35 headless; 60 / 27 / 30 / 42 / 28 / 35 / 55 end to
end). The icon is witnessed by the shell itself (`Shell_NotifyIconGetRect`, because Windows hides a
new icon in the overflow, where no taskbar button exists); the Run entry by `Get-ItemProperty` on
`HKCU\...\Run` under a throwaway value name (`"<exe>" --hidden` written and removed, the user's real
autostart untouched); Show/Hide and close-to-tray by Win32 `IsWindowVisible` plus the process still
running; and Quit's restore by the read-only probe (a scripted night dimmed the AOC to 76, Quit
returned its day 78). The menu's theming is measured as luminance (0.15 dark vs 0.96 light). Three
defects were found by driving and by looking: `TogglePattern.Toggle()` raises `Checked`, not `Click`,
so the startup handler never ran; the OS `ContextMenu` template paints `SystemColors`, so it needed
its own template; and a `Collapsed` check mark takes no space, so the reserved column vanished -
`Hidden` now, asserted by "the menu does not resize when a line is checked". Boundaries: the tray
attaches to the full surface, not the `--settings` spike; the Run key is the single source of truth;
session end restores; a refused icon falls back to the ordinary window lifecycle; single-instance is
out of scope. No promotion into `AGENTS.md` (that budget was spent in EXP10). Recommend **EXP12 = the
stored `theme=` preference** (small now, and the tray menu joins it), then real sunset/sunrise by
location, then per-monitor profiles.
