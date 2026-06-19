# PreToolUse guard: auto-approve READ-ONLY MCP tool calls so registered MCP
# servers don't prompt when only fetching information. Anything that isn't a
# clear read verb (create/update/delete/add/send/authenticate/...) falls
# through to the normal permission flow and still prompts.
$ErrorActionPreference = 'SilentlyContinue'
$raw = [Console]::In.ReadToEnd()
try { $j = $raw | ConvertFrom-Json } catch { exit 0 }
$name = $j.tool_name
if (-not $name) { exit 0 }

# tool name is mcp__<server>__<tool>; take the trailing tool segment.
$tool = ($name -split '__')[-1]

if ($tool -match '(?i)^(get|list|search|fetch|read|query|find|lookup|describe|view|count|retrieve|browse|show)') {
  Write-Output '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow","permissionDecisionReason":"Read-only MCP tool auto-approved."}}'
}
exit 0
