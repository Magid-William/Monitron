# EXP13 - The slider knob: a click lands where you clicked

## Summary
Clicking a row's slider track should put the knob on the clicked level instead of nudging it one
large step, and the row's slider should be named for a screen reader and show where keyboard focus is.

## Hypothesis
A click anywhere on a row's slider track moves the thumb to the clicked position (snapped to the
nearest whole level, clamped at the ends) and the display itself ends up at that level - instead of
the OS slider's +/-LargeChange nudge - so a row is settable without hitting or dragging the small
thumb. And the row's slider carries a name a screen reader can read, and shows where keyboard focus is.

Falsified by: a click that lands on the click point but also adds +/-10; a press on the thumb that no
longer drags; a value that moves while the rendered thumb does not follow it; an empty slider name;
or focusing the slider that changes no pixel.

## Methodology & blast radius
- Method: the jump is WPF's own move-to-point, switched on for the row's slider
  (`IsMoveToPointEnabled = true`) - the OS slider already snaps to the tick, clamps at the ends and
  leaves a press on the thumb to the thumb. The slider gets an accessible name like the gear beside
  it. The focus cue is *measured first* (UIA `SetFocus()` plus a frame-diff at the slider's own
  rect): if the OS focus rectangle already shows on the dark row, nothing is written; if it does
  not, the smallest fix is one accent focus ring beside the other templates.
  The claim is a UI claim, so it is verified by driving: a real, guarded mouse click on the track
  (foreground verified first, point asserted inside the slider's own UIA rect, cursor restored),
  witnessed from outside the app - the rendered pixels at the filled/unfilled boundary, the display's
  own reading (the EXP02 probe for the external row, an independent `Get-CimInstance` for the panel),
  a thumb drag that must still work, and the facts a screen reader sees over UI Automation.
- May touch: `src/Monitorn.App/Program.cs` (the row's slider), `Theming.cs` (the focus ring only, if
  the measurement says it is needed), a new `src/Monitorn.App/verify-slider.ps1`, the EXP13 row in
  `experiments-overview.md`, and the header comments naming this experiment.
- Off-limits: the writer/debounce, the scheduler, settings/persistence, placement, the tray, the
  icon, `build.ps1` (unless a new source is added), and every existing verifier - their passes and
  their UIA `SetValue` drives must stay green untouched.

## Open questions (known unknowns)
- [x] How much of the a11y story this experiment carries - click-to-position plus the accessible
      name plus the focus cue. (User's call.)
- [x] Whether a verifier may synthesize a real mouse click - yes, guarded: abort unless the app is
      foreground and the point is inside the slider's own rect, and restore the cursor. The claim is
      about real mouse input, and pixels are its only honest witness. (User's call.)
- [x] Snapped to the tick rather than the raw click point - the slider already snaps, and whole
      levels are what a monitor takes.
- [x] The track click no longer nudges by +/-10; PageUp/PageDown keep `LargeChange` - a deliberate
      behaviour change, recorded as a boundary.
- [x] The thumb's drawn size is left alone - the jump removes the need to hit it.
- [ ] If the one property does not hold on its own, which fallback - proposed default: clear the two
      track RepeatButtons' commands in the existing `StyleSlider` (two lines), and only if the
      built-in never fires, own the preview press. The run decides; nothing is assumed.
- [ ] Out of scope by default: a stored a11y preference, an announced live region, driving the keys
      themselves (the standing rule sends no keystrokes), touch/flick, and the settings window.

## Conclusion (findings)
_Pending._

## Learnings
- `git checkout -b EXP13` off EXP12's tip - the branch this experiment lives on.
