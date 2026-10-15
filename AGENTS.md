# AGENTS.md

## What this project about
Monitron — a Windows tray app that controls the brightness of individually connected
monitors (externally over **DDC/CI**, internal panels via the WMI brightness API) and
**dims them on a schedule** (e.g. fade down 21:00 → 06:00, then fade back). Prior-art
research and the product thesis live in `docs/prior-art/`.

Stack: **C# / WPF on .NET Framework 4.8** (chosen in EXP01 planning; mirrors Monitorian's
approach). Built code-only (no XAML) with the in-box `csc.exe` - no SDK and no Visual Studio needed.

## Experiment workflow
Experiments are numbered EXP01, EXP02, ... One git branch per experiment,
one `experiments/EXP0N/README.md` per experiment. No change lands without an
experiment that states why.

| Step | Trigger | Action | Overview status |
|---|---|---|---|
| 1 Number   | user declares a new experiment | run `git log --oneline --branches`, read `experiments-overview.md`, tree `experiments/`; pick the next free number -> branch `EXP0N` | - |
| 2 Plan     | "start EXP0N ..." | plan mode: formalize + research viability; output Summary / Hypothesis / Methodology & blast radius / Open questions. Each open question carries a proposed default. No files written in plan mode. | - |
| 3 Record   | plan approved (build mode) | create the branch, write `experiments/EXP0N/README.md` from the template below, add an overview row | `todo` |
| 4 Run      | work begins | do the work; keep Learnings current as discoveries happen | `inProg` |
| 5 Conclude | user says "conclude EXP0N" | write the Conclusion/Findings, compress the Learnings, commit all changes on the branch, update the overview row | user-declared |

## Status
`todo | inProg | success | failed | abandoned`

Only the USER declares `success`, `failed` or `abandoned`. The agent recommends
and waits; it never declares a status on its own. Findings that contradict the
goal are a candidate for
`failed` -> propose it, do not set it.

`abandoned` is set only when the user says to abandon the experiment.

## Rules
- It's a **sin** to explore other folders outside `the repository root`.
- One experiment = one hypothesis. Make the blast radius explicit before starting.
- Branch per experiment, named after the EXP number (`EXP0N`).
- Experiment **code lands at the repo root** (`src/`, `Monitron.sln`, ...) — that is the app. `experiments/EXP0N/` holds only the README: the record, not the implementation.
- Machine logic is tested by driving it: construct the state machine with an injected clock (`Func<DateTimeOffset> now`), then advance it by invoking its transitions and assert on the exposed state. Never wait on a fixed tick, `Thread.Sleep`, or a real timer. *(This app is FSM-first: the scheduler is a state machine, so every experiment tests a machine.)*
- A UI claim is verified by driving the window, not by describing it: find the controls with UI Automation and drive them through their own patterns (`TogglePattern`, `SelectionItemPattern`, `InvokePattern`, `ValuePattern`), then assert the state they report. Never `SendKeys` for this — keystrokes need the OS foreground, and a click elsewhere on the desktop turns a run into bogus failures. *(Promoted from EXP04, approved: it is what turns a screenshot into an assertion.)*
- **Verify once, when the experiment is done - not after every edit.** Work the experiment to completion; then, before concluding, run the whole verification (the `*-test.ps1` headless suites and the `verify-*.ps1` end-to-end ones), fix what fails and rerun as many times as it takes to go green. *(Promoted from EXP10, approved: running the suite after each change spends the experiment's time re-proving what has not changed - the defects that mattered here, the reused `$p` and the turn-off ramp, were found by driving the finished thing. A single targeted command while chasing one known failure is not a suite run.)*
- Plan mode is read-only: no branch, README or overview edits until the plan is approved.
- The overview is updated at every transition: plan-ready -> in progress -> final status.

## Open questions
- List them at plan time with a **proposed default** for each.
- If the user does not answer, adopt your proposed default, mark it explicitly
  as an assumption, and proceed. Never block waiting on an open question.
- If an assumption is later proven wrong, note it in Learnings and the Conclusion.

## Learnings (agent memory)
- Each experiment keeps a running `## Learnings` list: what was tried, the exact
  command that finally worked, and when to use it. Update it as you go so future
  sessions skip rediscovery instead of repeating it.
- On conclude, compress it to the smallest useful form.
- Durable, reusable facts only; never volatile per-run noise.
- At most ONE key learning may be promoted into AGENTS.md, and only after asking
  the user and getting the reason. Rare on purpose: keep AGENTS.md small.

## Experiment README template

```markdown
# EXP0N - <title>

## Summary
<goal for humans, < 250 chars>

## Hypothesis
<falsifiable prediction>

## Methodology & blast radius
- Method:
- May touch:
- Off-limits:

## Open questions (known unknowns)
- [ ] <question> — proposed default: <answer + why>

## Conclusion (findings)
_Pending._

## Learnings
- `<command that worked>` — use when <condition>
```
