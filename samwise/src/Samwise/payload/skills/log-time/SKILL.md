---
name: log-time
description: Record time spent against the current work item or a specified one, in whatever tracker is connected. Use when the user says "log time", "log 2h", "record worklog", "add time to SA-1234", or wants to track work against a ticket / work item / issue.
---

# log-time

Records time spent on a work item via whichever work-tracking MCP is connected.

## First: find the work-tracking MCP
Time tracking differs by tracker — **discover which MCP is connected** and map accordingly:
- **Atlassian / Jira** — add a worklog via `addWorklogToJiraIssue` (native worklog with `timeSpent`).
- **Azure DevOps Boards** — there's no worklog entity; update the work item's **Completed Work** (and optionally **Remaining Work**) hours fields instead.
- **GitHub Issues** — has no native time tracking; if that's the only tracker, tell the user and offer to add a comment noting the time instead of failing silently.

If unsure which is connected, list the available MCP tools and pick the matching one.

## Inputs
- **Time spent** (required) — e.g. `30m`, `1h`, `2h 30m`, `1d`. Parse from phrasing ("two and a half hours" → `2h 30m`). For ADO, convert to hours (`2h 30m` → `2.5`).
- **Item key/ID** (optional) — if not given, use the current item from `~/.claude/current-pbi.json` (`key` field). If that's missing too, ask.
- **Comment** (optional) — a short note of what was done. If absent, offer to summarize from this session's work in one line.
- **Start time** (optional) — default to now if the tracker supports it.

## Steps
1. Resolve the item key/ID (arg → `~/.claude/current-pbi.json` → ask).
2. Confirm in one line: `Log <time> to <KEY> — "<comment>"?` — this is a write, so confirm before sending.
3. Call the appropriate MCP tool for the connected tracker (see mapping above).
   - For Jira/Atlassian, resolve the `cloudId` at runtime via `getAccessibleAtlassianResources` rather than assuming one.
4. Report compactly: `✓ Logged <time> to <KEY>` and the new total if returned.

## Notes
- Never guess the time — if it's ambiguous, ask.
- Related: [[start-pbi]] sets the current item this skill reads.
