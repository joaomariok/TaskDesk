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

## Test isolation

Gate tests (xUnit and the in-process MSTest app) use per-test state only: temp folders and fakes. Machine-global state (clipboard, registry, real global hotkeys, the foreground window) belongs to the opt-in FlaUI suite.

## Test tool limits

Product code serves the product. When a test tool cannot drive something, fix the test tool; a product-code workaround for it is a finding.

## View state transitions

A view test covers a transition, not only the first render: the bound item changes from A to B, a value goes from set to empty or null, an error is set and then cleared, a recycled row shows a different item. A changed view whose tests render it only once is a finding.

## Changed tests

A changed existing test asserts at least as much as before; a removed assertion is replaced by one that implies it. A changed test that checks less is a finding, even when the change was approved.

## Visual changes

XAML and visual changes are checked against the named rules and Do's and Don'ts in `DESIGN.md`. Name the rule the change follows, or report a finding.
