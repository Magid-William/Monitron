# Prior art — Monitron

Research produced by **EXP01** (2026-10-14). Scope: Windows brightness-control tools and
their scheduling; the API surface they build on; and how a monitor is identified across
reconnects.

## Verdict on the gap
EXP01's hypothesis — *"no mainstream tool combines DDC/CI hardware brightness control with a
user-defined overnight dimming schedule"* — is **falsified**. **Twinkle Tray** does exactly
that, for free, per display. See [`tools.md`](tools.md).

## Thesis (revised)
Monitron is not a first-mover. Its value is *fit and form*:

1. **Native and light** — C#/WPF on .NET 8, no Electron/Node runtime (Twinkle Tray is
   Electron, ~200 MB installed).
2. **A focused overnight window** — fade down at 21:00 and restore at 06:00, with a gradual
   fade and an explicit "manual override ends the schedule for the night" rule.
3. **Identity that survives reconnect** — EDID-first per-monitor profiles (see
   [`apis-and-identity.md`](apis-and-identity.md)).
4. **Small enough to own** — a personal build whose whole surface is understood.

## Index
- [`tools.md`](tools.md) — the tool-by-tool survey (mechanism × scheduling × cost).
- [`apis-and-identity.md`](apis-and-identity.md) — how brightness is controlled on Windows,
  and how to identify a monitor across disconnect/reconnect.

## Two families of "dimming" (used throughout)
- **Backlight** — the monitor's real brightness, via DDC/CI (`dxva2.dll`) or WMI (internal
  panels). This is what Monitron changes.
- **Pixel / gamma** — the image is darkened in the GPU/pipeline (`SetDeviceGammaRamp`,
  color temperature). f.lux and Windows Night Light are here; the backlight is untouched.
