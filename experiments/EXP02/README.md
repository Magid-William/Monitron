# EXP02 - Detect monitors and control their brightness (sampling spike)

## Summary
Prove on this machine that Monitron can enumerate connected monitors and read/write each
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
  `the repository root`.
- Escalation budget: exactly one reversible write per monitor; abort without writing if the
  initial read is invalid or the monitor is a known-bad model.

## Open questions (resolved during the run)
- [x] Native `dxva2`/WMI vs shelling to the Power Display CLI — **native**. This PowerToys build
      (0.100.2) ships **no** Power Display CLI, so the CLI was never an option.
- [x] Read VCP directly or fetch the capabilities string — **read VCP `0x10` directly**; the
      capabilities-string API was never called (avoids the `win32kfull` BSOD path).
- [x] Which monitors in scope — **both**: external over DDC/CI, internal over WMI.
- [x] Probe code location — `src/probe/` at the repo root.
- [x] Misbehaving monitor — a **NULL physical-monitor handle** was observed; flagged and handled.
- [x] SDK prerequisite — **none**. The plan assumed the .NET 8 SDK; instead we used the in-box
      .NET Framework 4.8 `csc.exe` (**0 MB download** — metered-internet assumption). Environment:
      Windows 11, Intel UHD 620 (driver 24.20.100.6223) + NVIDIA MX150.

## Results (spike run, 2026-10-15)
- **Topology:** `QueryDisplayConfig` → **1 active path**, targetId `200195`, outputTech `0x0A`
  (DisplayPort external) = the AOC `Q27G3G3R3` (`DISPLAY\AOC2703`). Internal `BOE0764` is
  connected (eDP) but is not an active rendering target.
- **Detect:** `EnumDisplayDevices` lists both monitors on the Intel adapter; WMI lists both
  panels; the authoritative active-target identity comes from `QueryDisplayConfig`.
- **Control — external (DDC/CI, dxva2):** brightness read 47 (min 0, max 100), contrast 26;
  reversible write **47 → 27 → 47**, read-back verified. Matches Power Display's own recorded
  value (47) for the same monitor.
- **Control — internal (WMI):** `WmiSetBrightness` **62 → 42 → 62**, read-back verified.
- **Quirk:** `GetPhysicalMonitorsFromHMONITOR` returns `hPhysicalMonitor = NULL` on this Intel
  setup, yet the DDC functions still resolve to the sole active monitor and work. Do **not**
  treat a NULL handle as failure here, and do **not** use it for identity — key identity off the
  `QueryDisplayConfig` target id / PnP device path.

## Conclusion (findings)
**Success (user-declared, 2026-10-15).** The hypothesis holds: Monitron enumerates every
connected display and reads/writes its brightness, reversibly and with read-back verification —
external over DDC/CI (`dxva2.dll`, VCP `0x10`), internal over WMI. On this machine the external
AOC `Q27G3G3R3` read 47 and was driven 47 → 27 → 47 (later set persistently to 50, then 30);
the internal `BOE0764` panel round-tripped 62 → 42 → 62.

What this settles:
- **Mechanism proven on real hardware** for both monitor kinds — the actuator seam
  (`IBrightnessController`) has a working implementation.
- **No heavyweight dependency:** in-box .NET Framework 4.8 + Windows DLLs/WMI only — no SDK,
  Visual Studio, Windows SDK, PowerToys, Monitorian, or other third-party tool.
- **Identity must come from `QueryDisplayConfig` / PnP device path, not the physical handle**
  (NULL on this Intel setup).
- **The untrusted-read guard matters:** a NULL handle can still return a plausible value, so
  writes stay gated on a corroborated read.
- **Off-limits VCPs stay untouched:** `0x60` (input) and `0xD6` (power) were never written, and
  the capabilities-string API was never called.

Next: the scheduler (FSM, injected clock, fake controller), then the product-stack decision
(leaning .NET Framework 4.8 WPF for a 0 MB build, given the metered-internet constraint).

## Learnings
- Build C# with the in-box `csc` (no SDK/VS/Windows SDK, 0 MB):
  `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /platform:x64 /out:<exe> <cs>`.
- Probe: `src\probe\build.ps1` then `src\probe\probe.exe [--write [--force]] [--set N | --delta N]`.
- Active monitor + identity: `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS = 2)` (target id,
  `outputTechnology`); `EnumDisplayMonitors` alone hides the non-rendering internal panel.
- Control: external = `dxva2` VCP `0x10` brightness / `0x12` contrast; internal = WMI
  `root\wmi WmiMonitorBrightnessMethods.WmiSetBrightness` (0–100).
- Intel UHD 620 returns a **NULL** physical-monitor handle yet dxva2 still works: key identity
  off the target id, never the handle; never treat NULL as failure.
- A monitor's real DDC capabilities (read-only) are in
  `%LOCALAPPDATA%\Microsoft\PowerToys\PowerDisplay\settings.json` (`capabilitiesRaw`) — avoids
  the capabilities-string BSOD path.

