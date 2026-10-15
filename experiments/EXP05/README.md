# EXP05 - Bring the brightness functionality to life (real detection + real writes)

## Summary
Join EXP02's actuator to EXP03's surface: detect the active displays, show their real brightness,
and let a slider write it over DDC/CI (or WMI), debounced so the last value wins. Same in-box
net48 toolchain, 0 MB.

## Hypothesis
A net48 app built by the in-box `csc` can (1) enumerate the **active rendering targets** and read
each one's real brightness into its row, and (2) such that a slider drag causes a real,
independently read-back-verifiable change to *that* monitor only, with the value **debounced**
and the window staying responsive.

**Falsified if:** an active display yields no usable handle; or a slider move cannot be shown by an
external read-back to have reached the physical monitor; or the change leaks to another monitor;
or the debounce applies a stale value, or the window freezes.

## Methodology & blast radius
- Method:
  1. `src/Monitorn.App/Brightness.cs`: `MonitorInfo`, the `IBrightnessController` seam EXP02
     reserved, and the **debounce machine** `BrightnessWriter` - per monitor a `Target` set on each
     request and a settle deadline `now + delay`; an `Advance()` step writes once when the deadline
     passes, tracking `Applied` vs `Target`, and a failed write is recorded, not retried until the
     next slider move. No P/Invoke, no WMI, so it compiles into the headless test.
  2. `src/Monitorn.App/BrightnessController.cs`: the hardware implementation. Active targets come
     from `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` (`DisplayConfigGetDeviceInfo` for the GDI
     device name, the friendly name and the PnP device path); an active path whose
     `outputTechnology` has the internal high bit set is an internal panel driven over WMI
     (`WmiMonitorBrightness` / `WmiMonitorBrightnessMethods`), otherwise it is external and driven
     over DDC/CI (`dxva2` VCP `0x10`). EXP02's guards are carried: a write stays gated on a
     corroborated read, and a NULL physical-monitor handle is not treated as failure.
  3. `Program.cs`: replace the hardcoded rows with real `Detect()`; each row binds to a real
     monitor; the slider calls `BrightnessWriter.Request` and a `DispatcherTimer` calls `Advance`;
     the label shows the target at once; an unreadable monitor gets a disabled slider and a muted
     note; pending writes flush on close.
  4. Verify by driving: (a) `writer-test.ps1` - headless debounce assertions with a fake controller
     and an injected clock; (b) `verify-control.ps1` - UI Automation on the real window: assert the
     row set and each row's shown value against an independent read, drive one slider with
     `RangeValuePattern` away from and back to the original, and assert an **external** read-back
     (the read-only `probe.exe`) follows.
- May touch: branch `EXP05` off `EXP04`; `experiments/EXP05/README.md`; the overview row;
  `src/Monitorn.App/` (`Program.cs`, new `Brightness.cs` / `BrightnessController.cs` /
  `BrightnessWriterTests.cs`, `build.ps1` + a `System.Management` reference, new `writer-test.ps1`
  and `verify-control.ps1`); and the two repo facts the user asked to fix: `README.md`'s `.NET 8`
  claim -> net48, and `src/probe/Probe.cs:107` -> restore the `SetMonitorBrightness` method name
  (then rebuild `probe.exe` so source and binary agree).
- Off-limits: VCP `0x60` (input) and `0xD6` (power) - the bricking vector; the DDC/CI
  capabilities-string call; persistence; the scheduler; the tray shell; sunset lookup;
  `src/probe/Probe.cs` beyond the one-word fix; anything outside
  `D:\Playground\Projects\Apps\Monitorn\`; downloads.
- Escalation budget: exactly one reversible write per monitor per verifier run, restored to the
  original read; no write if the initial read is unreadable.

## Open questions (resolved before the run)
- [x] Write model - **debounce** (~250 ms settle), latest value wins. (user)
- [x] Row set - **active rendering targets only**, from `QueryDisplayConfig`. (user)
- [x] Repo facts - **fix both** (`README.md` -> net48, `Probe.cs:107`). (user)
- [x] Code home - **in the app** (`src/Monitorn.App/`); no `Monitorn.Core` library yet. (user)
- [x] Independent witness - the read-only `probe.exe`, so the assertion does not rest on the app
      agreeing with itself. *Assumption.*
- [x] Debounce delay - **250 ms**; a failed write is surfaced, not retried silently. *Assumption.*
- [x] Branch - `EXP05` off `EXP04`.

## Results (run 2026-10-15)
Built and driven on this machine (Windows 11, the AOC `Q27G3G3R3` over DDC/CI), every run exiting
cleanly:

| Pass | Command | Result |
|---|---|---|
| Build | `src\Monitorn.App\build.ps1` | `Monitorn.App.exe` from the in-box `csc`, **0 MB**, now with a `System.Management` reference and the new `Brightness*.cs` sources |
| Machine | `src\Monitorn.App\writer-test.ps1` | **19 assertions, 0 failed** - a drag settles into one write of the last value, an unchanged value is a no-op, a failed write is surfaced and not retried until the next move, monitors debounce independently, and closing flushes a pending value |
| Detection | `verify.ps1` (screenshot) | one real row: **`Q27G3G3R3`, 30** - matching the independent probe's read, not a sample |
| End to end | `src\Monitorn.App\verify-control.ps1` | **11 checks, 0 failed**: 1 row for 1 active target, the row shows the probe's read, the slider drives **30 -> 20 -> 30** with the **probe's own read following each step**, and the gear still opens the night-light settings window |

Findings:
- **The hypothesis holds.** The app detects the active display, shows its real brightness, and a
  slider drag is written to that monitor and confirmed by an **independent** read (`probe.exe`) -
  30 -> 20 -> 30, restored. The app is not agreeing with itself; the witness is EXP02's separate
  tool.
- **The debounce is the machine, and it is honest:** 19 headless assertions drive it with a fake
  controller and an injected clock, with no sleep and no hardware. It is why a drag over slow
  DDC/CI issues one write (of the final value) rather than a burst.
- **Identity and the row come from the display config, not a sample table.** The friendly name
  (`Q27G3G3R3`) and the PnP device path come from `DisplayConfigGetDeviceInfo`; the internal/external
  split is the `outputTechnology` high bit. The NULL physical-monitor handle EXP02 flagged is again
  irrelevant - the value round-trips anyway.

Boundaries this run did not exercise:
- **The internal-WMI path is implemented but not exercised here:** this machine has one **active
  target** and it is external, and rows are active targets only (the user's call), so the
  non-rendering internal panel is, by construction, not a row. WMI is the path an internal *active*
  panel would take.
- EXP03's and EXP04's own verifier scripts hard-code the old sample rows (`AOC Q27G3G3R3`); the
  surface's data is now real, so those scripts' name assertions are stale. Their records stand as
  the records of their experiments; this one supersedes only the *data source* of the surface.
- Nothing is persisted and no schedule is applied; the sunset range and the settings window are as
  EXP04 left them.

## Conclusion (findings)
_Pending._

## Learnings
- `src\Monitorn.App\writer-test.ps1` - builds and runs the headless `BrightnessWriter` assertions
  with the in-box `csc` (`/target:exe`; a winexe cannot print). Compiles only `Brightness.cs`, so
  the debounce is testable without touching `dxva2`/WMI. Use it whenever control logic changes.
- `src\Monitorn.App\verify-control.ps1` - drives the real surface through UI Automation and proves a
  slider reached the monitor: it reads the display **independently with the read-only `probe.exe`**,
  drives the slider with `RangeValuePattern`, and asserts the probe's own read follows, then
  restores. Use it instead of describing a monitor write.
- A WPF `Slider` is driven by `RangeValuePattern` (`SetValue`), not keystrokes: it needs no OS
  foreground and the `ValueChanged` handler fires exactly as a drag would (`Maximum` is on the
  pattern's `Current`, so the target can be clamped without guessing 0-100).
- Real monitor identity comes from `DisplayConfigGetDeviceInfo`: `GET_SOURCE_NAME` gives the GDI
  device name (`\\.\DISPLAYn`), `GET_TARGET_NAME` the friendly name and PnP device path. The
  internal/external split is `DISPLAYCONFIG_PATH_TARGET_INFO.outputTechnology >= 0x80000000`.
- `EnumDisplayMonitors` lists exactly the active rendering targets, so it matches the active paths
  by GDI device name; a non-rendering internal panel is absent from it (as EXP02 found).
- WMI brightness lives at `root\wmi`: `WmiMonitorBrightness.CurrentBrightness` to read,
  `WmiMonitorBrightnessMethods.WmiSetBrightness(Timeout=0, Brightness)` to write. Reference
  `System.Management.dll` from the in-box framework directory (`...\v4.0.30319\`).
- The in-box `csc` is C# 5: pass the injected clock as `delegate { return DateTimeOffset.Now; }`
  (no lambda-with-body-free syntax sugar from later C#), and avoid `=>`, `nameof`, interpolation.
- A `CommandLine` event name that collides with a method (`Applied` event vs `Applied(id)`) is a
  compile error; name UI events and getters apart (`Written` vs `Applied`).
