---
target: main window
total_score: 25
max_score: 40
na_heuristics: 
p0_count: 0
p1_count: 3
target_identity: "file:D:\\GIT\\TaskDesk\\src\\TaskDesk.App\\MainWindow.xaml"
target_fingerprint: "sha256:bb0f3c006b9188c871d985da239f8ab989c4db73e483288cd63f17956cbdc21e"
target_path: "D:\\GIT\\TaskDesk\\src\\TaskDesk.App\\MainWindow.xaml"
timestamp: 2026-10-17T15-40-34Z
slug: src-taskdesk-app-mainwindow-xaml
---
Method: design review only (detector not applicable: native WPF; PRODUCT.md platform reads "web" only because the schema has no Windows value). Evidence: XAML of the four main-window views, DESIGN.md, surface brief, 20 UI-tour screenshots (31/31 tests passed).

## Design Health Score
| # | Heuristic | Score | Key Issue |
|---|---|---|---|
| 1 | Visibility of System Status | 3 | Detail-pane errors render at the bottom of the scroll area |
| 2 | Match System / Real World | 3 | Literal "x" for Tag removal |
| 3 | User Control and Freedom | 3 | Completed Smart View is a dead end |
| 4 | Consistency and Standards | 2 | Tags render four ways; bordered vs borderless lists; dotted classic focus |
| 5 | Error Prevention | 2 | Free-text HH:mm times; tooltip-only rules; read-only List looks editable |
| 6 | Recognition Rather Than Recall | 2 | Uncoloured rail Lists identical; shortcuts only in tooltips |
| 7 | Flexibility and Efficiency | 3 | No in-window key to quick add; no bulk actions |
| 8 | Aesthetic and Minimalist Design | 3 | Three control bands before the first Task |
| 9 | Error Recovery | 2 | Validation far from its field |
| 10 | Help and Documentation | 2 | No shortcut reference |
| Total | | 25/40 | Acceptable |

## Design Specificity Verdict
Honestly native; meaning-only colour on Due chips is the best detail. The signature pieces are drowned by the stock control stack above the list; the top 160 px could be any Fluent sample. The List stripe never appears in the tour (no coloured List).

## Priority Issues
1. [P1] Chrome above the primary action: heading, filters, search, label, then quick add; detail overlay at 900 px clips the filter row and hides Due chips. Fix: quick add under heading, search in heading row, filters behind a Filter button. /impeccable layout
2. [P1] Completed Smart View: empty main area, no empty state, quick add still targets Inbox, rows in a 160 px bordered expander pinned to the bottom. Fix: render completed in the main list, hide quick add, drop expander; borderless expander list elsewhere. /impeccable distill
3. [P1] Tags render four ways: row pill; filter toggle without "#" (ContentStringFormat at TaskListArea.xaml:149 not rendering); "#health x" button with a literal x (DetailPane.xaml:196); sidebar text misaligned (no icon column). Fix: one Tag chip everywhere. /impeccable polish
4. [P2] Detail pane: free-text times with tooltip-only rules; read-only List TextBox looks editable; ErrorMessage under Delete Task; scroll area clips labels under the fixed header with no divider. /impeccable harden
5. [P2] Narrow rail: uncoloured Lists are identical rings; rail badges white on #8A8A8A ~3.4:1 in Light. /impeccable adapt

## Persona Red Flags
- Alex: no in-window jump to quick add; no multi-select; arrows don't cross Overdue/open/Completed lists; shortcuts only via hover.
- Sam: dotted classic focus rectangle on rows; rail badge contrast in Light; filter Tag accessible name lacks "#"; high contrast unchecked.
- Owner at 900 px: detail overlay hides Due chips; adding from Completed view is invisible.

## Minor Observations
Heavy steel-blue sidebar selection in Light; stray splitter gripper at y~310; "0" counts are noise; 3*/2* sidebar split strands "+ New List"; Completed has no count; bordered "..." heading button.

## Questions to Consider
- What if the filter row didn't exist until asked for?
- Should the overlay at 900 px push the row's right-hand items instead of covering them?
- Is a separate Completed Smart View needed, given every view has "Completed (N)"?
