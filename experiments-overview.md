# Experiments overview

One experiment = one hypothesis. Status is `todo | inProg | success | failed | abandoned`;
only the user declares the final status.

| EXP | Title | Status | Branch | Started | Concluded | Record |
|---|---|---|---|---|---|---|
| EXP01 | Prior art: monitor brightness control & scheduled dimming | failed | EXP01 | 2026-10-14 | 2026-10-14 | [experiments/EXP01/README.md](experiments/EXP01/README.md) |
| EXP02 | Detect monitors and control their brightness (sampling spike) | success | EXP02 | 2026-10-15 | 2026-10-15 | [experiments/EXP02/README.md](experiments/EXP02/README.md) |
| EXP03 | Per-monitor control surface (slider + gear), UI only | success | EXP03 | 2026-10-15 | 2026-10-15 | [experiments/EXP03/README.md](experiments/EXP03/README.md) |
| EXP04 | Dummy "Schedule night light" settings window, UI only | success | EXP04 | 2026-10-15 | 2026-10-15 | [experiments/EXP04/README.md](experiments/EXP04/README.md) |
| EXP05 | Bring brightness to life: real detection + real writes on the surface | inProg | EXP05 | 2026-10-15 | - | [experiments/EXP05/README.md](experiments/EXP05/README.md) |
| EXP06 | Apply the night-light schedule (scheduler FSM + real monitors) | success | EXP06 | 2026-10-15 | 2026-10-15 | [experiments/EXP06/README.md](experiments/EXP06/README.md) |

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

**EXP05:** `inProg`. Joins EXP02's actuator to EXP03's surface: the app detects the **active
rendering targets**, reads each one's real brightness into its row, and a slider drives the real
monitor over DDC/CI (or WMI for an internal active panel), **debounced** so the final value wins.
Rows are active targets only and the write model is debounce, both the user's call; the two repo
facts EXP04 flagged (`README.md`'s `.NET 8` claim and `src/probe/Probe.cs:107`) are fixed here as
the user asked. **Run 2026-10-15:** the one active target (AOC `Q27G3G3R3`) is detected and shown
at its real 30; a slider drove it **30 -> 20 -> 30** with the independent read-only `probe.exe`
following each step; 19 headless debounce assertions and 11 UI Automation checks, all passing. The
internal-WMI path is implemented but not exercised (no internal *active* target on this machine).

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
