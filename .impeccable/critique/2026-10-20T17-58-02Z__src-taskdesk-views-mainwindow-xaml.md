---
target: main window
total_score: 27
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 2
target_identity: "file:D:\\GIT\\TaskDesk\\src\\TaskDesk.Views\\MainWindow.xaml"
target_fingerprint: "sha256:fecf6f9403ebc7e2ab418aff2c8210e361493d391a1e85b00ec8dfaec457dc14"
target_path: "D:\\GIT\\TaskDesk\\src\\TaskDesk.Views\\MainWindow.xaml"
timestamp: 2026-10-20T17-58-02Z
slug: src-taskdesk-views-mainwindow-xaml
---
Method: design review only (detector not applicable, native platform; PRODUCT.md platform reads "web" only because the schema has no Windows value). Evidence: XAML of MainView, NavigationPane, TaskListView, TaskRow, DetailPane, DetailDock, Foundation.xaml; DESIGN.md; surface brief; 18 screenshots of a sandboxed build (Light 900x600, Dark 1240x700 and 820x600, 30 sample Tasks plus a blue "Work" List).

## Design Health Score
| # | Heuristic | Score | Key Issue |
|---|---|---|---|
| 1 | Visibility of System Status | 2 | Quick add on Upcoming/Overdue/Tag pages saves a Task that never appears; Overdue hides 3 of 8 rows silently |
| 2 | Match System / Real World | 3 | Overdue "sáb." reads like an upcoming Saturday; "Yesterday" next to "qua." mixes languages |
| 3 | User Control and Freedom | 3 | Filter button is inert while a Filter is on |
| 4 | Consistency and Standards | 3 | Heading "..." and sidebar context menu differ for the same List; "Add" vs "Add tag" |
| 5 | Error Prevention | 3 | Invisible quick-add result invites duplicate Tasks |
| 6 | Recognition Rather Than Recall | 2 | Shortcuts only in hover tooltips; quick-add target is a vanishing placeholder |
| 7 | Flexibility and Efficiency | 3 | Arrows stop at each group's edge; no multi-select |
| 8 | Aesthetic and Minimalist Design | 3 | "Inbox" caption on most Smart View rows; right-hand row items float |
| 9 | Error Recovery | 3 | Errors under their fields; not exercised live |
| 10 | Help and Documentation | 2 | No shortcut reference anywhere |
| Total | | 27/40 | Acceptable (top of band) |

## Design Specificity Verdict
Honestly native: Mica, Fluent controls, Dark that reads designed. Specificity lives in small pieces: scoped labels ("Add a task to Work", "Search in Work"), relative Due wording, List stripe, count badges. At a glance it is Microsoft To Do's layout (largely the brief's native-first choice), and the signature is thin: the stripe only appears for coloured Lists (the Default List never has one), and where it shows it sits flush against Fluent's selection pill (two blue bars on a selected Blue-List row with a blue accent). The completion moment (400 ms hold) was not captured, so it is unverified. Deterministic scan: not applicable (native platform).

## Overall Impression
Up from 25/40 (2026-10-17): heading chrome, Completed Smart View and the four-way Tag chip are fixed. What is left is structural: the list is three capped boxes, and quick add sometimes swallows the Task. Biggest opportunity: one grouped, single-scroll Task list.

## What's Working
- One-line heading row (name, Filter, "...", scoped search) with quick add directly under it, focused at start-up.
- Meaning-only colour holds in Light and Dark; nothing hard-coded.
- Completed Smart View is right (full-strength checked rows, no quick add, no expander); one Tag chip everywhere; native TimePickers.

## Priority Issues
1. [P1] Task list is three capped boxes. Overdue ListView MaxHeight 160 (TaskListView.xaml:41): Today shows "Overdue (8)" with 5 rows in a nested scroll and ~250 px empty below. "Completed (N)" is a bordered Expander pinned to the window bottom, MaxHeight 160 when open (TaskListView.xaml:62-66). Arrow keys do not cross groups (TaskListView.xaml.cs:130). Fix: one ListView over a grouped CollectionViewSource (Overdue / open / Completed (N) headers, Completed collapsible), no MaxHeight; drag reorder limited to the open group. /impeccable layout
2. [P1] Quick add swallows Tasks. Upcoming/Overdue add an undated Inbox Task; Tag pages add an untagged one (MainViewModel.cs:313-342); nothing appears. Today sets Due today but the label only says "Add a task to Inbox" (MainViewModel.Heading.cs:38). Fix: Tag page applies the Tag ("Add a task to Inbox · #work"); Today label "Add a task due today"; Upcoming/Overdue hide quick add or confirm "Added to Inbox · Show" via the InfoBar pattern. /impeccable harden
3. [P2] Due wording. Within 6 days either side a Due is a bare weekday (DueDisplay.cs:31-38): Overdue "sáb." equals an upcoming Saturday; only red differs, and high contrast drops the red (Words-Too Rule). Today/Tomorrow/Yesterday are English, weekday/month names follow the regional culture. Fix: past dates before yesterday carry the date or "N days ago"; names in the UI language. /impeccable clarify
4. [P2] Filter row. Every Tag is a toggle in a wrapping row (MainView.xaml:107): 10 Tags = 3 lines at 900 px, quick add pushed to y~240. While a Filter is on the Filter toggle is inert (MainViewModel.Search.cs:88-95). Fix: Tags behind one "Tags" drop-down with checkable items; Filter button always hides the row and carries the active count ("Filter · 1"). /impeccable distill
5. [P2] Right-hand row items float. One right-aligned StackPanel (TaskRow.xaml:96): Priority flags at x~1110/1116/1197 in Dark All; "Inbox" caption shifts per row and repeats on 24 of 27 rows. Fix: fixed right-edge slots (Due ~96 px, Priority 16 px always reserved), variable items to their left; hide the List caption for the Default List or single-List results. /impeccable layout

## Persona Red Flags
- Alex (keyboard-first): Down arrow dead-ends at the last visible Overdue row; Ctrl+F at <=1100 px with details open focuses a search box hidden under the overlay (MainView.xaml.cs:65-69; from code and the 900 px shot, not reproduced live); no multi-select; no quick-add syntax; Space/Enter/F2/Delete/Ctrl+N not shown anywhere.
- Sam (screen reader, keyboard, low vision): weekday-only Overdue chips in high contrast; Tag remove button 18x18 with 8 px glyph (DetailPane.xaml:39-49), below WCAG 2.2 24x24; disabled Due time/Reminder time explain themselves only in HelpText; quick-add target is a placeholder that vanishes on typing (MainView.xaml:128-131); high contrast still unchecked.
- The owner at 900x600: opens on Inbox, not Today; details overlay covers search and every Due chip; adding from a Tag page looks like nothing happened.

## Minor Observations
- Rail: a colourless List is a hollow rounded 16 px square (NavigationPane.xaml:82), identical to the Task checkbox; use a ring as the full sidebar does.
- Splitter grips at x~187 and x~898 visible at rest, read as stray scrollbar thumbs.
- Heading "..." menu has Rename, Colour; sidebar context menu for the same List adds Delete.
- "Add" (Subtasks) vs "Add tag" (Tags); "+ New List" uses a typed plus where everything else uses Segoe Fluent Icons.
- Due time / Reminder time render disabled without a date while Repeat hides; pick one rule (hide).
- "Delete Task" text starts ~11 px right of the field labels (button padding).
- "Clear search" and "Load more completed tasks" override Fluent padding (Padding="8,2", MainView.xaml:142) against the Default-Size Rule; "No Tasks match 'zzz'" lacks the full stop the other empty states have.
- Detail overlay at <=1100 px has no elevation, slices row titles mid-word; Fluent-Lift Rule allows Fluent's shadow.
- DESIGN.md says rows are 26 px with 8x1 padding; code is MinHeight 32, Padding 8,0 (TaskListView.xaml:16-20).
- Due chip always formats HH:mm; the TimePicker follows the system 12/24-hour clock.

## Questions to Consider
- Should the window open on Today, given the brief's story starts with what is due today and Overdue?
- Does a Default List row need its List caption at all?
- Should quick add always produce a Task that belongs to the page, or should pages that cannot (Upcoming, Overdue) not offer it?
- Is a 400 ms dim-and-strike enough of a peak for the moment the brief calls memorable?
