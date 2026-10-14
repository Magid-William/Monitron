# Prior art — APIs & monitor identity

Reference for how Monitorn will actually control brightness and remember monitors. Gathered
in EXP01; the concrete probe of *this* machine's monitors is EXP02.

## 1. External monitors — DDC/CI over `dxva2.dll`
The mechanism used by Monitorian, Twinkle Tray and Display Dimmer.

1. `EnumDisplayMonitors` → one `HMONITOR` per display.
2. `GetMonitorInfo` / `MONITORINFOEX` → `szDevice` (e.g. `\\.\DISPLAY1`) and bounds.
3. `GetNumberOfPhysicalMonitorsFromHMONITOR` + `GetPhysicalMonitorsFromHMONITOR`
   → `PHYSICAL_MONITOR { hPhysicalMonitor, szPhysicalMonitorDescription }` per physical panel
   (so two panels on one connector, or a DP-MST hub, are still separate).
4. Capability probe: `GetMonitorCapabilities` (check `MC_CAPS_BRIGHTNESS`) and/or
   `GetVCPFeatureAndVCPFeatureReply`.
5. Read/write: `GetMonitorBrightness` / `SetMonitorBrightness`, or raw
   `GetVCPFeatureAndVCPFeatureReply` / `SetVCPFeature` with VCP codes:
   `0x10` brightness, `0x12` contrast, `0x60` input source, `0xDF` power mode.
6. Always `DestroyPhysicalMonitors` when done.

**Reliability:** DDC/CI is flaky at boot and after resume; expect timeouts. Plan: retry with
backoff, keep the last good value, and re-assert on `WM_DISPLAYCHANGE`, `WM_DEVICECHANGE`,
session unlock, and power-resume.

## 2. Internal / laptop panels
Not DDC/CI. Use WMI `root\wmi` → `WmiMonitorBrightness` (get) and
`WmiMonitorBrightnessMethods.WmiSetBrightness` (set), or the Windows power/brightness API.
Monitorian treats these as a separate path.

## 3. Pixel / gamma dimming (context, not Monitorn's path)
`SetDeviceGammaRamp` darkens the image — no backlight change, works even without DDC/CI, but
is bypassed by HDR. This is f.lux / Night Light / DimScreen territory.

## 4. Monitor identity across disconnect/reconnect
The hard part. There is no single perfect key:

| Key | Stable same port | Stable *different* port | Unique for 2 identical models |
|---|---|---|---|
| `szPhysicalMonitorDescription` (model string) | mostly | mostly | **No** |
| Windows **device instance ID** (`DISPLAY\DEL41A3\5&…UID4352`) | Yes | **No** — the `UID` encodes the connector, so it changes | Yes |
| **EDID** (manufacturer + product + serial) | Yes | Yes | Usually (if the vendor set a serial) |

- `DISPLAYCONFIG_TARGET_DEVICE_NAME` (via `DisplayConfigGetDeviceInfo`) gives a friendly name,
  `edidManufactureId` / `edidProductCodeId`, `connectorInstance`, and `monitorDevicePath` —
  but the path also encodes the connector.
- The EDID blob (registry `HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY\...\Device Parameters\EDID`)
  can be parsed for manufacturer, product and serial.

**Decision (proposed default):** key per-monitor profiles by **EDID (mfr+product+serial)
first, falling back to device instance ID**, with a **user-editable monitor name**. Matches
instantly on same-port reconnect; survives port changes when the monitor reports a real serial;
degrades gracefully when it does not. Exact key finalized in **EXP02** after probing this
machine's actual monitors.

## 5. Windows' own display config
The CCD API (`GetDisplayConfigBufferSizes` / `QueryDisplayConfig` / `DisplayConfigGetDeviceInfo`)
is the modern source for display topology and target names; useful for friendly names and for
noticing topology changes.
