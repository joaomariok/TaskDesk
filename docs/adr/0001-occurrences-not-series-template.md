# Occurrences, not a Series template

A recurring Task is modelled as a chain of independent Occurrences: completing (or Skipping) the current Occurrence creates the next one as a new Task that copies it, at most once, and the Occurrences share only a `SeriesId`. There is no Series entity holding a master copy, so editing one Occurrence never changes past or other Occurrences, and completed Occurrences remain as ordinary completed Tasks in history.

## Considered Options

- **Series template entity** (a master Task that Occurrences are projected from, with "edit this / edit all" semantics): rejected because it doubles the data model, needs exception handling for edited Occurrences, and complicates import/export, for a single-user app where "edit the open Occurrence; the next one copies it" covers real use.

## Consequences

- Deleting the open Occurrence would silently end the Series, so delete on an Occurrence asks whether to Skip it or stop repeating.
- Un-completing and re-completing an Occurrence must not create a second next Occurrence; each Occurrence records the one it produced.
- Recurrence fields that must not drift between copies (e.g. a yearly Feb 29) are stored explicitly in the Recurrence, not inferred from the previous Due.
