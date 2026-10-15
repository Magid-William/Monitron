# EXP03 - Per-monitor control surface (slider + gear), UI only

## Summary
UI only: a WPF window listing one row per monitor — name, 0-100 slider, gear button —
from hardcoded sample data, built with the in-box toolchain. No DDC/CI, no WMI, no
scheduler, no writes.

## Hypothesis
A net48 WPF window built by the in-box `csc` (0 MB download) can present the whole
per-monitor control surface — name + slider + gear, one row per monitor — legibly from 1 to N
monitors, where dragging a slider changes on-screen state only.

**Falsified if:** the in-box toolchain cannot produce a runnable WPF window without a download;
or a row cannot show name + slider + gear legibly (overlap, clipping, unusable at 100 % DPI).

## Methodology & blast radius
- Method:
  1. `src/Monitron.App/` at the repo root: `Program.cs` — a WPF window written **code-only, no
     XAML** (so no BAML step, which keeps the build to the in-box `csc`) — plus `build.ps1`
     mirroring `src/probe/build.ps1`, referencing `PresentationFramework`, `PresentationCore` and
     `WindowsBase` from `...\Framework64\v4.0.30319\WPF\` and `System.Xaml` from the framework
     directory itself, and `verify.ps1` to drive and screenshot the window.
  2. One row per monitor: name (ellipsis on overflow) + `Slider` (0-100) + live value label +
     gear `Button` (Segoe MDL2 Assets glyph `E713`), stacked in a single resizable window.
  3. Static sample data only: three hardcoded rows — AOC `Q27G3G3R3`, BOE `BOE0764`, and one
     unnamed row to exercise the fallback label.
  4. Slider drag updates only its own value label. The gear renders, is enabled, and is inert.
  5. Verify by driving the app: build -> launch -> capture the window as a PNG and look at it ->
     one resize re-run -> confirm a clean exit. No unit harness: this experiment has no state
     machine to drive, its artifact is visual.
  6. DPI: embed `/win32manifest` declaring PerMonitorV2 awareness so the surface is crisp on a
     scaled display instead of bitmap-stretched.
- May touch: branch `EXP03` (off `EXP02`); `experiments/EXP03/README.md`; the overview row; the
  new `src/Monitron.App/` (**experiment code lands at the repo root** per AGENTS; only the README
  lives under `experiments/EXP03/`).
- Off-limits: any DDC/CI or WMI call, any brightness write; `src/probe/**`; tray icon, autostart,
  scheduling, profiles; anything outside `D:\Playground\Projects\Apps\Monitorn\`; downloads
  (the net48 in-box path was chosen, so no SDK is fetched).
- Numbering: EXP03 was the number EXP02 reserved for the scheduler; this experiment takes EXP03
  and the scheduler slides one number later.
- Escalation budget: none — the experiment cannot touch hardware by construction.

## Open questions (resolved before the run)
- [x] Stack — **net48 in-box WPF, code-only, 0 MB**. No SDK is installed
      (`dotnet --list-sdks` reports none) although the .NET 8 runtime incl. WindowsDesktop 8.0.30
      is present; README's `.NET 8` claim is therefore not buildable as written today. Downloading
      the SDK (~250 MB) would break EXP02's 0 MB assumption, so it is left to its own experiment.
- [x] Surface — **a plain resizable window** now; the tray icon/flyout shell gets its own
      experiment.
- [x] Gear — **inert**, enabled. The surface is what is being proven, not the settings behind it.
- [x] Data — **hardcoded samples**; showing the *real* detected monitors is functionality and
      stays out of scope.
- [x] Tests — **none**; the artifact is visual, verified by screenshot (the AGENTS
      "drive the machine" rule applies to the scheduler experiment).
- [x] Branch — `EXP03` off `EXP02` (`main` is an ancestor of `EXP02`).

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11, 125 % display scaling), each run closing cleanly
with exit code 0:

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitron.App\build.ps1` | `Monitron.App.exe` from the in-box `csc`, **0 MB downloaded**, no XAML/BAML step |
| Default | `verify.ps1` | 3 rows: `AOC Q27G3G3R3` 47, `BOE BOE0764` 62, `Display 3` 50 |
| One monitor | `verify.ps1 -AppArgs '--rows 1 --width 300'` | 300 DIP window, row still legible; name fits, slider usable, gear visible |
| Many monitors | `verify.ps1 -AppArgs '--rows 5 --width 520'` | 5 rows stack cleanly, no clipping or overlap |
| Slider driven | `verify.ps1 -Keys '{TAB}{RIGHT 10}'` | row 1 `47 -> 57`, its thumb moved, **other rows unchanged** (62, 50) |
| Gear driven | `verify.ps1 -Keys '{TAB}{TAB}{ENTER}'` | gear takes focus and is pressed; no panel, no second window, values unchanged - inert as specified |
| DPI | window rect of the 400-DIP default | **500 px** captured at 125 % scaling: the PerMonitorV2 manifest is honoured (no bitmap stretch) |

Findings:
- **The hypothesis holds.** A net48 WPF window built by the in-box toolchain renders the whole
  per-monitor surface — name + slider + gear — legibly from 1 to 5 monitors, and dragging a
  slider changes nothing but its own value label. The screenshot diff of the gear run shows the
  gear's focus ring as the **only** content change.
- **The stack question EXP02 left open is answered for the lean path:** code-only WPF (no XAML)
  compiles with `csc` + the in-box WPF assemblies, so a real control surface costs **0 MB** -
  but it needs a `<reference>` to `System.Xaml.dll` that the WPF folder does not hold.
- **DPI is not optional:** code-only WPF has no DPI awareness by default; the
  `/win32manifest` PerMonitorV2 declaration is what keeps the surface crisp on a scaled display.
- **Row layout that reads well:** name and live value on the caption line, the slider beneath
  spanning to the gear, the gear vertically centred in the row. Two lines per monitor keep long
  names and a short slider legible in a 300-DIP window.
- Follow-ups this spike surfaced, all deliberately out of scope: no app icon yet (the title bar
  shows the generic one); sliders report integers only, with no "at maximum" affordance; the
  gear needs something behind it; and nothing here detects monitors or touches brightness.

Evidence: PNGs of every pass above, plus a before/after pixel diff of the gear run, were captured
with `src/Monitron.App/verify.ps1` (which is the reproducible form of the table's commands).

## Conclusion (findings)
**Recommendation: `success`** (2026-10-15). The user asked for the experiment to be concluded; the
final status is theirs to declare, and the evidence below is what it rests on.

The hypothesis holds. A net48 WPF window built by the in-box `csc` (0 MB downloaded, no XAML, no
BAML step) presents the whole per-monitor control surface — one row per monitor with its name, a
0-100 slider and a gear button — and stays legible from one monitor in a 300-DIP window to five
rows in a 520-DIP one. Driving it confirms the behaviour is as bounded as claimed: a slider moves
its own value (47 -> 57) and nothing else, and a focused gear presses without opening anything.

What this settles:
- **The stack question EXP02 left open now has a working UI answer:** code-only WPF on the in-box
  .NET Framework 4.8 — no SDK, no Visual Studio, no XAML tooling, no download. Hand a later
  experiment the .NET 8 SDK and this surface can be retargeted, but nothing needs it today.
- **The surface and the actuator share a unit.** Rows are the unit of both the UI and the
  `IBrightnessController` seam, so feeding real detection into these rows is a substitution of
  data, not a redesign.
- **DPI awareness is a declaration, not a default:** without the PerMonitorV2 manifest the surface
  is bitmap-stretched at 125 %; with it, a 400-DIP window is 500 crisp pixels.

What it deliberately does not settle — the boundary of this experiment:
- No monitor is detected and no brightness is read or written. `Program.cs` contains no P/Invoke,
  no `dxva2`, no WMI and no registry access at all: the off-limits list held by construction.
- The surface is a plain window; the tray icon, flyout and autostart are still unbuilt, and the
  gear has nothing behind it.
- Sliders show integers with no "at maximum" affordance, and the window has no app icon.

Two repo-level facts surfaced during the run, both left alone as out of scope:
- `README.md` still states the stack is **.NET 8**, while this experiment had to build on net48
  (no SDK on the machine). Claim and code now disagree; correcting it — or installing the SDK and
  retargeting — is the user's call, not something this experiment may decide.
- `src/probe/Probe.cs:107` declares `SetMonitorBrightness` with no method name, so EXP02's
  committed probe source no longer compiles. EXP02's record and its verified behaviour stand, and
  its build output is gitignored, so nothing else is affected; the source is a one-word fix.

Next, in the order the seams suggest: the scheduler FSM (injected clock + fake
`IBrightnessController` — the experiment EXP02 recommended and this one displaced), then feeding
real detection into these rows, then the tray shell, then something behind the gear.

## Learnings
- Build the UI with no toolchain install: `src\Monitron.App\build.ps1` — in-box
  `...\Framework64\v4.0.30319\csc.exe`, `/target:winexe`, `/win32manifest:app.manifest`, and
  `/reference` to `WPF\{PresentationFramework,PresentationCore,WindowsBase}.dll` **plus
  `System.Xaml.dll` from the framework directory itself** (not the `WPF\` folder — it is the one
  reference the WPF folder does not hold). 0 MB, no BAML step.
- Verify a UI by looking at it, not by describing it: `src\Monitron.App\verify.ps1
  [-AppArgs '--rows N --width PX'] [-Keys '{TAB}{RIGHT 10}']` — waits for the window, confirms it
  is really foreground, refuses a blank frame and re-shoots, writes the PNG, then closes the
  window and checks for exit code 0.
- WPF's first frame lags the window's arrival: a shot taken ~1 s after the window appears can be
  blank or half-painted, so settle ~1.6 s and require actual pixel content before trusting a file.
- `SendKeys` has **no `{SPACE}` keyword** (use a literal space, or `{ENTER}`, which also presses a
  focused WPF button), and never name a script parameter `$Args`: it is a PowerShell automatic
  variable and the parameter is silently dropped.
- Grade the environment on the SDK, not on `dotnet --info`: `dotnet --list-sdks` is empty here
  while `dotnet --list-runtimes` shows `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App`
  8.0.30 — a *runtime* cannot build.
