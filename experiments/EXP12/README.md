# EXP12 - The app icon: the neon-green logo, as the app's one multi-size .ico

## Summary
`logo.svg`'s mark is painted one neon green (#02DA96), and that one SVG becomes the app's `.ico` -
nine frames, carried by the exe, the window and the tray.

## Hypothesis
The look was never the problem; the icon was. With the mark's own paint set to one colour - no
recolouring in the renderer - the generated `Monitron.ico` is a proper icon: the nine frames Windows
asks for, reproducible from the committed SVG, and the very frames the exe carries, so
`PrivateExtractIcons` on the built exe returns each of them byte for byte, and the shell and the app
draw that asset on the exe, the window and the tray.

## Methodology & blast radius
- Method: the SVG is the source of truth and the pipeline already in the tree is used unchanged -
  `SvgIcon` reads the paths, `IconGen` fits and rasterises them, `IcoFile` packs them, `IconAssets`
  hands the embedded asset to the window and the tray. `icogen.ps1` regenerates the committed asset;
  `build.ps1` embeds it with `/win32icon:` (Explorer, Alt-Tab, the exe's Properties) and
  `/resource:` (the window icon and the tray). No renderer on this machine and no download.
- May touch: `logo.svg` (the six paint values only), `src/Monitron.App/IconTests.cs` (the one
  assertion that the logo is black), `src/Monitron.App/Monitron.ico`, this file,
  `experiments-overview.md`.
- Off-limits: `logo.figcraft` (the design source, untouched - it still describes a black mark),
  the brightness/schedule/placement/theme paths, `src/probe`, an installer or code signing.

## Open questions (known unknowns)
- [ ] The colour - the user's call, and it took two tries: black (3.2:1 on this machine's dark
      taskbar, i.e. invisible there), then one mid grey (#A0A0A0), which read washed out on light,
      then the shipped **`#02DA96`**. Adopted.
- [ ] Small frames - the same geometry at every size: no ink boosting and no detail dropped, so the
      16-24 px frames keep the ticks and the thin ring. Adopted.
- [ ] Verification - `icon-test.ps1` headless plus the user's own end-to-end pass; the `verify-*.ps1`
      suites were **not** run for this experiment, at the user's instruction. Adopted.

## Conclusion (findings)
The hypothesis held, and the part that was ever wrong was never the frames. Changing the six paint
values in `logo.svg` to `#02DA96` and running the pipeline unchanged produced a nine-frame
`Monitron.ico` (16/20/24/32/40/48/64 as DIB, 128/256 as PNG; 71197 bytes) that a byte audit finds
spec-correct - `ICONDIR` and entries, `biHeight = 2 x size`, 32bpp BI_RGB, bottom-up XOR, zeroed AND
mask, 8-bit RGBA PNG frames - and all nine frames sit **byte for byte** inside the rebuilt
`Monitron.App.exe` (245248 bytes), which is what the app draws with: its own report says
`resource=Monitron.ico`, `frames=16,20,24,32,40,48,64,128,256`, `windowIcon=true`, `trayIcon=true`,
and the exe's icon sampled at 256 px is now (3,190,131) - the green - against (140,140,140) for the
grey build before it. `icon-test.ps1` is green (49 passed, 0 failed, including that the committed
asset is exactly what `icogen` writes today) and the user confirmed the result on the real exe, tray
and app.

Two things made this look like "the icon did not change", and neither was the icon: **the exe cannot
be rewritten while the app runs** (`csc` fails - *"used by another process"*), and the tray icon is
read **once, at startup, from the exe the process was started from**, so a rebuild only shows after
a quit and a restart. Explorer's own icon cache (keyed by path) is the third place an old mark can
linger. The earlier attempt's real defects were in its build story, not in its byte container.

Boundaries: `logo.figcraft`, the design source, still describes a black mark, so re-exporting from
the design tool would undo the colour; the 16-24 px frames are still the fine-detailed mark (the
ticks and the thin ring survive at 20 px - the size the shell draws here - but they are small); the
green carries the identity by hue rather than by luminance (3.6:1 over the taskbar, 1.8:1 over
white); `verify-icon.ps1` was not run, so "the shell's icon for the exe" is witnessed here by the
exe's bytes and the app's own report, not by the shell.

Recommend next: the standing steps - the stored `theme=` preference, then real sunset/sunrise, then
per-monitor profiles.

## Learnings
- `pwsh -File src\Monitron.App\icogen.ps1` regenerates `Monitron.ico` from `logo.svg` (`-Png <dir>`
  also writes one PNG per frame - how a small size gets judged); `build.ps1` then embeds it with
  `/win32icon:` (Explorer, Alt-Tab, the exe's Properties) and `/resource:` (window and tray).
- `pwsh -File src\Monitron.App\icon-test.ps1` is the headless check after an SVG edit: it asserts the
  committed `.ico` is exactly what `icogen` writes today, so a stale or hand-edited asset fails.
- To see a new icon: **quit the app, rebuild, start it again**. `csc` cannot write the exe while the
  app runs, and the tray icon is read once at startup from the exe the process came from. Explorer
  caches icons by path, so its listing can lag too.
- The exe's icon resource holds the `.ico`'s frames verbatim, so "does the app carry it" is a byte
  search of the built exe for each frame - cheaper than `PrivateExtractIcons`; when pixels must be
  compared, use `PrivateExtractIcons` plus
  `DrawIconEx(hdc, 0, 0, hIcon, w, h, 0, IntPtr.Zero, 3 /* DI_NORMAL */)`, because
  `Image.FromHbitmap` loses the alpha.
- `build.ps1` regenerates the asset only when it is missing or older than `logo.svg`: editing the
  generator alone leaves the committed `.ico` stale.
- `Geometry.Parse(d)` returns a **frozen** `PathGeometry` whose default rule is even-odd: `Clone()`
  it before setting `FillRule` or `Transform` (the rule lives on `PathGeometry`).
- `Window.Icon` takes the **largest** frame (256): WPF turns the single `BitmapSource` into one HICON
  and Windows scales it down.
