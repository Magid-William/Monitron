# Monitron

A Windows tray app that controls the brightness of **each connected monitor** and **dims them on a
schedule** — for example, fade down to a low level at 21:00 and restore the previous brightness at
06:00.

- **Per-monitor brightness.** External displays over **DDC/CI** (`dxva2.dll`); internal laptop
  panels over the Windows **WMI** brightness API. Each display gets its own row and slider, and the
  row's slider is named and shows keyboard focus for a screen reader.
- **A scheduled night light.** A start and end time plus a strength; Monitron fades down at the
  start, holds the level, and gives every display its own previous value back at the end. Closing
  the window does not stop it — the schedule keeps running from the notification area.
- **A tray shell.** Show/Hide, *Run on Windows startup*, and Quit.
- **Dark mode.** Both windows follow the Windows app theme.
- Its own icon: the designed logo (`logo.svg`), generated into a nine-frame `Monitron.ico`.

## Build and run

No SDK and no Visual Studio are needed — the app is code-only WPF (no XAML) built with the .NET
Framework 4.8 compiler that ships with Windows:

```powershell
pwsh -File src\Monitron.App\build.ps1
.\src\Monitron.App\Monitron.App.exe
```

Verification is by **driving**, not by describing: the headless `*-test.ps1` suites assert the state
machines with an injected clock, and the `verify-*.ps1` scripts drive the real windows through UI
Automation and read the real displays.

## How it is built

One hypothesis per experiment, on its own branch, with a written record: the log is
[`experiments-overview.md`](experiments-overview.md) and each experiment's detail lives in
`experiments/EXP0N/README.md`. Prior-art research is in [`docs/prior-art/`](docs/prior-art/).

## Status

Working, pre-1.0. Brightness control, the schedule, settings persistence, theming, placement, the
tray shell, the icon and per-row accessibility are implemented and verified on real hardware.
Next: per-monitor profiles that survive unplug/reconnect, and real sunset/sunrise by location.

## License

MIT — see [`LICENSE`](LICENSE).
