---
version: 1
slug: "src-taskdesk-app-mainwindow-xaml"
primary_target: "src/TaskDesk.App/MainWindow.xaml"
related_targets: ["src/TaskDesk.App/Views/NavigationPane.xaml","src/TaskDesk.App/Views/TaskListArea.xaml","src/TaskDesk.App/Views/DetailPane.xaml"]
---

# Surface brief: main window

Recorded after the build (spec #29, landed on main at c1d4621) from the direction agreed in the shape and grilling sessions. Global product truth is in PRODUCT.md and the visual system in DESIGN.md; this brief keeps only what belongs to this surface.

## Scope and mode
The TaskDesk main window: sidebar (`Views/NavigationPane.xaml`), Task list with heading, quick add and filters (`Views/TaskListArea.xaml`), and the detail pane (`Views/DetailPane.xaml`), hosted by `MainWindow.xaml`. Mode: **Operate**.

## Audience, job, action
- The owner, all day, with the window at mid-size and popped in and out of other work.
- Job: see what needs doing now, capture a Task in seconds, finish it in one keystroke.
- Primary action: type in quick add and press Enter. Second: Space to complete the selected Task. Everything is reachable by keyboard.
- Content: Tasks with Due, Priority, Tags, Subtasks, Recurrence and Reminders; Smart Views (Today, Upcoming, Overdue, All, Completed), Lists and Tags.
- Constraints: native Windows 11 Fluent only; minimum 800 × 500; Light, Dark and high contrast.

## Chosen direction and memorable moment
List first, details on demand. The list owns the window; the detail pane appears only when asked and stays closed when closed. The memorable moment is completion: the round checkbox fills with the accent, the row dims and is struck through in place, then settles into "Completed (N)".

## Direction contract
- THESIS: The list is the whole job, and everything else waits to be asked for. It refuses the category-default three-pane mail layout in which a detail pane is always open and eats the width of a mid-size window.
- OWN-WORLD: Stock Fluent surfaces, Segoe UI on Fluent's five-step ramp, and the user's own Windows accent, with a soft TaskDesk signature: a round 20 px Task checkbox, pill chips for Due and Tags, count badges, a 3 px List-colour stripe. Colour beyond the neutrals and the accent means Overdue (critical red), today (accent text) or a List's own colour.
- STORY: The owner opens the window and sees at once what is due today and what is Overdue. They add a Task with Enter, complete one with Space, and open details only when a Task needs editing. They trust that nothing is lost and nothing gets in the way.
- FIRST VIEWPORT: At 900 × 600, a 180 px sidebar on the left (Smart Views with icons and counts, then Lists, then Tags) and the list taking the rest. In the list, top to bottom: the page heading at subtitle size with the "⋯" List menu, filter chips on their own row, the scoped search box, the labelled quick-add box (the primary action, focused at start-up), then one-line rows. No detail pane: it overlays the right of the list on demand and docks only above 1100 px. Below 900 px the sidebar is a 56 px icon rail.
- FORM: list-first, details on demand. The owner chose it from the three dealt sketches (views-across-top, agenda-by-due, list-first-details-on-demand). seed: 7beb4b93dd6ddd20
- FINISH: unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance

## Unresolved decisions
- The Impeccable finish review against this contract has not run; the branch had a code review only. DESIGN.md is written; the build ships no rasters.
- Not yet checked by eye: high contrast, checkbox hover and pressed states, a live theme switch, and rail badge contrast in Light.
- Start-up cost of Fluent loading twice for a saved Light or Dark theme is not measured against the 1.5 s target.
