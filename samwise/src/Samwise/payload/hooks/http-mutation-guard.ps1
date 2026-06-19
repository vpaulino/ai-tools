# PreToolUse guard: prompt only when a shell command makes an EXTERNAL
# HTTP mutation (POST/PUT/DELETE/PATCH). GET-style requests fall through
# silently. Only wired to fire for curl/wget/Invoke-WebRequest/Invoke-RestMethod
# via the `if` filters in settings.json, so it adds no latency to other commands.
$ErrorActionPreference = 'SilentlyContinue'
$raw = [Console]::In.ReadToEnd()
try { $j = $raw | ConvertFrom-Json } catch { exit 0 }
$c = $j.tool_input.command
if (-not $c) { exit 0 }

# Explicit method flags (curl -X / --request, PowerShell -Method), or curl
# data/upload flags that implicitly force POST/PUT.
$pattern = '(?i)(-X\s*"?(POST|PUT|DELETE|PATCH)|-X(POST|PUT|DELETE|PATCH)|--request\s+"?(POST|PUT|DELETE|PATCH)|-Method\s+"?(Post|Put|Delete|Patch)|(^|\s)(-d|--data|--data-raw|--data-binary|--data-urlencode|-F|--form|-T|--upload-file)(\s|=|$))'

if ($c -match $pattern) {
  Write-Output '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"ask","permissionDecisionReason":"External HTTP mutation (POST/PUT/DELETE/PATCH) detected - confirm before sending."}}'
}
exit 0
