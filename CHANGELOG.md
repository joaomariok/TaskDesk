# Changelog

All notable changes to TaskDesk are written here, one section per release, newest first. The format follows [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). A release needs its section before the tag is pushed: the release workflow fails without it, and the section becomes the GitHub release notes.

## [0.1.0] - 2026-10-23

The first release.

### Added

- Tasks in Lists, ordered by hand. The Default List always exists and receives every Task added outside a List. Drag a List in the sidebar to reorder it.
- Smart Views across all Lists: Today, Upcoming, Overdue, All and Completed.
- Tags on any Task, and a Filter by Priority, Due, Reminder or Tags that stays on across views until it is cleared.
- Subtasks as a one-level checklist inside a Task, and a Priority of None, Low, Medium or High.
- Due as a date, with or without a time.
- Reminders at a fixed moment or an amount of time before Due, the Due notification, Nag for an incomplete Task and Snooze, delivered as Windows notifications with Mark done and Snooze buttons.
- Recurrence for repeating Tasks.
- Quick add from a global hotkey, and the tray: closing the window keeps Reminders running, and the tray icon opens or exits TaskDesk.
- Start with Windows, on by default and switchable in Settings.
- Backups of your data, and Export and Import to move it to another PC.
- A `Setup.exe` installer that installs for the current user and appears in Apps & features, and self-update: TaskDesk downloads a new release in the background and installs it at the next start or with Restart to update.
