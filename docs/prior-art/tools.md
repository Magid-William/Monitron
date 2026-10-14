# Prior art — tools

Survey from EXP01. "Scheduling" = can change brightness on a time/day rule without external
tools. "Backlight" = controls the monitor's real brightness (DDC/CI or WMI), as opposed to
gamma/pixel dimming.

| Tool | Type / license | Backlight control | Scheduling | Cost | Note |
|---|---|---|---|---|---|
| **Twinkle Tray** | Electron + Node, **MIT**, MS Store + GitHub | DDC/CI + WMI (internal) | **Yes** — time of day, idle, sun position, **per display**; app profiles | Free | **The real competitor** — does the whole job already |
| **Monitorian** | WPF, .NET Framework 4.8, **MIT** (core) | DDC/CI via `dxva2` + internal | Paid Store add-on "Time Commands"; not in public repo | Free core / paid add-ons | Closest code reference; scheduling is exactly the paid part |
| **Display Dimmer** | **WPF/.NET**, closed source | DDC/CI **+ gamma dimming** | Yes — time schedules (1 rule free, more with Pro), app rules | Free + one-time Pro | Closest UX; scheduling partly Pro-gated |
| **MonitorPilot** | Commercial (Automata Labs) | DDC/CI | Yes — day/night rules that dim + warm | Paid | Marketing hits the same need |
| **ClickMonitorDDC** | Freeware (dev stopped) | DDC/CI | No (hotkeys/CLI only) | Free | Manual control, abandoned |
| **ControlMyMonitor** | Freeware (NirSoft) | DDC/CI, raw VCP | No (CLI) | Free | Excellent diagnostic/probing tool |
| **DimScreen** | Freeware | Gamma (pixel) | No | Free | Image-only dimming |
| **Win10 Brightness Slider** | Small OSS | WMI/DDC | No | Free | Minimal |
| **f.lux** | Freeware | **Gamma only** — not backlight | Yes — color temperature / "bedtime" | Free | Changes warmth; does **not** dim the backlight |
| **Windows Night Light** | OS built-in | **Pixel color temperature** | Yes — hours or sunrise/sunset | Built-in | Color only; no brightness effect |
| **KTC time-based presets** | Monitor/vendor firmware | OSD preset | Vendor time-based presets | Hardware | Monitor-specific, not a PC app |

## Findings
1. **Twinkle Tray alone falsifies the hypothesis.** Free, mainstream, MIT, DDC/CI brightness
   scheduled by time of day per display.
2. **The mechanism is settled:** serious tools use DDC/CI (`dxva2.dll`) for external monitors
   and WMI for internal panels. Monitorn's planned design is the proven one.
3. **Backlight ≠ pixels.** f.lux / Night Light / DimScreen change the image; only DDC/CI/WMI
   tools change the backlight. Monitorn is in the backlight family.
4. **Scheduling is where the paid walls are.** Monitorian gates it; Display Dimmer partly gates
   it; Twinkle Tray gives it away. Monitorn's overnight-fade rule must therefore compete on
   *fit*, not on existence.

## Sources
- Twinkle Tray — <https://twinkletray.com/> ; feature list via ghacks review (2022-07-12).
- Monitorian — <https://github.com/emoacht/Monitorian> (README: DDC/CI, Time Commands add-on).
- Display Dimmer vs Twinkle Tray — <https://displaydimmer.com/display-dimmer-vs-twinkle-tray>.
- ControlMyMonitor — <https://www.nirsoft.net/utils/control_my_monitor.html>.
- f.lux behavior — justgetflux.com FAQ / forum ("doesn't adjust the backlight").
- Windows Night Light — Microsoft support ("Adjust Night light strength", color temperature).
