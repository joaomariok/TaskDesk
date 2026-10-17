# Coding standards

Judgement calls for review. Analyzers enforce the mechanical rules. For each rule, name the changed code that satisfies it, or report a finding.

## Settings

Every persisted setting has a reader in the running app and a test proving that changing it alters behaviour. A setting that is stored and never read is a finding.

## Task queries

Every query returning Tasks loads Tags, Subtasks and Recurrence through one shared path. A new query reuses that path; a hand-written `Include` chain is a finding.

## Destructive paths

Delete, import and replace each either restore everything on undo (Tags, Subtasks, Recurrence, Reminders, ordering) or are guarded by a backup taken first. Check what the undo does not bring back.

## External side effects

Registry writes, files outside the data folder and OS registration sit behind an interface, and tests use a fake. A direct call to the real thing from app logic is a finding.
