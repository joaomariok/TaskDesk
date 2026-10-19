---
name: ui-check
description: Check a TaskDesk UI change in the running app with winapp, in Light and Dark, before and after. Use after changing XAML, styles, theme brushes or layout, and when asked for a UI check, a visual check or the UI tests.
---

# UI check

Two passes: the UI suite (does it behave) and a look at the states the change touches (does it look right). Both take over the desktop, so first tell the owner not to type or click until you say so. Nothing touches the owner's TaskDesk: every run uses a temp data folder.

## 1. Behaviour

Run `pwsh scripts/ui-tests.ps1` and judge it by the exit code. It reruns each failed test once by itself and prints `FLAKE: <test>` (passed on rerun) or `FAILED: <test>` (failed twice, a real failure). Exit 0 is a clean pass, 5 is a pass with flakes, reported as such, and 6 means the desktop is locked: ask the owner to unlock it, then run again. For each `FAILED` test, read its PNG in the folder printed as "Failure screenshots" (a dialog in the way, focus elsewhere, text in the wrong box).

## 2. Look

1. Make a folder `<check>` for this check in the session scratchpad. From the diff, list the **states** it touches: each a window size, theme and the steps that reach it (for example "detail pane open, Due time rejected"). Every state is shot in Light and in Dark.
2. Start the app: `pwsh scripts/run-sandbox.ps1 -Theme Light|Dark [-Width <px>] [-SampleTasks 30]`. It returns at once and prints the app's pid. `-Width 820` shows the icon rail; `-SampleTasks` adds Tasks with Tags, Subtasks, Due, Overdue and Completed, all in the Default List; make other Lists and Reminders with `winapp ui`.
3. Reach each state with `winapp ui` against `-a <pid>`, then `winapp ui screenshot -a <pid> -o <check>/after/<theme>-<state>.png`. Popups and flyouts need `--capture-screen`. Close the app with `Stop-Process -Id <pid>`.
   - Find slugs with `winapp ui search "<text>" -a <pid>` or `inspect`.
   - Type into a box with `winapp ui focus <slug>`, then `winapp ui send-keys "<text>" --verbatim --via send-input`, then `send-keys tab --via send-input`. `set-value` skips TaskDesk's LostFocus bindings, so nothing validates or saves.
   - Wait for a result with `winapp ui wait-for "<text>" -a <pid>` rather than sleeping.
   - Open Settings with `winapp ui send-keys "ctrl+," -a <pid> --via send-input`; no Settings button is searchable.
   - Set `NO_COLOR=1` before parsing `winapp` output in a script: its colour codes break text matching.
4. **Before/after**, when the change alters something that already exists: repeat steps 2 and 3 with `-Ref main` into `<check>/before`, with the same names, theme, width and sample count. A ref from before the WinUI swap (#68) builds the WPF app, under the same project path and exe name. Then run `pwsh scripts/compare-shots.ps1 -Before <check>/before -After <check>/after` and read every image in `<check>/after/compared`: before | after | changes in magenta. Each magenta area is either the intended change or a side effect to report. For a new state there is no before.
5. In every shot, check:
   - text clipped, cut off or overlapping another control;
   - a control running past its pane or the window edge;
   - text hard to read against its background, or a colour that ignores the theme (a light patch in Dark, or the reverse);
   - a list that should show content but is empty;
   - an input without a visible label or hint;
   - rows, columns or splitters out of line with their neighbours;
   - controls side by side in one row that differ in height (DESIGN.md, the Default-Size Rule).

## Report

The suite result first (passed, failed or flaky), then the states looked at, then each visual finding with the full path of its shot, what is wrong and where. A finding is a candidate for the owner to confirm, not a verdict. Say "no visual findings" when there are none. Fix nothing unless the owner asks.
