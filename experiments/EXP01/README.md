# EXP01 - Prior art: monitor brightness control & scheduled dimming

## Summary
Survey the Windows brightness/scheduling landscape (Monitorian, Twinkle Tray, Display
Dimmer, ClickMonitorDDC, f.lux, Windows Night Light, ...) to fix Monitorn's product
thesis and decide what it must add. Research only — no app code.

## Hypothesis
**No mainstream tool combines DDC/CI hardware brightness control with an arbitrary,
user-defined overnight dimming schedule** (e.g. 21:00 → 06:00). The claim was that
existing options have one or the other: f.lux and Windows Night Light schedule *color
temperature* but leave the monitor backlight alone; Monitorian controls DDC/CI
brightness capably but ships scheduling only as a paid add-on / command-line +
Task Scheduler workaround.

Falsified if some widely used tool already does both out of the box.

## Methodology & blast radius
- Method:
  1. Enumerate candidates in three buckets: (a) OSS Windows tools, (b) OS / commercial,
     (c) the API surface they build on.
  2. For each: brightness mechanism, scheduling ability, license/cost, and the gap it leaves.
  3. Write findings + a product thesis to `docs/prior-art/`.
- May touch: `git init`; branch `EXP01`; `experiments/EXP01/README.md`;
  `experiments-overview.md`; `docs/prior-art/*.md`; retargeted `AGENTS.md`.
- Off-limits: anything under `src/` (that is the build experiment); anything outside
  `D:\Playground\Projects\Apps\Monitorn\`.

## Open questions (known unknowns)
- [x] Deliverable shape — default: `docs/prior-art/README.md` (index + thesis) plus one
      note per tool cluster. **Adopted.**
- [x] Depth/breadth — default: Windows-only and DDC/CI-centric; other OSes as context only.
      **Adopted.**
- [x] Closed-source tools we cannot read — default: include at behavior level, mark internals
      unverified. **Adopted** (Display Dimmer, MonitorPilot marked as such).
- [x] API feasibility probe on *this* machine — default: excluded from EXP01; the dxva2
      hardware probe becomes EXP02's first step. **Adopted.**
- [x] Language of findings — default: English. **Adopted.**
- [x] `AGENTS.md` retarget timing — default: at record time (build mode). **Adopted** (done on `main`).
- [x] Success criterion — default: the doc names ≥5 tools, classifies each by scheduling ×
      DDC/CI, and states whether the hypothesis holds. **Met.**
- [x] Next experiment — default: EXP02 = DDC/CI hardware probe + `Monitorn.Core` skeleton.
      **Recommended (see Conclusion).**

## Conclusion (findings)

**The hypothesis is FALSIFIED.** A mainstream, free, MIT-licensed tool already does exactly
this: **Twinkle Tray** controls DDC/CI brightness *by time of day, per display* ("Supports
brightness adjustments by time of day or when idle", per-display scheduling included free),
and adds idle- and sun-position triggers. **Display Dimmer** (closed source) offers time-based
schedules too, and **MonitorPilot** markets rules that "automatically lower brightness and
shift external monitors to warmer" at night.

So the premise "no mainstream tool offers DDC/CI hardware brightness on a user schedule" does
not hold. Monitorn is **not** filling an empty capability gap.

Additional findings:
- **The mechanism is validated, not novel.** Every serious tool (Monitorian, Twinkle Tray,
  Display Dimmer) uses DDC/CI via `dxva2.dll` for external monitors. Monitorn's planned
  approach is the proven one. See `docs/prior-art/apis-and-identity.md`.
- **The dividing line is backlight vs. pixels.** f.lux and Windows Night Light change color
  temperature / gamma (the *image*), not the monitor's backlight. Only DDC/CI tools touch the
  actual backlight. This distinction is the core of Monitorn's implementation choice.
- **Monitorian's scheduling is the closest analogue but is gated.** Its Time Commands are part
  of paid Microsoft Store add-ons and are *excluded from the public repo*; free users fall back
  to the CLI + Task Scheduler.

**Revised thesis.** Monitorn's reason to exist is not a capability gap but *fit and form*:
a **native, dependency-light WPF/.NET 8** tray app (Twinkle Tray is Electron/Node ~200 MB;
Display Dimmer is closed source), with an **overnight fade window tuned to the user's exact
need** (21:00 → 06:00, fade down and restore rather than instant jumps), **EDID-robust
per-monitor profiles** that survive reconnect, and a small enough surface to be fully
understood and maintained. It is also an explicit personal/learning build.

**Consequence for the plan.** Because prior art de-risks the *mechanism* but removes the
*novelty*, the next experiment should be small and evidence-driven: prove DDC/CI works on this
machine and stand up the scheduler. Recommended next: **EXP02 = DDC/CI hardware probe +
`Monitorn.Core` skeleton** (Native interop, `MonitorService`, clock-injected `Scheduler`).

## Learnings
- `git init -b main` then `git checkout -b EXP01` — use for every experiment; the repo must
  exist before the branch (this folder started empty).
- Twinkle Tray, not Monitorian, is the real competitor: it is free and includes per-display
  time-of-day scheduling. Compare Monitorn against Twinkle Tray, not just Monitorian.
- Brightness automation splits into two families — **backlight (DDC/CI, WMI)** vs
  **pixel/gamma (f.lux, Night Light)**. Always state which one a feature belongs to.
- Monitorian's paid add-on code is not in its public repo; do not plan to copy its scheduling.
