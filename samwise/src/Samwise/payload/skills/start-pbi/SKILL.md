---
name: start-pbi
description: Begin work on a backlog item / work item / issue — pin it to the status line, create a branch, summarize it, and find likely-affected files. Use when the user says "start SA-1234", "begin work on <item>", "pick up <ticket/work item/issue>", or wants to kick off a piece of work.
---

# start-pbi

Sets up everything needed to start working a backlog item, regardless of which tracker it lives in.

## First: find the work-tracking MCP
This skill is tracker-agnostic. **Discover which work-tracking MCP server is connected** and use it:
- **Atlassian / Jira** — issues like `SA-1234`; tools such as `getJiraIssue`, `searchJiraIssuesUsingJql`, `transitionJiraIssue`.
- **Azure DevOps Boards** — numeric work item IDs; tools to get/query work items and update state.
- **GitHub Issues / Projects** — `owner/repo#123`; tools to get issues and move project cards.

List the available MCP tools if unsure, pick the one matching the item the user named, and confirm if ambiguous.
*(For Atlassian/Jira, resolve the `cloudId` at runtime via `getAccessibleAtlassianResources` rather than assuming one. For Azure DevOps, the organization comes from the connected server; for GitHub it comes from `owner/repo`.)*

## Input
- **Item key/ID** (required) — e.g. `SA-10445`, an ADO work item id, or `owner/repo#123`. If missing, ask (or offer the top of the user's open list via the tracker's query tool).

## Steps
1. **Fetch the item** from the connected tracker: summary/title, description, status/state, type, priority, and acceptance criteria if present.
2. **Pin it to the status line** — write `~/.claude/current-pbi.json` (UTF-8):
   `{"key":"<KEY-OR-ID>","title":"<summary>"}`
3. **Create / switch the git branch** (only if cwd is a git repo):
   - Match the repo's existing convention (check `git branch -a`). Default to `feature/<KEY>-<short-slug-of-summary>`.
   - If the branch exists, switch to it; otherwise create it from the current base branch. Confirm the branch name before creating.
4. **Summarize the item** for the user: 3–5 lines — goal, acceptance criteria, type/priority.
5. **Scan for affected files** — search the repo for class/entity/feature names mentioned in the title/description and list the most relevant matches as starting points.
6. **Offer** (don't auto-do) to transition the item to *In Progress* in the tracker — that's a write, so ask first.

## Notes
- Keep the summary tight; this is a launchpad, not a full analysis.
- Related: [[log-time]] logs against the item this skill pins.
