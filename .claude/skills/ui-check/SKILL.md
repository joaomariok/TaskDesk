---
name: ui-check
description: Run TaskDesk's UI test suite and eyeball screenshots of the main window in Light and Dark. Use when asked for a UI check, a visual check, or to run the UI tests, and after XAML, theme or layout changes.
---

# UI check

Two passes over the real window: the UI suite (does it behave) and a screenshot review (does it look right). The suite drives the keyboard and mouse, so the desktop is taken over for about a minute.

## Steps

1. Tell the owner the desktop will be busy for about a minute and that nothing should be typed or clicked meanwhile. Close nothing of theirs; the tests run against temp data folders and never touch the owner's TaskDesk.

2. Run, with a new folder in the scratchpad:

   ```
   pwsh scripts/ui-tests.ps1 -Screenshots <scratchpad>/ui-shots-<n>
   ```

   Judge the result by the exit code. On failure, report each failing test with its message, then rerun only that class (`-Class <name>`) once: a pass on rerun is a **flake** to report as such, a second failure is a real failure. Continue to step 3 either way if screenshots were written.

3. Read every PNG in the folder (10 when the tour passes: `light-` and `dark-`, steps 01 to 05 in the order empty, tasks, details, list-and-tag, completed). For each, check:
   - text clipped, cut off or overlapping another control;
   - a control running past its pane or the window edge;
   - text that is hard to read against its background, or a colour that does not follow the theme (a light patch in Dark, or the reverse);
   - a pane or list that should show the Tasks, Tags or Lists the step created but is empty;
   - inputs with no visible label or hint saying what they are for;
   - columns, splitters or rows out of line with their neighbours.

   Ignore the strip of desktop showing through at the window's outer edge (the capture includes the invisible resize border) and the translucent window backdrop.

4. Report the suite result first (passed, failed, or flaky), then the visual findings as a list, each with the screenshot's full path, what is wrong, and where in the window. A visual finding is a candidate for the owner to confirm, not a verdict. Say "no visual findings" when there are none. Fix nothing unless the owner asks.
