# Experiments overview

One experiment = one hypothesis. Status is `todo | inProg | success | failed | abandoned`;
only the user declares the final status.

| EXP | Title | Status | Branch | Started | Concluded | Record |
|---|---|---|---|---|---|---|
| EXP01 | Prior art: monitor brightness control & scheduled dimming | failed | EXP01 | 2026-10-14 | 2026-10-14 | [experiments/EXP01/README.md](experiments/EXP01/README.md) |
| EXP02 | Detect monitors and control their brightness (sampling spike) | success | EXP02 | 2026-10-15 | 2026-10-15 | [experiments/EXP02/README.md](experiments/EXP02/README.md) |
| EXP03 | Per-monitor control surface (slider + gear), UI only | success | EXP03 | 2026-10-15 | 2026-10-15 | [experiments/EXP03/README.md](experiments/EXP03/README.md) |
| EXP04 | Dummy "Schedule night light" settings window, UI only | success | EXP04 | 2026-10-15 | 2026-10-15 | [experiments/EXP04/README.md](experiments/EXP04/README.md) |

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
tray shell, and persistence.
