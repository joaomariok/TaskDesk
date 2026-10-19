# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

> The schema has no Windows value. TaskDesk is a native Windows 11 desktop app (WinUI 3 on .NET 10, native Fluent controls), not a web app. Follow Windows 11 / Fluent conventions, never web ones.

## Users
Primary: the owner, a senior C#/C++ Windows developer, using it as a personal to-do list on their own PC through the working day. Other Windows 11 users may come later; this is undecided, so no surface should assume them yet (no onboarding, accounts or marketing).

## Product Purpose
A small, fast, local-only to-do list for Windows 11. It lives in the system tray, starts with Windows and raises Windows notifications for Reminders. Success: capturing a Task takes seconds from anywhere (global hotkey), nothing is ever lost (every edit saved within ~0.5 s, automatic backups), and the app is never in the way.

## Positioning
Local, private and keyboard-first together. No account, cloud, sync, network calls or telemetry: everything stays in `%LOCALAPPDATA%\TaskDesk`. A global quick-add hotkey and full keyboard shortcuts make the mouse optional. Cloud to-do apps cannot honestly claim both.

## Operating Context
Runs all day in the tray, alongside other desktop work. Main window: Smart Views / Lists / Tags navigation, the Task list with quick-add, and a detail pane that saves automatically. A borderless quick-add window comes up over any app on Ctrl+Alt+Space. Reminders arrive as Windows app notifications with Mark done / Snooze buttons (tray balloons are the fallback). Data folder, export/import and backups are reached from Settings.

## Capabilities and Constraints
- Domain vocabulary is fixed in `GLOSSARY.md` (Task, List, Default List, Smart View, Tag, Subtask, Priority, Due, Overdue, Reminder, Snooze, Recurrence, Series, Occurrence, Skip). UI copy uses these words.
- Features: Lists, Tags, Priorities, Due date/time, Reminders, Subtasks (one level), Recurrence presets, search and filter chips, delete with 10 s undo, light/dark/system theme, JSON/CSV export and JSON import.
- Windows 11 x64 only, unpackaged, English UI only (no localization).
- Performance: cold start < 1.5 s with 5,000 Tasks; no visible lag when switching lists or searching (virtualized lists); `--minimized` working set target < 150 MB.
- UI uses theme resources, not hard-coded colors; custom styles are `BasedOn` the Fluent styles so Light and Dark both work. Minimum window 800×500.
- Out of scope for v1: sync, accounts, mobile, attachments, rich-text notes, encryption at rest, ARM64.
- Full spec: `docs/SPEC.md`; decisions: `docs/adr/`.

## Brand Commitments
Name: TaskDesk. App icon: an original checkbox motif (`src/TaskDesk.App/Assets/TaskDesk.ico`). No other brand assets or voice rules are set.

## Evidence on Hand
No users, testimonials, benchmarks or press beyond the owner's own measurements (startup and size results in `docs/adr/0003-ui-framework.md` and the issue #20 report). Do not invent any.

## Product Principles
1. Never in the way: fast to open, fast to capture, quiet in the tray.
2. The keyboard is enough: every action is reachable without the mouse.
3. Local and private: no network, no telemetry, the owner's data stays on the owner's PC.
4. Nothing is lost: edits save at once, deletes can be undone, backups happen on their own.
5. Feels native to Windows 11: Fluent theme, system Light/Dark, Windows notifications.

## Accessibility & Inclusion
Every control is reachable by keyboard and has `AutomationProperties.Name` (screen readers and UI Automation tests). Light and Dark themes must both read correctly.
