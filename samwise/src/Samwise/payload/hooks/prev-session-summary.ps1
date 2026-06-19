# SessionStart hook. Injects two pieces of additional context:
#   1) A recap of the previous Claude Code session in this folder.
#   2) A work-item overview routine that is currently enabled only when a
#      Jira/Atlassian MCP server is connected. Other trackers should be skipped
#      silently until support is added.
#
# The hook itself cannot call MCP, so tracker detection and reads are performed
# by the agent on the first turn. The hook fails silently and never blocks
# startup.

$ErrorActionPreference = 'Stop'
try {
    $raw = [Console]::In.ReadToEnd()
    if (-not $raw) { exit 0 }
    $payload = $raw | ConvertFrom-Json

    $current = $payload.transcript_path
    if ($current -and (Test-Path (Split-Path $current -Parent))) {
        $projDir = Split-Path $current -Parent
    } else {
        $enc = ($payload.cwd -replace '[:\\/]', '-')
        $projDir = Join-Path "$env:USERPROFILE\.claude\projects" $enc
    }

    $sb = New-Object System.Text.StringBuilder

    $prev = $null
    if (Test-Path $projDir) {
        $curId = $payload.session_id
        $prev = Get-ChildItem "$projDir\*.jsonl" -ErrorAction SilentlyContinue |
            Where-Object { $_.BaseName -ne $curId } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1
    }

    if ($prev) {
        $lines = Get-Content $prev.FullName -Encoding UTF8
        $title = $null
        $branch = $null
        $prompts = New-Object System.Collections.Generic.List[string]
        $lastAsst = $null

        foreach ($l in $lines) {
            if (-not $l.Trim()) { continue }
            try { $o = $l | ConvertFrom-Json } catch { continue }

            switch ($o.type) {
                'ai-title' {
                    if ($o.aiTitle) { $title = $o.aiTitle }
                }
                'user' {
                    if ($o.gitBranch) { $branch = $o.gitBranch }
                    $c = $o.message.content
                    if ($c -is [string] -and $c.Trim() -and $c -notmatch '^\s*<') {
                        $t = $c.Trim()
                        if ($t.Length -gt 200) { $t = $t.Substring(0, 200) + '...' }
                        $prompts.Add($t)
                    }
                }
                'assistant' {
                    $c = $o.message.content
                    if ($c -is [array]) {
                        $txt = ($c | Where-Object { $_.type -eq 'text' } | ForEach-Object { $_.text }) -join ' '
                        if ($txt.Trim()) { $lastAsst = $txt.Trim() }
                    } elseif ($c -is [string] -and $c.Trim()) {
                        $lastAsst = $c.Trim()
                    }
                }
            }
        }

        $age = (Get-Date) - $prev.LastWriteTime
        if ($age.TotalDays -ge 1) { $when = '{0:N0} day(s) ago' -f $age.TotalDays }
        elseif ($age.TotalHours -ge 1) { $when = '{0:N0} hour(s) ago' -f $age.TotalHours }
        else { $when = '{0:N0} minute(s) ago' -f $age.TotalMinutes }

        [void]$sb.AppendLine("Summary of the previous Claude Code session in this folder (last active $when):")
        if ($title)  { [void]$sb.AppendLine("- Title: $title") }
        if ($branch) { [void]$sb.AppendLine("- Git branch: $branch") }
        if ($prompts.Count -gt 0) {
            [void]$sb.AppendLine("- User requests:")
            $recent = if ($prompts.Count -gt 6) { $prompts[($prompts.Count - 6)..($prompts.Count - 1)] } else { $prompts }
            foreach ($p in $recent) { [void]$sb.AppendLine("    - $p") }
        }
        if ($lastAsst) {
            $snip = if ($lastAsst.Length -gt 500) { $lastAsst.Substring(0, 500) + '...' } else { $lastAsst }
            [void]$sb.AppendLine("- Where it left off: $snip")
        }
        [void]$sb.AppendLine("(Tip: run ``claude --continue`` to resume that session with full history.)")
        [void]$sb.AppendLine("")
    }

    $sysMsg = $sb.ToString()

    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("=== WORK ITEM SESSION-START ROUTINE (first assistant reply only) ===")
    [void]$sb.AppendLine("This routine is currently enabled only for Atlassian/Jira. Keep the wording tracker-neutral so future providers can be added under the same pattern.")
    [void]$sb.AppendLine("1. Detect whether an Atlassian/Jira MCP server is connected. Resolve cloudId at runtime with getAccessibleAtlassianResources; do not assume one.")
    [void]$sb.AppendLine("2. If Jira is connected, query my open assigned work for the active sprint using a compact JQL shape such as:")
    [void]$sb.AppendLine("   assignee = currentUser() AND sprint in openSprints() AND statusCategory != Done ORDER BY updated DESC")
    [void]$sb.AppendLine("   Request only compact fields: summary, status, issuetype, priority, updated, and sprint if available.")
    [void]$sb.AppendLine("3. Show a short 'Work item overview': active sprint name and dates if available, then a compact table of open items (key, summary, status).")
    [void]$sb.AppendLine("4. Take the first item (most recently updated) and write this user-local status file:")
    [void]$sb.AppendLine("   Path: $env:USERPROFILE\.claude\current-pbi.json")
    [void]$sb.AppendLine('   Content (UTF-8): {"key":"<ITEM-KEY>","title":"<summary>"}')
    [void]$sb.AppendLine("5. Then address the user's actual request.")
    [void]$sb.AppendLine("If Jira is not connected, or if only another tracker is connected, skip this routine silently and answer the user normally.")

    $out = @{ hookSpecificOutput = @{ hookEventName = 'SessionStart'; additionalContext = $sb.ToString() } }
    if ($sysMsg -and $sysMsg.Trim()) { $out.systemMessage = $sysMsg }
    $out | ConvertTo-Json -Depth 4 -Compress
} catch {
    exit 0
}
