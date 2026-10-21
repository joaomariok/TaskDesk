# Manual test checklist

What automated tests cannot cover (SPEC 11.3), plus checks per feature. Items marked **(automated)** are covered by the opt-in UI suite (`pwsh scripts/ui-tests.ps1`); check them by hand only when that suite cannot run. Run against a published build (`dotnet publish src/TaskDesk.App -p:PublishProfile=win-x64`). To keep your real data safe, set `TASKDESK_DATA_DIR` to a scratch folder first. That also turns on sandbox mode: the Run key is never read or written and reminders use tray balloons. Checks of rich toasts need notification registration, so also set `TASKDESK_SANDBOX=0` for them; this still never touches the Run key. Checks of auto-start need a normal run (no `TASKDESK_DATA_DIR`) of a published build, which uses your real data folder, so back it up first. Turning "Start with Windows" off removes the Run entry only when it points at the running exe. The log shows the active mode at start-up.

Mark each item with the date and result.

## Notifications (Reminders)

- [ ] Set a Reminder two minutes ahead on a Task. A Windows toast appears with the Task title, due text and list name, and the TaskDesk icon and name.
- [ ] Toast buttons are shown: Mark done, Snooze 10 min, Snooze 1 h.
- [ ] App running: click Mark done. The Task completes. Repeat with Snooze 10 min: no new toast until about 10 minutes later; the planned Reminder time is unchanged in the detail pane.
- [ ] App running: click the toast body. The main window opens with that Task selected.
- [ ] App not running (Exit from the tray first, with a Reminder armed or after creating one from another start): click Mark done or a Snooze button on the toast. The app starts hidden, applies the action and no window appears. Click the body: the window opens on that Task.
- [ ] Sleep the PC with several Reminders due, then wake it. Up to three due Reminders show individual toasts; more than three show one "N reminders missed" summary that opens the Today view.
- [ ] Turn on Do Not Disturb: no popup, and the Reminder is not lost.
- [ ] Due notification, timed: give a Task with no Reminder a Due two minutes ahead (today, with a Due time). At the Due time (within 30 s) a toast appears with the Task title and "Due … · List", and nothing more for that Due. Untick "Notify at Due" under the Due time on another such Task: no toast at its Due.
- [ ] Due notification, date-only: set "Default reminder time" in Settings two minutes ahead, then give a Task with no Reminder today's date as its Due (no time). At that time a toast appears for it.
- [ ] A Task already Overdue that notifies (for example, a Reminder set two minutes ahead on a Task due yesterday): the toast body begins with "Overdue · ". The tray balloon fallback shows the same body.
- [ ] Nag: give a Task a Due two minutes ahead (today, with a Due time) and pick "Every 15 min" under Due. The Due notification shows at the Due, then 15 minutes later a toast for the same Task whose body begins with "Overdue · ", then another every 15 minutes. Pick "Off": no more toasts. Complete it while it nags: no more toasts.
- [ ] A Nag missed during sleep: with an "Every 15 min" Nag running, sleep the PC for about an hour, then wake it. One toast shows for the Task (not one per missed Nag), and the next comes at the next 15-minute step counted from the Due.
- [ ] A Snooze absorbs a Nag: on a Task with an "Every 15 min" Nag, click "Snooze 1 h" on its toast. No toast for that Task for an hour, one toast when the Snooze ends, then the Nags resume at their next 15-minute step counted from the Due.
- [ ] Upgrade from a build without the Due notification, with Tasks whose Due has passed: no burst of Overdue toasts at the first start; a Reminder that was armed and already past still shows once.
- [ ] Force the fallback (see ADR 0002 item d, on a machine without the runtime): a banner explains it and a tray balloon is used instead.
- [ ] Checking the ADR 0002 pending items on a clean machine: toast appears, buttons work, app launches from a closed state, icon and name are right. Record the results in the ADR.

## Tray and window

- [ ] The tray icon is present while running. Left click shows and hides the window.
- [ ] Right-click menu has Open TaskDesk, Quick add, Settings, Exit, and each works.
- [ ] Close button hides to the tray (default). The first time only, a one-time hint says TaskDesk is still running in the tray.
- [ ] Settings, close behavior = exit: the close button exits the process.
- [ ] Window size, position and pane widths are restored after restart; minimum size is 800x500. **(automated: pane widths)**
- [ ] Exit leaves no TaskDesk process behind and unregisters the hotkey.

## Start and single instance

- [ ] Settings, Start with Windows on: the Run key value `TaskDesk` points to the exe with `--minimized`. Off: the value is removed.
- [ ] Log off and on (or restart): TaskDesk starts hidden in the tray, with no duplicate instance.
- [ ] Move the published folder, start it once: the Run key path is updated.
- [ ] `TaskDesk.App.exe --minimized` starts with no window; the tray icon is there.
- [ ] Starting a second instance focuses the first one's window (also when it is hidden in the tray) and the second process exits.

## Quick add and hotkey

- [ ] `Ctrl+Alt+Space` from another app opens the quick-add window centered on the monitor with the mouse. Enter saves, Esc closes, clicking away closes without saving.
- [ ] It works while the main window is open, minimized and hidden in the tray.
- [ ] The Task lands in the chosen list and shows in the main window and the list counts without a restart.
- [ ] Conflict: pick a combination another app owns. Settings shows that the shortcut is used by another application and the previous hotkey keeps working.
- [ ] Disable the hotkey in Settings: the combination no longer opens quick add.

## Theme

- [ ] Settings theme System / Light / Dark applies immediately, with no restart, in the main, Settings and quick-add windows.
- [ ] With System selected, changing the Windows app mode is followed while the app runs.
- [ ] No unreadable text or hard-coded colors in either theme.

## Lists, Tags and Tasks

- [ ] Drag a Task to reorder it in a List; the order survives restart. Reordering is off in Smart Views, Tag filters and search.
- [ ] Drag a Task above and below a row by half: the accent insertion line shows the gap it will land in (and hides on drop or when the drag leaves the list); then drop it on its own gap (just above or below itself) and nothing changes.
- [ ] Drag Lists in the navigation pane to reorder; rename, color and delete from the right-click menu.
- [ ] Creating a List (new list box, Enter) and tagging a Task (new tag box, Enter). **(automated)**
- [ ] Click a Tag in the navigation pane to filter; click again to clear.
- [ ] Space toggles complete, Delete removes the Task, Ctrl+Z and the Undo bar restore it (within 10 seconds) **(automated, except the recurring case)**; undoing a recurring Task's completion also removes the Occurrence it produced.
- [ ] Edits in the detail pane save on their own (text after about 0.5 s); kill the process right after typing and restart: the edit is there. **(automated: edit then close and restart; killing the process is still manual)**
- [ ] Subtasks: add, tick, delete, undo; the "done/total" progress shows on the row.
- [ ] Subtasks in the Task list: a Task with Subtasks shows a right-pointing chevron, and a Task without leaves the column blank. A click on the chevron opens and closes the Task without selecting the row or opening the pane; Right and Left on the focused row do the same. Open a Task, switch to another List, a Smart View and a Tag page and come back: it is still open. Complete the open Task: its whole block stays for the completion hold, then moves into "Completed (N)" together, dimmed once; Ctrl+Z brings it back whole.
- [ ] Due dates: Today, Upcoming, Overdue, All, Completed views contain the right Tasks, including a Task due today at a time that has just passed.
- [ ] Recurrence: complete a repeating Task and the next Occurrence appears; Skip works; deleting an Occurrence offers "Delete this occurrence" and "Stop repeating".
- [ ] Search and filters (priority, Tags, has due, has reminder) work in the current view, and Esc or clearing restores it.
- [ ] Search for a Subtask's title (for example in capitals): its Task opens with all its Subtasks; press Esc and it closes again. A Task you closed during the search stays closed.

## Keyboard-only

- [ ] Ctrl+N, Ctrl+F, Ctrl+Z, F2, Space, Delete, Esc and Ctrl+1 to Ctrl+5 do what SPEC 6.4 says. **(automated, except Ctrl+N; Esc only for clearing search)**
- [ ] Start-up puts keyboard focus in quick add; Enter adds the Task. **(automated)**
- [ ] Tab reaches every control; nothing needs the mouse; a screen reader (Narrator) announces names for the main controls.

## Data safety, backup, export and import

- [ ] Settings, Back up now, creates a file in `backups\` that opens as a database; only the newest 10 are kept.
- [ ] Start after a schema change: a backup is made before migrating. With a deliberately corrupted `taskdesk.db`, an error dialog shows the log and backups folders and nothing is deleted.
- [ ] Export JSON and CSV; open the CSV in Excel: accents are correct (UTF-8 with BOM).
- [ ] Import the JSON in Merge mode and in Replace mode; a malformed file is rejected with no change.

## Performance (5,000 Tasks)

Generate data with `dotnet run --project tools/TaskDesk.SampleData -- "<empty folder>" 5000` and run with `TASKDESK_DATA_DIR` set to it.

- [ ] Start to a visible window takes under 1.5 s.
- [ ] `--minimized` working set in Task Manager (Details, Working set) is under 150 MB (a target, not a hard failure).
- [ ] Scrolling the Inbox with the mouse wheel and by dragging the scrollbar is smooth.
- [ ] Switching Smart Views (Ctrl+1 to Ctrl+5) and typing in the search box show no visible lag.
- [ ] Memory does not keep growing after scrolling the whole list.
