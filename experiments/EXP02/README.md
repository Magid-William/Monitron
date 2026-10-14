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
_Pending (user-declared)._

## Learnings
- `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe /platform:x64 /out:<exe> <cs>`
  — compile a .NET Framework 4.8 console app with **no SDK, no Visual Studio, no Windows SDK**
  (0 MB). Use when a metered / SDK-less machine must build C#.
- `src\probe\build.ps1` then `src\probe\probe.exe [--write [--force] --delta N]` — the EXP02
  DDC/topology probe (read-only by default; `--force` writes through a NULL handle).
- `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS = 2)` — authoritative **active** display + identity
  (target id, `outputTechnology`). Prefer it over `EnumDisplayMonitors`, which collapses to
  rendering targets and hides the internal panel.
- dxva2 on Intel UHD 620: `GetPhysicalMonitorsFromHMONITOR` yields a **NULL handle** but
  `Get/SetMonitorBrightness` still work. Pass the handle anyway; never key identity off it.
- External = DDC/CI via dxva2 (VCP `0x10` brightness, `0x12` contrast); internal = WMI
  `root\wmi WmiMonitorBrightness(Methods).WmiSetBrightness` (0–100).
- Ground truth for a monitor's DDC capabilities without calling the caps-string API:
  `%LOCALAPPDATA%\Microsoft\PowerToys\PowerDisplay\settings.json` (`currentBrightness`,
  `capabilitiesRaw`).
- Machine inventory: .NET 8 SDK **absent**; in-box .NET Framework 4.8 (`csc`/`MSBuild` + WPF
  assemblies) and **Rust 1.99** (gnu + gnullvm toolchains, **no MSVC linker**) are present.

