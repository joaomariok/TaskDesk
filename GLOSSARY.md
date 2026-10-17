# TaskDesk

A local-only, single-user to-do app for Windows. This glossary fixes the words used for the things a person plans, schedules and completes in it.

## Language

### Organizing

**Task**:
One thing the owner intends to do, with a title, optional notes, and optional Due, Reminder, Priority, Tags and Subtasks.
_Avoid_: To-do, item, TaskItem (code name only)

**List**:
A named, owner-ordered container that every Task belongs to exactly one of.
_Avoid_: Project, folder, category

**Default List**:
The one List that always exists, cannot be deleted and receives Tasks added outside a List; it starts out named "Inbox" but can be renamed.
_Avoid_: Inbox (as a concept)

**Smart View**:
A built-in, read-only view of Tasks across all Lists selected by Due and completion: Today, Upcoming, Overdue, All, Completed.
_Avoid_: Filter, saved search, list

**Filter**:
A temporary narrowing of the current view by Priority, Due present, Reminder present or Tags; it is never saved and stays on across views until cleared. Search is not a Filter.
_Avoid_: Smart View, saved search, query

**Tag**:
A short label, shown with a leading `#`, attached to any number of Tasks across Lists.
_Avoid_: Label, category

**Subtask**:
A one-level checklist step inside a Task; ticking Subtasks and completing their Task are independent of each other.
_Avoid_: Checklist item, child task

**Priority**:
The owner's importance rating of a Task: None, Low, Medium or High.
_Avoid_: Importance, urgency

### Time

**Due**:
The local calendar date, optionally with a time, by which a Task should be done; the only date Smart Views look at.
_Avoid_: Deadline, scheduled date

**Overdue**:
The state of an incomplete Task whose Due has passed: a timed Due once its time passes, a date-only Due from the start of the following day.
_Avoid_: Late, past due

**Reminder**:
The planned instant at which the owner is notified about a Task, independent of Due; it never places a Task in a Smart View.
_Avoid_: Alarm, notification, alert

**Snooze**:
A temporary postponement of a fired Reminder that leaves the planned Reminder unchanged.
_Avoid_: Delay, reschedule

### Recurrence

**Recurrence**:
The repeat rule on a Task: frequency, interval and the days or dates it lands on.
_Avoid_: Repeat pattern, schedule, RRULE

**Series**:
All the Tasks produced by one Recurrence over time, linked as the same recurring commitment.
_Avoid_: Recurring task (for the whole), template

**Occurrence**:
One Task within a Series, edited, moved and deleted on its own; completing or Skipping it produces the next Occurrence at most once.
_Avoid_: Instance, copy, repetition

**Anchor**:
Which date the next Occurrence is counted from: the current Occurrence's Due, or the date it was completed or Skipped.
_Avoid_: Base, reference date

**Skip**:
Deleting the current Occurrence while keeping the Series going, so the next Occurrence is still produced.
_Avoid_: Dismiss
