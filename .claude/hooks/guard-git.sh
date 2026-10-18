#!/usr/bin/env bash
# PreToolUse hook (Bash and PowerShell): subagents leave git state alone (docs/agents/implement-spec.md).
# Only subagent calls carry agent_id; the main session passes through untouched.
# The one write a brief allows, git merge --ff-only <BASE>, passes.
input=$(cat)
command -v jq >/dev/null 2>&1 || { echo 'guard-git: jq not found; git guard skipped' >&2; exit 0; }

agent_id=$(jq -r '.agent_id // empty' <<<"$input")
[ -z "$agent_id" ] && exit 0
cmd=$(jq -r '.tool_input.command // empty' <<<"$input")

# git, any global options (-C <path>, -c <key=value>, --no-pager), then the subcommand.
pattern='(^|[^[:alnum:]_.-])git(\.exe)?([[:space:]]+(-[Cc][[:space:]]+[^[:space:]]+|--[^[:space:]]+))*[[:space:]]+((add|checkout|switch|restore|reset|stash|commit|rebase|push|revert|clean|merge|cherry-pick|am)([^[:alnum:]-]|$)|branch[[:space:]]+-[dDmMcCf]([^[:alnum:]-]|$))'
while IFS= read -r line; do
    if [[ "$line" =~ $pattern ]]; then
        match=${BASH_REMATCH[0]}
        if [[ "$match" =~ merge ]] && [[ "$line" =~ merge[[:space:]]+--ff-only ]]; then
            continue
        fi
        echo "Blocked: subagents leave git alone (no add, checkout, switch, restore, reset, stash, commit, rebase, push, revert, clean, merge or branch changes). The only allowed write is git merge --ff-only <BASE>. Report what you needed and the orchestrator decides." >&2
        exit 2
    fi
done < <(sed -e 's/&&/\n/g' -e 's/||/\n/g' -e 's/;/\n/g' -e 's/|/\n/g' <<<"$cmd")
exit 0
