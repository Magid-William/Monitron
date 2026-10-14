# Experiments overview

One experiment = one hypothesis. Status is `todo | inProg | success | failed | abandoned`;
only the user declares the final status.

| EXP | Title | Status | Branch | Started | Concluded | Record |
|---|---|---|---|---|---|---|
| EXP01 | Prior art: monitor brightness control & scheduled dimming | failed | EXP01 | 2026-10-14 | 2026-10-14 | [experiments/EXP01/README.md](experiments/EXP01/README.md) |
| EXP02 | Detect monitors and control their brightness (sampling spike) | success | EXP02 | 2026-10-15 | 2026-10-15 | [experiments/EXP02/README.md](experiments/EXP02/README.md) |

**EXP01:** status `failed` is the agent's recommendation, pending the user's declaration —
the hypothesis ("no mainstream tool combines DDC/CI brightness with a user schedule") was
falsified by prior art (Twinkle Tray already does it). Recommend **EXP02 = DDC/CI hardware
probe + `Monitorn.Core` skeleton**.

**EXP02:** `success`. Detection and reversible brightness control proven on this machine —
external over DDC/CI (`dxva2`, VCP `0x10`), internal over WMI — built and run with only the
in-box .NET Framework 4.8 toolchain (0 MB). Recommend **EXP03 = scheduler FSM** (injected clock
+ fake `IBrightnessController`), with the product-stack decision (lean .NET Framework 4.8 WPF)
as its own call.
