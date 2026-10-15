# Monitron

**Brightness for every monitor — and a night light that dims them for you.**

Monitron sits in your notification area, next to the clock. It gives each of your screens its own
brightness slider, and it can dim them all on a schedule — fade down in the evening, hold the low
level overnight, and put every screen back the way it was in the morning.

## What it does

- **A slider for every screen.** Your main monitor bright, your second one dim — each keeps its own
  level. External monitors and laptop panels both work.
- **A night light that remembers.** Choose a start time, an end time, and how dim to go. Monitron
  fades down when the night begins and gives each screen back exactly the brightness it had before —
  so nothing is left dark when you don't expect it.
- **Out of the way.** Close the window and it keeps running from the tray. Open it again from the
  tray icon whenever you like, or switch on *Run on Windows startup*.
- **Matches your PC.** The windows follow your Windows light or dark theme, and it carries its own
  icon.

## Using it

1. **Set a screen** — drag its slider.
2. **Set the night light** — click the gear on a row, switch it on, and pick the hours and strength.
3. **Leave it running** — close the window; the tray icon brings it back, or quits it.

## Getting it

There is no installer yet. Monitron is a single program and needs nothing installed alongside it —
it runs on Windows 10 and 11 as they come.

Build and run it:

```powershell
pwsh -File src\Monitron.App\build.ps1
.\src\Monitron.App\Monitron.App.exe
```

## For developers

The app is C# / WPF, written in code only (no XAML), and built with the .NET Framework 4.8 compiler
that ships with Windows.

Every claim in the project is verified by **driving** the real thing rather than describing it: the
headless `*-test.ps1` suites assert the state machines with an injected clock, and the `verify-*.ps1`
scripts drive the real windows through UI Automation and read the real displays.

The work is organised as one hypothesis per experiment, on its own branch, with a written record —
the log is [`experiments-overview.md`](experiments-overview.md) and the prior-art research is in
[`docs/prior-art/`](docs/prior-art/).

## License

MIT — see [`LICENSE`](LICENSE).
