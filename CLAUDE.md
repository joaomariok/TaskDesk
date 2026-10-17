# TaskDesk

## Agent skills

### Issue tracker

Issues live in GitHub Issues on joaomariok/TaskDesk (via `gh`). See `docs/agents/issue-tracker.md`.

### Triage labels

Default canonical labels: needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one root `GLOSSARY.md` plus `docs/adr/`. See `docs/agents/domain.md`.

### Implementing a spec

Running `/implement-spec`, or fanning tickets out to worktree agents: follow `docs/agents/implement-spec.md`.

## Verification

Gate: `pwsh scripts/gate.ps1` (must exit 0).

Review: `CODING_STANDARDS.md`

## Git approvals

Overrides the global commit-approval rule for this repo. Commit on a working branch as soon as the gate exits 0, without asking. Merging to `main` and pushing each need the owner's approval.
