# Monitorn

A Windows tray app that controls the brightness of each connected monitor and **dims
them on a schedule** — for example, fade down to a low level at 21:00 and restore the
previous brightness at 06:00.

- External monitors: **DDC/CI** via `dxva2.dll` (per-monitor, like Monitorian).
- Internal laptop panels: the Windows WMI brightness API.
- A notification-area icon: **Show/Hide**, **Run on Windows startup**, and **Quit**; the schedule keeps
  running while the window is closed.
- Planned: per-monitor profiles that survive unplug/reconnect.

Stack: **C# / WPF on .NET Framework 4.8** (built with the in-box `csc.exe` - no SDK needed).

## Status
Pre-implementation. Prior-art research lives in [`docs/prior-art/`](docs/prior-art/);
the experiment log is [`experiments-overview.md`](experiments-overview.md).
