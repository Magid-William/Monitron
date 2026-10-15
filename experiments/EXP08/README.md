# EXP08 - Dark mode: the app's own windows follow the Windows app theme

## Summary
The app's two windows - the per-monitor surface and the settings window - are hardcoded light while
this machine's Windows is in dark mode. Make both dark from one palette, find out what a pure
brush swap cannot reach, follow the OS theme live, and darken the title bar. Asserted by pixels.

## Hypothesis
A single palette object that both windows read renders them dark, and the **only** elements that
resist a pure brush swap are the ones whose look comes from the OS theme's control template -
`Slider` track/thumb, `CheckBox`/`RadioButton` glyphs, `Button` chrome, `TextBox` border - which
must be given templates of their own.

**Falsified if:** every element recolors from properties alone (then the premise is wrong); an
element that should have recolored stays light, or one that should resist does not; the rendered
pixels disagree with the palette the app reports; the OS theme cannot be read and followed; or the
DWM dark title bar is unavailable on this build.

## Methodology & blast radius
- Method:
  1. `src/Monitorn.App/Theme.cs` - a pure model, no WPF: `ThemePalette` (the colors as plain
     `0xAARRGGBB`, with `Light` *exactly* the palette the app has drawn so far) and `ThemeChoice`
     (the rule `(what Windows says, what the user asked for) -> Light|Dark`). Headless
     `theme-test.ps1`.
  2. `src/Monitorn.App/Theming.cs` - `ThemeBrushes`: the palette as **mutable, shared**
     `SolidColorBrush` instances, so a theme switch repaints windows that are already built (the
     surface holds the scheduler's wiring) instead of rebuilding them. Plus one control template
     each for `Slider` (a `Track` named `PART_Track`), `CheckBox`, `RadioButton`, `Button` and
     `TextBox` (`PART_ContentHost`), which is what the OS-styled parts need.
  3. `src/Monitorn.App/SystemTheme.cs` - read the OS app theme
     (`HKCU\...\Themes\Personalize\AppsUseLightTheme`), follow it live (`WM_SETTINGCHANGE` with
     `ImmersiveColorSet` through an `HwndSource` hook), and darken the **non-client title bar**
     with `DwmSetWindowAttribute(20)`, asserted by reading the attribute back. `ThemeState` is the
     one object that resolves all of it.
  4. `Program.cs` / `SettingsWindow.cs` - the two duplicated light palettes collapse into the one
     object; verification seams: `--theme light|dark|auto` (pin the theme), `--os-theme light|dark`
     (stand in for the OS reading, the clock's analogue), `--theme-no-templates` (the measurement
     below), `--theme-report <dir>` (render and sample both windows), `--theme-toggle-after
     <sec> <light|dark>` (drive a live re-resolution).
  5. **Verification seam, the assertion:** `ThemeReport.cs` shows both windows, renders each to a
     `RenderTargetBitmap`, samples named regions (window background, a row card, the slider band,
     the check/radio box, a text field's face, a button's face) and writes the sampled colors plus
     the resolved palette and the DWM title-bar read-back to `<dir>\<window>.txt`, atomically, at
     launch and on every theme change. The PNG is evidence; the numbers are the assertion.
     Measured at the same time: with `--theme-no-templates` the same report shows which controls a
     pure brush swap leaves light.
  6. Tests: `ThemeTests.cs` + `theme-test.ps1` (headless: the palettes, the resolution rule, the
     parsing, the hex formatting). `verify-theme.ps1` (UI Automation + the report: each theme in
     turn, the raw-vs-templated measurement, `auto` following `--os-theme`, a live switch, and the
     title-bar read-back).
  7. Regressions: `model-test` / `writer-test` / `schedule-test` / `persistence-test` and
     `verify-settings` / `verify-control` / `verify-schedule` / `verify-persistence`.
- May touch: branch `EXP08` off `EXP07`; `experiments/EXP08/README.md`; the overview row;
  `src/Monitorn.App/` (`Theme.cs`, `Theming.cs`, `SystemTheme.cs`, `ThemeReport.cs`,
  `ThemeTests.cs`, `theme-test.ps1`, `verify-theme.ps1`, `Program.cs`, `SettingsWindow.cs`,
  `build.ps1`, and `verify-schedule.ps1`'s launch - it now uses a temp settings file, the same
  correction EXP07 made to `verify-settings.ps1`, because a persisted `gradualFade=false` in the
  user's real file makes its fade phase impossible to observe).
- Off-limits: any monitor write or schedule/timing change (a theme must not touch brightness -
  report mode runs no scheduler and no writer); DDC/CI and VCP; per-monitor profiles and identity;
  the tray shell and autostart; real sunset/sunrise; the `settings.ini` format (the theme follows
  the OS; a stored user preference is a later experiment); the registry is **read only**; anything
  outside `D:\Playground\Projects\Apps\Monitorn\`; downloads.
- Escalation budget: every run pins `--theme`/`--os-theme` and writes into
  `%TEMP%\monitorn-exp08\`, with `--settings-file` pointing at a temp file; the verifier asserts
  the real `%APPDATA%\Monitorn\settings.ini` is unchanged (its content hash, whether or not it
  exists) and that no monitor value changed.
  The user's real Windows theme is never changed: the OS reading is injected through `--os-theme`,
  the same way the clock is injected.

## Open questions (proposed defaults adopted unless the user says otherwise)
- [x] **What "dark mode" means** - the app's own UI theme (both windows), following the Windows
      app theme. *Assumption: a monitor-side pixel/gamma "dark mode" is a different product feature
      (`docs/prior-art/tools.md`), and the app already dims the backlight.*
- [x] **Spike or full** - full: the pixel assertion measures the templating anyway. *Assumption.*
- [x] **Follow the OS or a manual switch** - follow the OS; `--theme` is the verification seam and
      is not persisted. *Assumption: a stored preference adds a key to `settings.ini`; that keeps
      the format out of this experiment.*
- [x] **Title bar** - darken it via DWM attribute 20, a no-op if the call fails. *Assumption.*
- [x] **Which controls to hand-template** - only what the measurement shows is still light.
      *Assumption.*
- [x] **High contrast** - honored in the light-path sense: the theme only sets colors, it does not
      disable the OS's high-contrast drawing; not exhaustively tested. *Assumption.*
- [x] **AGENTS update** - none. UI Automation cannot see color, so the rendered-pixel read-back is
      this experiment's own technique; a promotion is a separate ask. *Assumption.*

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11 25H2, build 26200; the AOC `Q27G3G3R3` over DDC/CI).
Windows was already set to its dark app theme (`AppsUseLightTheme=0`), which is what the app had
been ignoring.

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitorn.App\build.ps1` | `Monitorn.App.exe` from the in-box `csc`, **0 MB** (`Theme.cs`, `Theming.cs`, `SystemTheme.cs`, `ThemeReport.cs` added) |
| Machine | `theme-test.ps1` | **34 assertions, 0 failed**: both palettes, ten roles with no color shared between them, the auto/pinned rule, the parsing, the hex formatting |
| End to end | `verify-theme.ps1` | **42 checks, 0 failed**: each theme rendered and sampled, the raw-vs-templated measurement, `auto` following the injected OS reading, a driven live switch, the DWM read-back, and no monitor or settings write |
| Regression | `model-test` / `writer-test` / `schedule-test` / `persistence-test` | **38 / 19 / 114 / 42**, 0 failed |
| Regression | `verify-settings.ps1` | **30 checks, 0 failed** (the dark theme drives the same as the light one) |
| Regression | `verify-control.ps1 -AppArgs '--no-schedule'` | **11 checks, 0 failed**: the re-templated slider still drives the real monitor |
| Regression | `verify-schedule.ps1` | **25 checks, 0 failed**: a scripted night still runs (60% from 70) and leaves the display at 70 |
| Regression | `verify-persistence.ps1` | **27 checks, 0 failed** |

The measurement, as the share of *light* pixels in each region (0 = black, 1 = white):

| region | light theme | dark theme | dark palette, no templates of ours |
|---|---|---|---|
| window background | 1.00 | 0.00 | 0.00 |
| monitor row card | 1.00 | 0.00 | 0.00 |
| slider, unfilled side | 0.99 | 0.01 | **0.43** |
| check box | 0.98 | 0.00 | **0.36** |
| radio mark | 1.00 | 0.00 | **0.29** |
| field face | 0.87 | 0.13 | 0.10 |
| button face | 0.81 | 0.19 | 0.21 |

Findings:
- **The hypothesis holds, with one refinement.** A single palette does render both windows dark,
  and the parts that resist a pure brush swap are the OS-templated ones - but the resisting set is
  smaller than predicted: it is the **Slider's whole track** and the **CheckBox/RadioButton marks**.
  A `TextBox`'s face and a `Button`'s face *do* follow a property assignment (the OS template binds
  `Background`/`BorderBrush`/`Foreground`), so the fractions above already drop for them with no
  template at all. What the `Button` keeps for itself is its own **border and hover/pressed states**
  - a probe button given `Background=Red` renders `#FD7300` with a `#707070` border, which is the
  "Button chrome" the hypothesis named.
- **The claim is asserted on the real visual tree, not on a screenshot.** Each window is rendered to
  a `RenderTargetBitmap` and each region is located through that element's own
  `TransformToAncestor`, so the numbers are the same on every run and do not depend on what is in
  front of the window. The verifier also asserts its own baseline: the controls really are on the OS
  templates in the `--theme-no-templates` run (`ourTemplate=false`) and on ours in the dark one.
- **Following Windows live works, and was driven without touching the user's theme.** `auto` resolved
  light from an injected OS reading, and a scripted colour-set change re-resolved it to dark in
  place: the report file went `mode=light` -> `mode=dark`, and the *pixels* followed (background
  0.00 light, check box 0.00 light). The user's real Windows theme was never changed.
- **The dark title bar is real and verified.** `DwmSetWindowAttribute(20)` landed on this build and
  `DwmGetWindowAttribute` reads the attribute back as 1 in dark and 0 in light.
- **The light palette is the historical one, but the light *look* is not byte-identical.** Every
  colour in `ThemePalette.Light` is EXP03's (asserted), yet the light theme now draws its check
  boxes, radios, slider and fields from our templates instead of the OS's, so their shapes and
  hover/focus states differ slightly from before. That is the honest cost of a code-only theme.
- **Two defects were found only by driving it, not by reading the code.** The first live switch
  *crashed* the app (unhandled `InvalidOperationException` in `ThemeBrushes.Equip`): a brush that
  appears as a `Setter` value inside a `ControlTemplate` is **frozen** when that template is sealed,
  so it cannot be mutated. And a report written in the same dispatcher turn as the change rendered
  the *pre-change* tree - the controls were simply missing from the frame. Both are fixed (private
  brush copies plus template rebuild; the report is written at `ContextIdle`), and both are in the
  Learnings below.
- **Nothing else moved.** The re-templated slider still drives the monitor through UI Automation
  (11/11), a whole scripted night still fades 70 -> 60 and hands 70 back (25/25), the settings file
  still round-trips (27/27), and every theme run left the display and the user's real
  `%APPDATA%\Monitorn\settings.ini` untouched (asserted by a content hash).

Boundaries this run did not exercise:
- **Only the two windows this app has.** There is no `ComboBox`, `ScrollBar`, `ListBox` or `Menu` in
  it, so none were templated; a control added later needs its own template and its own probe.
- **High contrast was not asserted.** The theme only sets colors and does not disable the OS's
  high-contrast drawing, but the two palettes were not chosen against a high-contrast theme.
- **`WM_SETTINGCHANGE` itself was not observed arriving from Windows.** The live *re-application* was
  driven through the injected reading (the clock's analogue); the OS broadcast that calls it is wired
  but not exercised, because changing the user's real theme is not something a verifier may do.
- **No stored preference.** The theme follows the OS; `--theme` is a verification seam, so nothing
  was added to `settings.ini` and there is still no switch in the UI.

Recommendation: the overview's standing next step is unchanged - the **tray shell + autostart**.
A stored `theme=` preference and a switch in the settings window are now small (one palette object
and one `Changed` event already exist), and are the natural follow-on if the OS-following rule is
not enough.

## Conclusion (findings)
_Pending: the run's findings are above; the user declares the status._

## Learnings
- `src\Monitorn.App\theme-test.ps1` (headless, 34) and `src\Monitorn.App\verify-theme.ps1`
  (end to end, 42) - run them whenever a color, the resolution rule, a control template or the
  report's probes change. The verifier needs `src\probe\probe.exe` for the "no monitor write" check.
- A brush used as a **`Setter` value inside a `ControlTemplate` is frozen when the template is
  sealed**; mutating its `Color` then throws `InvalidOperationException` at the next theme change.
  Use a private copy for anything a template references and rebuild the templates on a switch
  (`ThemeBrushes.Equip` drops them), and keep the mutable shared brushes only for values assigned
  directly to elements. That combination is what makes a live theme switch possible at all.
- A `Track` cannot be built with `FrameworkElementFactory`: its `Thumb`, `IncreaseRepeatButton` and
  `DecreaseRepeatButton` are plain CLR properties, not dependency properties. Keep the OS `Slider`
  template and re-template the parts it already has, found with
  `slider.Template.FindName("PART_Track", slider)` after `ApplyTemplate()`.
- Required template parts, or the control is blank/broken: `Slider` -> a `Track` named
  `PART_Track`; `TextBox` -> a `ScrollViewer` named `PART_ContentHost`. `Track`'s `Minimum`,
  `Maximum`, `Value` and `IsDirectionReversed` are *not* inherited, so bind them to the templated
  parent.
- `RenderTargetBitmap` on a shown `Window` works, and `element.TransformToAncestor(theSameVisual)`
  gives coordinates in that same space - render and measure against one visual and the two cannot
  disagree. Take the shot **after** the layout/render for a change:
  `Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, ...)`; in the same dispatcher turn as a
  template change it renders the old tree (controls missing).
- `--theme-no-templates` is the measurement seam: it applies the palette and the property-level
  control colors but installs no templates, which is how "what a brush swap alone reaches" is
  asserted rather than argued. `--os-theme` injects the OS reading so `auto` and a live switch can
  be driven without changing the user's real Windows theme.
- A verification run that inherits the user's persisted settings is not deterministic. Read a theme
  report with `--settings-file` pointing at a **fresh temp file**: with the user's real file, whose
  schedule was off, the settings body renders *disabled* and the OS paints its own disabled control
  colors - the text box read as white in a dark theme and looked like a bug. (The same class of
  mistake is why `verify-schedule.ps1` had to be given a temp settings file here.)
- The OS `TextBox`/`Button` templates *do* follow `Background`, `BorderBrush` and `Foreground`; a
  probe that set `Background=Red` rendered `#FF0000` with a lime border. The `Button` template
  overlays its own shine and border (`#FD7300` on red, `#707070` border), which is why the face
  recolors but the chrome does not.
- DWM's dark title bar is `DwmSetWindowAttribute(hwnd, 20, 1)` (attribute 19 before Windows 10
  20H1), needs only `new WindowInteropHelper(window).EnsureHandle()` - no showing - and is readable
  back with `DwmGetWindowAttribute(hwnd, 20, ...)` so it can be asserted.

