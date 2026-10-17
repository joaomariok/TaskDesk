# Implementing a spec with worktree agents

Fan a spec's tickets out to parallel implementer agents, then land each ticket on one integration branch. The orchestrator is the main session. Implementers and mergers are subagents.

## Orchestrator steps

1. Create the integration branch from `main` and note its tip commit. Every brief names this tip as `<BASE>`.
2. Read the spec issue and its tickets (see `issue-tracker.md`). Split them into tickets that can run in parallel and tickets that must wait for an earlier one.
3. Launch one implementer per ready ticket, each in its own worktree. Set `model` explicitly. Brief each with the **Implementer brief** below, plus its ticket number, `<BASE>`, and the scratchpad subfolder it owns.
4. When an implementer reports, read its diff and keep the diff uncommitted in its worktree. The report is a claim, not evidence.
5. Land the tickets one at a time, in the order they finish. For each:
   - Copy the diff onto the integration branch. When it does not apply cleanly, launch a merger with the **Merger brief** below.
   - Run the **Gate** on the integration working tree.
   - Commit once, as `<type>: <what changed>`, after the gate exits 0 and the owner approves.
6. Advance `<BASE>` to the new integration tip before launching the next wave of implementers.
7. When the last ticket is committed, run the **Gate** once more on the integration tip. Report the exit codes and test counts, and list the acceptance criteria that need a manual check.

Subagents never commit, create branches or touch another worktree. The orchestrator owns every commit and every branch.

## Gate

Run from the repo root, one command at a time:

1. `dotnet build-server shutdown`
2. `dotnet build -warnaserror`
3. `dotnet test`

Read each command's exit code. Exit 0 with zero warnings is green. Run the gate itself, not through a pipe such as `| grep`: a pipe replaces the gate's exit code with grep's.

## Implementer brief

Point each implementer at this section, and add its ticket number, `<BASE>` and scratch subfolder.

### Base check

Agent worktrees start from `main`, not from the integration branch.

1. Run `git log --oneline -1`.
2. Run `git merge --ff-only <BASE>` as a single command on its own. A chained command is blocked by the permission classifier. The fast-forward moves your temporary worktree branch to the integration tip and creates no commit.
3. Run `git log --oneline -1` again. HEAD must be `<BASE>`. If it is any other commit, or the fast-forward failed, stop and report.

### Git

Beyond that one fast-forward, leave git alone: no commit, branch, checkout, merge, rebase, stash, reset or push. Leave all changes uncommitted in your worktree.

### Context

- The parent spec is a GitHub issue. Read only the sections of `docs/SPEC.md` your ticket needs.
- Use `GLOSSARY.md` vocabulary and respect `docs/adr/`.
- The walking-skeleton commit sets the patterns to follow:
  - Core use-case interfaces, implemented in Data, tested against a real temp SQLite file through the `TempDatabase` helper.
  - Pure Core calculators with table-driven tests.
  - Thin ViewModel tests against fakes.
  - In tests, pass `TestContext.Current.CancellationToken` to every awaited call (xUnit1051 is an error).
- These test seams are already agreed with the owner. The brief is pre-approved, so skip the plan step.

### Scope

- Build only what your ticket asks for. Sibling tickets run in parallel.
- Keep edits to shared files (`TaskDeskDbContext`, `App.xaml.cs`, `MainViewModel`, `MainWindow.xaml`, `Directory.Packages.props`) minimal and additive.
- Confirm that any package or API exists in the version you pick before you use it.
- Write everything in English. Tests are never weakened, skipped or deleted.
- Tests write only to temp locations, never to `%LOCALAPPDATA%` or the registry. Do not run the WPF app.

### Migrations

A ticket that changes the schema adds exactly one migration, named `Ticket<N>_<Thing>`:

```
dotnet tool run dotnet-ef migrations add Ticket<N>_<Thing> --project src/TaskDesk.Data --output-dir Migrations
```

### Scratch files

Write temporary files only to your own subfolder of the session scratchpad, named in your brief. A shared folder caused name collisions between agents.

### Process

Invoke the Skill tool with `mattpocock-skills:tdd` and follow it. Finish with the **Gate**.

### Report

Short, with no file contents:

- gate exit codes and test counts
- files and areas changed
- acceptance criteria not verified automatically, as a manual checklist
- decisions later tickets need

## Merger brief

A merger carries one ticket's uncommitted worktree changes onto the integration branch, which is checked out in the main checkout. Point each merger at this section, and add the worktree path, the worktree's base commit, the migration name `Ticket<N>_<Thing>` and its scratch subfolder.

### Hard rules

- Leave the result as uncommitted changes in the main checkout. The orchestrator runs the gate and commits.
- Git is read-only for you: no commit, branch, checkout, merge, rebase, stash, reset, restore or push.
- Never touch the index: no `git add`, `git add -N`, `git reset`. Read state with `git diff`, `git status` and `git ls-files`.
- Do not modify the source worktree.
- Write everything in English. Tests are never weakened, skipped or deleted.

### Line endings

Many repo files are CRLF. Preserve each file's existing line endings. Worktree diffs can contain CRLF-to-LF churn: compare with `git diff --ignore-cr-at-eol` and carry over only real content changes. The final `git diff --stat` shows only real changes.

### Method

1. Inspect the ticket's changes in its worktree.
   - Tracked changes: `git -C <WT> diff --ignore-cr-at-eol <WT_BASE>`.
   - New files: `git -C <WT> ls-files --others --exclude-standard`.
2. Read what the integration tip already has: `git log --oneline` and the relevant files. Earlier tickets landed there after the worktree's base.
3. Apply the ticket's changes to the integration working tree.
   - Copy new files.
   - Port edits to existing files by hand and keep both sides of every additive conflict: constructor parameters, DI registrations, XAML panes, interface members, test fakes. When a test fake must implement an interface member from an earlier ticket, implement it minimally.
   - When two tickets' UI claim the same place, combine them (for example, stack both in the left pane) and report what you did.
4. Migrations: discard the ticket's migration files and its `ModelSnapshot` changes. After porting the model changes, regenerate one migration against the merged model, from the main checkout:

   ```
   dotnet tool run dotnet-ef migrations add Ticket<N>_<Thing> --project src/TaskDesk.Data --output-dir Migrations
   ```

   Check that the generated migration holds only this ticket's schema changes.
5. Run the **Gate** from the main checkout and fix merge mistakes until it exits 0.

### Report

Short:

- gate exit codes and test counts
- files changed
- every non-trivial conflict and how you resolved it
- anything from the ticket you could not carry over
