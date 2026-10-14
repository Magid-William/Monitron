# EXP02 - Detect monitors and control their brightness (sampling spike)

## Summary
Prove on this machine that Monitorn can enumerate connected monitors and read/write each
display's brightness — external panels over DDC/CI, internal over WMI — with reversible,
logged writes, before any scheduler is built.

## Hypothesis
A minimal probe using `dxva2.dll` (`GetPhysicalMonitorsFromHMONITOR` → `GetMonitorBrightness`
/ `SetMonitorBrightness`, VCP `0x10`) enumerates every DDC/CI-capable external monitor and each
write produces a visible, read-back-verifiable change; internal panels answer via WMI
`WmiMonitorBrightnessMethods.WmiSetBrightness`.

**Falsified if:** a monitor is enumerated but no brightness write is visible or verifiable, or
readback is inconsistent in a way we cannot quarantine — *not* if some monitors are simply
absent (a known hardware limitation).

## Methodology & blast radius
- Method (three passes, all logged):
  1. **Detect only, no writes.** Enumerate via `QueryDisplayConfig`/`EnumDisplayMonitors`,
     obtain physical handles, read VCP `0x10` (brightness) and `0x12` (contrast), and record
     identity per monitor: EDID id, device path, friendly name, transport/connector. Read
     internal panels via WMI.
  2. **Controlled write.** On one external monitor: read current brightness, set
     `current − 20` (clamped), confirm visually and by read-back, then **restore the original
     value**. VCP `0x10` only.
  3. **Internal panel.** Same read → write → restore cycle via WMI `WmiSetBrightness`, if a
     panel is present.
- May touch: branch `EXP02`; `experiments/EXP02/README.md`; the overview row; one throwaway
  probe **at the repo root** (per AGENTS, experiment code lands at the repo root,
  `experiments/EXP0N/` holds only the README).
- Off-limits: VCP `0x60` (input source) and `0xD6` (power) — the confirmed bricking vector;
  any manufacturer-range VCP; and the DDC/CI **capabilities-string call** (the `win32kfull`
  BSOD path) — we read VCP `0x10` directly instead. Nothing outside
  `D:\Playground\Projects\Apps\Monitorn\`.
- Escalation budget: exactly one reversible write per monitor; abort without writing if the
  initial read is invalid or the monitor is a known-bad model.

## Open questions (known unknowns)
- [ ] Native `dxva2`/WMI from the start, or shell to the Power Display CLI as a zero-risk first
      probe? — proposed default: native driver code from the start (adapted from
      PowerToys/Monitorian, MIT, attributed); the CLI only as a fallback for a monitor the
      native path cannot reach.
- [ ] Read VCP directly or fetch the capabilities string? — proposed default: read VCP `0x10`
      directly; never call the capabilities-string API.
- [ ] Which monitors are in scope? — proposed default: all of them — external over DDC/CI,
      internal over WMI.
- [ ] Where does the probe code live, given it is throwaway? — proposed default: one project at
      the repo root, deletable after the experiment.
- [ ] What if a monitor misbehaves (invalid range, garbage read, no ack)? — proposed default:
      report-only for that monitor, quarantine it, and never let an unvalidated read feed the
      write value.
- [ ] Prerequisite: the .NET 8 SDK must be installed to build a C# probe. — proposed default:
      assume present; if missing, stop and flag it rather than installing anything.

## Conclusion (findings)
_Pending._

## Learnings
- `git checkout -b EXP02` — branch per experiment; EXP numbers come from
  `git log --oneline --branches` + `experiments-overview.md` + `experiments/`.
