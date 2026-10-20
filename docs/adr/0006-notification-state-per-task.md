# Notification state belongs to the Task, not to each Reminder

**Status: Accepted (2026-10-20). Spec #114.**

A Task can have several Reminders (fixed, or some time before Due), a Due notification and a Nag. The state of its notifications still lives on the Task: one Snooze and one "notified through" instant, the latest moment the Task has notified for. Reminder rows hold only their planned moment. A Task is due to notify when it is open, not snoozed past now, and some moment lies after "notified through" and at or before now. Claiming it advances "notified through" to the latest such moment.

The owner wanted four behaviours: one toast per Task, a Snooze that silences the whole Task, missed moments that collapse into one notification, and moments already past when they are set never firing. One instant per Task gives all four with one column and the same single conditional update the claim uses today.

## Considered Options

- **State per source** (a fired marker and a Snooze on each Reminder, plus one each for the Due notification and the Nag): rejected. Reminder A snoozed to 9:10 and Reminder B at 9:05 would give two toasts for one Task, a Nag has no row to keep a marker on, and every edit would need its own re-arm rule per source.

## Consequences

- Every change to what makes a Task notify (adding, editing or removing a Reminder; changing the Due, "Notify at Due" or the Nag; un-completing; creating the Task, including the next Occurrence, quick add and import) moves "notified through" to at least now. That is how a moment set in the past never fires. Edits to other fields must leave it alone, or they would swallow a missed moment.
- A moment at or before "notified through" never fires again. A Reminder is re-armed only by moving it, or the Due, to a later moment.
- Clear Snooze can't re-arm a single Reminder; it makes the Task notify once at the next check by acting on the Task's instant.
- Moments are computed when the rule runs (the default Reminder time and the time zone are read then), not stored. The SQL query can only narrow the candidates; the rule itself runs in memory over them.
