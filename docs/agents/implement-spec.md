# Implementing a spec with worktree agents

Fan a spec's tickets out to parallel implementer agents, then land each ticket on one integration branch. The orchestrator is the main session. Implementers and mergers are subagents.

## Orchestrator steps

1. Start from a clean checkout: commit pending work first, including untracked `.impeccable/critique/` snapshots, which are tracked. Create the integration branch from `main` and note its tip commit. Every brief names this tip as `<BASE>`. When the spec builds on work not yet on `main`, create it from that work's tip instead.
2. Read the spec issue and its tickets (see `issue-tracker.md`). Split them into tickets that can run in parallel and tickets that must wait for an earlier one.
3. Launch implementers for ready tickets, within the **Concurrency** limits below, each in its own worktree. Set `model` explicitly. Brief each with the **Implementer brief** below, plus its ticket number, `<BASE>`, and the scratchpad subfolder it owns.
   - If the launch fails with "core.worktree redirect", the session's working directory has become lowercase `d:`. Return to `D:\GIT\TaskDesk` and retry.
4. When an implementer reports, read its diff and keep the diff uncommitted in its worktree. The report is a claim, not evidence.
5. Land the tickets one at a time, in the order they finish. For each:
   - Run `pwsh scripts/integrate-worktree.ps1 -Worktree <WT> -Base <BASE>` from the integration checkout, which must be clean. It applies the worktree's tracked diff with `git apply --3way`, copies its untracked files, runs the **Gate** and exits with the gate's code. It prints the changed and new files; stage those paths by name. It never commits. On conflicts it still copies the untracked files and auto-resolves the `App.xaml` merged-dictionary lines. Add `-UiTests` to run `scripts/ui-tests.ps1` after a green gate.
   - Commit your own doc edits (ADR, notes) before integrating; the target must be clean.
   - Exit 3 means conflicts: it lists the conflicted files and stops. Launch a merger with the **Merger brief** below. Exit 2 means a pre-check failed (dirty target, or worktree HEAD is not `<BASE>`) and nothing was changed.
   - Commit once, as `<type>: <what changed>`, after the gate exits 0 (see "Git approvals" in `CLAUDE.md`).
   - Remove the agent's worktree and branch right away (step 6 of **Finishing**), one at a time; a bulk removal at the end outruns the 2-minute command limit.
6. Advance `<BASE>` to the new integration tip before launching the next wave of implementers.
7. When the last ticket is committed, run the **Gate** once more on the integration tip. Report the exit codes and test counts, and list the acceptance criteria that need a manual check.

## Finishing

Done means the spec is on `main`, every ticket and the spec are closed, and no agent worktree or branch is left.

1. Review the integration branch with the `code-review` skill. Name the spec issue and its tickets as context, so approvals recorded in a ticket (such as an approved test-expectation change) are not re-flagged. Ask the reviewer to grade each finding high, medium or low, and to report only findings with a reachable failure scenario.
2. Fix every high and medium finding in one implementer, then land it like a ticket. Review again, this time only the fix diff. Stop at the first round with no high or medium finding, and file the remaining lows as one issue.
3. Run the `ui-check` skill on the integration tip, with before shots from `main` (its step 4; in a port, the replaced app on `main`), covering every state the spec's diff touches and the tickets' visual criteria. Report its findings before any owner acceptance run.
4. Ask the owner, then fast-forward `main` to the integration tip and push.
5. Close each ticket, then the spec, with `gh issue close <n> --comment "<commits>, on main."`.
6. Remove each agent worktree (`git worktree remove`) and delete its `worktree-agent-*` branch (`git branch -D`); every one is integrated by now. Removing many at once takes minutes: run it in the background.

Subagents never commit, create branches or touch another worktree. The orchestrator owns every commit and every branch. A `PreToolUse` hook (`.claude/hooks/guard-git.sh`) blocks git writes from subagents, except `git merge --ff-only`.

## Concurrency

The desktop is a shared resource: the gate's in-process WinUI tests (`TaskDesk.Views.Tests`) open real windows, and the FlaUI suite takes over the keyboard and mouse.

- Run at most 2 implementers at once.
- The orchestrator runs the FlaUI suite (`scripts/ui-tests.ps1`), or the one agent its brief names for it.
- While the owner runs an acceptance session, every agent waits: no gate, test or app run.

## Gate

Run `pwsh scripts/gate.ps1`. It shuts down the build server, then runs `dotnet build -warnaserror` and `dotnet test`, and stops at the first failure. Exit 0 is green. Read the script's exit code directly; a pipe such as `| grep` replaces it with grep's.

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
- For a ticket touching WinUI views, the WinUI shell or `TaskDesk.Views.Tests`, read `docs/agents/winui.md`.
- In a port, the running app being replaced is the reference. Where the spec's description differs from it, match the running app and report the difference.

### Scope

- Build only what your ticket asks for. Sibling tickets run in parallel.
- Keep edits to shared files (`TaskDeskDbContext`, `App.xaml.cs`, `MainViewModel`, `MainWindow.xaml`, `Directory.Packages.props`) minimal and additive.
- Confirm that any package or API exists in the version you pick before you use it.
- Write everything in English. Tests are never weakened, skipped or deleted.
- Tests write only to temp locations, never to `%LOCALAPPDATA%` or the registry.
- Write the FlaUI tests your ticket needs and leave running them to the orchestrator (see **Concurrency**); list the test classes to run in your report.
- To measure startup time or working set, use `pwsh scripts/measure-startup.ps1 -Publish <publish dir>` (sandbox mode, temp data folder, no leftover processes) instead of an ad-hoc script. It is the one way an implementer runs the app. `scripts/verify-toast.ps1` is owner-run only.

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
- the red run: each new test's name and its failure message before the code existed
- files and areas changed
- every existing test you changed or deleted, with the reason and who decided it: your brief, or the ticket comment that records it. Name the owner only when a ticket records the owner's approval.
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

The repo's `.gitattributes` sets `* text=auto eol=lf`, so files are stored and checked out with LF line endings. Write LF. Worktree diffs can still contain CRLF-to-LF churn: compare with `git diff --ignore-cr-at-eol` and carry over only real content changes. The final `git diff --stat` shows only real changes.

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
