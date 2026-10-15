# EXP04 - Dummy "Schedule night light" settings window (UI only)

## Summary
Put a settings window behind the gear: a **Schedule night light** section with a strength %, a
**gradual fade** checkbox, and the schedule itself - *sunset to sunrise* or *set hours* with a
turn-on and turn-off time. Dummy: nothing is saved, nothing is applied, no location lookup.

## Hypothesis
A code-only net48 WPF window (same in-box `csc` path as EXP03, 0 MB) can present the whole
night-light schedule - strength input, gradual-fade checkbox, the sunset-to-sunrise vs set-hours
mode choice, and two 12-hour times as `hour : minute AM/PM` - legibly, **and** the rule that
decides which fields are live per mode can be driven and asserted headlessly.

**Falsified if:** the added window cannot be built without a download; or the time fields / mode
coupling cannot be shown legibly at 125 % DPI and a 300-DIP minimum width; or the mode -> field
enablement cannot be driven without a real clock or a UI automation harness.

## Methodology & blast radius
- Method:
  1. New `src/Monitron.App/SettingsWindow.cs`, code-only (no XAML), reusing `Program.cs`'s
     brushes/label style. `build.ps1` gains the second source file.
  2. Window contents, following the requested sketch: header **Schedule night light** with an
     enable checkbox; `Strength [38] %`; `[x] gradual fade`; then the two timing options -
     `Sunset to sunrise (12:00 AM - 12:00 AM)` and `Set hours` with
     `Turn on [5][:][00][PM]` / `Turn off [9][:][00][AM]`; a one-line footer saying it is a dummy.
  3. Gear wiring: pressing a gear opens the settings window (the surface itself is unchanged);
     `--settings` launches straight into it so verification does not depend on focus.
  4. A headless `ScheduleSettings` model (mode, enabled, strength, fade, on/off `TimeSpan`) whose
     transitions decide field enablement and time formatting. Driven by invoking transitions and
     asserting exposed state - no clock, no timers, no WPF, per the AGENTS FSM rule.
  5. Verify by looking and by driving: `verify-settings.ps1` launches, types into the strength
     field, toggles fade, flips the mode and confirms the time fields enable/disable (screenshot
     diff), flips back, screenshots each pass, closes cleanly.
- May touch: branch `EXP04` off `EXP03`; `experiments/EXP04/README.md`; the overview row; the
  `src/Monitron.App/` files named above. Experiment code lands at the repo root per AGENTS; only
  the README lives under `experiments/EXP04/`.
- Off-limits: any DDC/CI, WMI or brightness write; persistence (registry or file); real
  sunset/sunrise (no geolocation, no network, no timezone math); tray icon and autostart;
  `src/probe/**` (its known `Probe.cs:107` compile break is left alone); anything outside
  `D:\Playground\Projects\Apps\Monitorn\`; downloads.
- Escalation budget: none - this cannot touch hardware by construction.
- Numbering: EXP03's recommendation was the scheduler FSM; the settings window was requested
  first, so the scheduler slides one number later (EXP03 did the same to EXP02's suggestion).

## Open questions (assumptions adopted - the user approved the plan)
- [x] "With 2 options" - the two options are the two **timing modes** (Sunset to sunrise |
      Set hours); Strength and gradual fade are modifiers always shown. *Assumption.*
- [x] Where it lives - **one shared, app-level settings window**; the schedule is not per-monitor,
      so any row's gear opens the same one. *Assumption.*
- [x] Time entry - **hour field `:` minute field `AM/PM` toggle**, matching `5 : 00 PM`
      (one free-text `h:mm tt` box was the alternative). *Assumption.*
- [x] Strength entry - **compact numeric `[38] %` field** (EXP03's slider was the alternative).
      *Assumption.*
- [x] Enable toggle - the section header has a checkbox that **gates the body**; the body greys
      out when off. On by default so the surface is visible in the screenshot. *Assumption.*
- [x] Sunset-to-sunrise range - literal `12:00 AM - 12:00 AM` as requested, labelled "set by
      location - not in this experiment"; no lookup. *Assumption.*
- [x] Save / apply - **nothing is persisted**, no OK/Cancel, closing discards. *Assumption.*
- [x] Mode coupling - when Set hours is not selected its time fields are **disabled but keep**
      their `5:00 PM` / `9:00 AM` values. *Assumption.*
- [x] Tests - headless `ScheduleSettings` transitions **plus** the screenshot pass, no framework,
      no fixed ticks. *Assumption.*
- [x] Window shell - **modeless, single instance**, re-opening re-focuses it. *Assumption.*
- [x] Branch - `EXP04` off `EXP03`.

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11, 125 % display scaling), every run exiting cleanly:

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitron.App\build.ps1` | `Monitron.App.exe` from the in-box `csc`, **0 MB downloaded**, no XAML/BAML step |
| Model | `src\Monitron.App\model-test.ps1` | **38 assertions, 0 failed** - 12-hour parsing, midnight/noon, refusal of `101`/`999`/`abc`/empty, and the mode coupling, all driven through the model's own transitions |
| Window | `src\Monitron.App\verify-settings.ps1` | **30 UIA checks, 0 failed**: every control the sketch asks for is in the tree (15 text nodes, 2 checkboxes, 2 radios, 5 numeric fields, 2 AM/PM toggles), the default values, both options, and every driven change; 5 screenshots |
| Coupling | in the same run | selecting *Sunset to sunrise* reports all four time fields disabled and the times still `5:00`/`9:00`; *Set hours* makes them live again |
| Gating | in the same run | the **On** switch off => **0 of 10** body controls live, the switch itself still usable; back on => all 5 fields live |
| Strength | in the same run | set `55` => stuck; set `999` => refused, field reverted to `55` |
| AM/PM | in the same run | the turn-on toggle flips `PM -> AM` (two `AM`, no `PM`) and back, numbers untouched |
| Gear, keyboard | `verify.ps1 -AppArgs '--rows 1' -Keys '{TAB}{TAB}{ENTER}'` | tab to the gear, press it -> foreground becomes `Monitron settings` |
| Gear, automation | `verify-settings.ps1 -AppArgs '--rows 1' -FromGear` | **6 checks, 0 failed**: named gear found, invoking it opens the schedule, closing it leaves the surface alive, invoking again reopens it |
| Surface regression | `verify.ps1` | EXP03's surface still renders (**500x485** at 125 %) and exits 0 |

Findings:
- **The hypothesis holds.** One 380-DIP window holds the whole schedule legibly at 125 % scaling,
  and the rule behind it is small enough to state in the model: the body follows `Enabled`, the
  time fields follow `Enabled && Mode == SetHours`. UIA confirms the window reports exactly that.
- **The coupling is visible, not just asserted:** the sunset screenshot is 475x425 against the
  default 475x378, because the "kept but not used" note appears when the fields go dead.
- **Commit on change, not on focus.** The fields originally committed on `LostFocus`, which made
  them undrivable by automation (and made the whole scenario depend on the desktop granting the
  window the foreground). They now commit on `TextChanged` **without re-rendering** and rewrite
  from the model on leave - the field stays usable while typing, `999` still reverts to `55`, and
  driving by pattern works whoever holds the foreground. About ten lines for real robustness.
- **A settings window needs a stated position.** With `WindowStartupLocation` left at its default
  the gear's window landed at the OS's cascade position rather than over the surface; it is now
  `CenterOwner` when opened from a gear and `CenterScreen` standalone. Found only because a
  screenshot of the surface rect showed no change while the foreground was the new window.
- **UIA turns a visual claim into an assertion.** 30 checks: "every control in the body is
  disabled (still live: 0 of 10)" is a claim a screenshot can only illustrate.

What this deliberately does not settle - the boundary of the experiment:
- Nothing is saved and nothing is applied: no registry, no file, no brightness call, and no
  monitor is detected. The window is a view over an in-memory model.
- The sunset range is the literal `12:00 AM - 12:00 AM` placeholder; there is no location lookup,
  no network and no timezone arithmetic.
- The schedule is app-level, not per-monitor; the window has no OK/Cancel and no app icon.
- **EXP03's "the gear is inert" is superseded** - it now opens this window. EXP03's record stands
  as the record of EXP03; the behaviour change is this experiment's.
- Unchanged repo facts, still untouched: `README.md` states **.NET 8** while everything here builds
  on net48, and `src/probe/Probe.cs:107` still lacks its method name.

Evidence: `%TEMP%\monitron-exp04\` holds `01-default`, `02-sunset`, `03-strength-55`, `04-off`,
`05-final` and `06-from-gear`. The reproducible form is the two commands above.

## Conclusion (findings)
**`success`** (2026-10-15, user-declared). The user asked for a dummy settings window offering
"Schedule night light" with a strength, a gradual fade and the two timing options; this is it,
rendering exactly that sketch and reporting exactly the state it claims.

The hypothesis holds. One 380-DIP code-only WPF window, built by the in-box `csc` (**0 MB**, no
XAML, no SDK, as in EXP03), carries the whole schedule legibly at 125 % scaling, and the rule
behind it is small enough to write down: the body follows the feature's own switch, and the two
time rows follow `Enabled && Mode == SetHours`. That rule is asserted twice - 38 assertions driving
the headless model, and 30 driving the real window through UI Automation - plus 6 that the gear
opens it, by keyboard and by automation. Nothing is described that was not driven.

What this settles:
- **A settings surface costs nothing extra to build or to test.** Same in-box toolchain, same
  code-only style; one extra `.cs` file for the model and one for the view. The seam EXP03 left
  (an inert gear) is now closed: the gear opens this window, and the window is the place a later
  experiment puts real settings behind.
- **Driving by pattern beats driving by keystroke.** UI Automation patterns need no OS foreground;
  keystrokes do, and a stray click on the desktop turned four checks into lies before this was
  fixed. A UI claim now either has an assertion behind it or it is not made.
- **Two defects were found only by looking and driving:** committing text on `LostFocus` made the
  fields undrivable (they now commit on change, without re-rendering, and rewrite from the model on
  leave), and an unset `WindowStartupLocation` put the window at the OS's cascade position instead
  of over the surface (`CenterOwner` now). Neither was visible in the code.

What it deliberately does not settle - the boundary of this experiment:
- Nothing is saved, nothing is applied: no registry, no file, no brightness call, no monitor
  detected. The window is a view over an in-memory model that dies with the process.
- The sunset range is the literal `12:00 AM - 12:00 AM` placeholder - no location, no network, no
  timezone arithmetic. The schedule is app-level, not per-monitor, and there is no OK/Cancel.
- **EXP03's "the gear is inert" is superseded**; EXP03's record stands as the record of EXP03.
- Unchanged repo facts, still untouched: `README.md` states **.NET 8** while everything here builds
  on net48, and `src/probe/Probe.cs:107` still lacks its method name.

Next, in the order the seams suggest: the **scheduler FSM** (injected clock, fake
`IBrightnessController`) - EXP02's recommendation, displaced twice now - with this window supplying
the shape of the settings it will read; then feeding real detection into the EXP03 rows, then the
tray shell, then persistence behind a window like this one.

Evidence: `%TEMP%\monitron-exp04\` - `01-default`, `02-sunset`, `03-strength-55`, `04-off`,
`05-final`, `06-from-gear`. Reproducible form: `model-test.ps1`, `verify-settings.ps1`,
`verify-settings.ps1 -AppArgs '--rows 1' -FromGear`, `verify.ps1`.

## Learnings
- `src\Monitron.App\model-test.ps1` - builds and runs the headless `ScheduleSettings` assertions
  with the in-box `csc` (`/target:exe`; a winexe cannot print). Use it whenever machine logic must
  be driven without a UI, which is every experiment here.
- `src\Monitron.App\verify-settings.ps1 [-AppArgs '--rows 1'] [-FromGear]` - drives the settings
  window through UI Automation, asserts what it exposes, writes shots to `%TEMP%\monitron-exp04`
  and exits non-zero on a failed check. Use it instead of describing a UI.
- Drive WPF controls through their UIA patterns (`TogglePattern`, `SelectionItemPattern`,
  `InvokePattern`, `ValuePattern`), never `SendKeys`: patterns need no OS foreground, and keystrokes
  only land when the desktop happens to grant it - a click elsewhere turns them into bogus failures.
  If `SetForegroundWindow` keeps failing, tap Alt once (`SendKeys '%'`) and retry: it releases
  Windows' foreground lock.
- UIA's `IsEnabled` is the **effective** value, so children of a disabled container report false -
  that is what makes a mode coupling assertable. Find anything with a readable label by
  `NameProperty`.
- An **owned** WPF window is not among UIA's desktop children; find it with `EnumWindows` by pid +
  title and `AutomationElement.FromHandle`.
- A WPF `Slider` contributes **Button** elements to the UIA tree (its track repeat-buttons): never
  find a button by count. `AutomationProperties.SetName` the icon-only ones and find by name - which
  is the accessibility fix too (`WindowStartupLocation` also needs stating, or a window cascades).
- Commit text fields on `TextChanged` **without** re-rendering, and rewrite from the model on
  `LostFocus`: the field stays usable while typing, invalid input reverts on leave, and automation
  can drive it without focus. Re-rendering on every keystroke makes a field impossible to clear.
- `ShotGood`'s content count detects a blank frame and nothing more - it cannot tell which window is
  on top (a white window over white text barely moves it). Prove "in front" with `GetForegroundWindow`.
- A PowerShell session keeps `Add-Type` types for its lifetime: two verifier scripts must not both
  define a class called `Win`. Name them per experiment (`WinExp04`).

