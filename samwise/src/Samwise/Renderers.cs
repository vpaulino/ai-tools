using System.Text;
using System.Text.Json.Nodes;

sealed record RenderContext(
    string TargetDir,
    string PayloadRoot,
    PermissionLevel PermissionLevel,
    MergeOptions Merge,
    JsonObject? OverlaySettings);

interface IRenderer
{
    string Platform { get; }
    void Render(CoreModel model, RenderContext ctx, ChangeLog log);
}

static class RendererFactory
{
    public static IReadOnlyList<IRenderer> ForPlatform(string platform) => platform switch
    {
        "claude" => new IRenderer[] { new ClaudeRenderer() },
        "codex" => new IRenderer[] { new CodexRenderer() },
        "copilot" => new IRenderer[] { new CopilotRenderer() },
        "all" => new IRenderer[] { new ClaudeRenderer(), new CodexRenderer(), new CopilotRenderer() },
        _ => throw new ArgumentException($"Unknown platform '{platform}'. Use claude, codex, copilot, or all.")
    };

    // Shared neutral instruction preamble (base of AGENTS.md / copilot-instructions.md).
    public static string BaseInstructions(string payloadRoot)
    {
        string path = Path.Combine(payloadRoot, "instructions.md");
        return File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n").Trim() : "";
    }

    public static string PermissionPosture(PermissionLevel level) => level switch
    {
        PermissionLevel.Conservative =>
            "Operating posture: conservative. Prefer read-only investigation; before editing files or running shell commands, briefly say what you intend to do. Never run destructive disk/power commands.",
        PermissionLevel.Trusted =>
            "Operating posture: trusted. You may edit files and run shell commands without asking, but never run destructive disk/power commands (format, diskpart, mkfs, dd, shutdown, etc.).",
        _ => ""
    };
}

// ---------------------------------------------------------------------------
// Claude Code: .claude/skills, .claude/hooks, .mcp.json, .claude/settings.json
// ---------------------------------------------------------------------------
sealed class ClaudeRenderer : IRenderer
{
    public string Platform => "claude";

    public void Render(CoreModel model, RenderContext ctx, ChangeLog log)
    {
        string claudeDir = Path.Combine(ctx.TargetDir, ".claude");
        var m = ctx.Merge;

        // skills -> .claude/skills/<dir>/ (verbatim copy preserves SKILL.md exactly)
        foreach (var skill in model.Skills)
            Io.CopyTree(skill.SourceDir, Path.Combine(claudeDir, "skills", skill.DirName),
                        m.Force, m.DryRun, log, $"claude:skills/{skill.DirName}");

        // hooks -> .claude/hooks/ (Claude-only feature)
        string hooksSrc = Path.Combine(ctx.PayloadRoot, "hooks");
        if (Directory.Exists(hooksSrc))
            Io.CopyTree(hooksSrc, Path.Combine(claudeDir, "hooks"), m.Force, m.DryRun, log, "claude:hooks");

        // MCP servers -> .mcp.json
        Io.MergeServersInto(model.Servers, Path.Combine(ctx.TargetDir, ".mcp.json"), "mcpServers", m, log);

        // settings profile -> .claude/settings.json, then overlay settings
        string settingsPath = Path.Combine(claudeDir, "settings.json");
        string profile = Path.Combine(ctx.PayloadRoot, ctx.PermissionLevel == PermissionLevel.Trusted
            ? "settings.trusted.fragment.json"
            : "settings.fragment.json");
        if (File.Exists(profile))
            Io.MergeSettingsObject(JsonNode.Parse(File.ReadAllText(profile))!.AsObject(), settingsPath, m, log);
        if (ctx.OverlaySettings is not null)
            Io.MergeSettingsObject(ctx.OverlaySettings, settingsPath, m, log);
    }
}

// ---------------------------------------------------------------------------
// OpenAI Codex: AGENTS.md (instructions + playbook index), .samwise/playbooks/*,
// .codex/config.toml ([mcp_servers.*]). No native hooks/permissions -> guidance.
// ---------------------------------------------------------------------------
sealed class CodexRenderer : IRenderer
{
    public string Platform => "codex";

    public void Render(CoreModel model, RenderContext ctx, ChangeLog log)
    {
        var m = ctx.Merge;

        // playbook bodies -> .samwise/playbooks/<dir>.md
        foreach (var skill in model.Skills)
        {
            string path = Path.Combine(ctx.TargetDir, ".samwise", "playbooks", skill.DirName + ".md");
            string content = $"# {skill.Name}\n\n> {skill.Description}\n\n{skill.Body}\n";
            Io.WriteText(path, content, m.Force, m.DryRun, log, $"codex:.samwise/playbooks/{skill.DirName}.md");
        }

        // AGENTS.md managed block: base instructions + on-demand playbook index + guidance
        var sb = new StringBuilder();
        string baseText = RendererFactory.BaseInstructions(ctx.PayloadRoot);
        if (baseText.Length > 0) sb.Append(baseText).Append("\n\n");
        sb.AppendLine("## Playbooks (read on demand)");
        sb.AppendLine("Skim this list. When a task matches a playbook, open its file before acting:");
        foreach (var skill in model.Skills)
        {
            sb.AppendLine($"- **{skill.Name}** — {skill.Description}");
            sb.AppendLine($"  → read `.samwise/playbooks/{skill.DirName}.md`");
        }
        sb.AppendLine();
        sb.AppendLine("## Operating guidance");
        sb.AppendLine(RendererFactory.PermissionPosture(ctx.PermissionLevel));
        sb.AppendLine("Codex has no hook system; there is no automatic session/safety hook here. Apply the posture above manually.");
        Io.UpsertManagedBlock(Path.Combine(ctx.TargetDir, "AGENTS.md"), sb.ToString(), m.DryRun, log, "codex:AGENTS.md");

        // MCP servers -> .codex/config.toml [mcp_servers.<name>]
        MergeCodexMcp(model.Servers, Path.Combine(ctx.TargetDir, ".codex", "config.toml"), m, log);
    }

    static void MergeCodexMcp(JsonObject servers, string path, MergeOptions m, ChangeLog log)
    {
        string existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var append = new StringBuilder();
        int added = 0;

        foreach (var kv in servers)
        {
            string header = $"[mcp_servers.{kv.Key}]";
            if (existing.Contains(header, StringComparison.Ordinal) && !m.Force)
            {
                log.Skipped($"config.toml#mcp_servers.{kv.Key}", "server already configured");
                continue;
            }
            append.Append(Toml.McpServerBlock(kv.Key, kv.Value!.AsObject()));
            append.Append('\n');
            added++;
            log.Created($"config.toml#mcp_servers.{kv.Key}");
        }

        if (added == 0 || m.DryRun) return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string output = existing.Length == 0 ? append.ToString() : existing.TrimEnd() + "\n\n" + append;
        File.WriteAllText(path, output, new UTF8Encoding(false));
    }
}

// ---------------------------------------------------------------------------
// GitHub Copilot: .github/copilot-instructions.md (base) + per-skill
// .github/instructions/<dir>.instructions.md (applyTo) + .vscode/mcp.json.
// No native hooks/permissions -> guidance.
// ---------------------------------------------------------------------------
sealed class CopilotRenderer : IRenderer
{
    public string Platform => "copilot";

    public void Render(CoreModel model, RenderContext ctx, ChangeLog log)
    {
        var m = ctx.Merge;

        // per-skill instruction files with applyTo scoping
        foreach (var skill in model.Skills)
        {
            string applyTo = string.IsNullOrWhiteSpace(skill.ApplyTo) ? "**" : skill.ApplyTo!;
            string path = Path.Combine(ctx.TargetDir, ".github", "instructions", skill.DirName + ".instructions.md");
            string content = $"---\napplyTo: '{applyTo}'\n---\n\n# {skill.Name}\n\n> {skill.Description}\n\n{skill.Body}\n";
            Io.WriteText(path, content, m.Force, m.DryRun, log, $"copilot:.github/instructions/{skill.DirName}.instructions.md");
        }

        // base instructions managed block
        var sb = new StringBuilder();
        string baseText = RendererFactory.BaseInstructions(ctx.PayloadRoot);
        if (baseText.Length > 0) sb.Append(baseText).Append("\n\n");
        sb.AppendLine("## Playbooks");
        sb.AppendLine("Task-specific playbooks live in `.github/instructions/*.instructions.md` and load automatically for matching files (see each file's `applyTo`).");
        sb.AppendLine();
        sb.AppendLine("## Operating guidance");
        sb.AppendLine(RendererFactory.PermissionPosture(ctx.PermissionLevel));
        sb.AppendLine("Copilot has no hook system; there is no automatic session/safety hook here. Apply the posture above manually.");
        Io.UpsertManagedBlock(Path.Combine(ctx.TargetDir, ".github", "copilot-instructions.md"),
                              sb.ToString(), m.DryRun, log, "copilot:.github/copilot-instructions.md");

        // MCP servers -> .vscode/mcp.json (VS Code uses the "servers" key)
        Io.MergeServersInto(model.Servers, Path.Combine(ctx.TargetDir, ".vscode", "mcp.json"), "servers", m, log);
    }
}

// Minimal TOML emitter for a single MCP server table (Codex config.toml).
static class Toml
{
    public static string McpServerBlock(string name, JsonObject server)
    {
        var sb = new StringBuilder();
        sb.Append("[mcp_servers.").Append(name).Append("]\n");

        string? type = server["type"]?.GetValue<string>();
        if (type == "http" && server["url"]?.GetValue<string>() is string url)
        {
            sb.Append("url = ").Append(Str(url)).Append('\n');
        }
        else
        {
            if (server["command"]?.GetValue<string>() is string command)
                sb.Append("command = ").Append(Str(command)).Append('\n');
            if (server["args"] is JsonArray args && args.Count > 0)
            {
                var parts = args.Select(a => Str(a?.GetValue<string>() ?? ""));
                sb.Append("args = [").Append(string.Join(", ", parts)).Append("]\n");
            }
            if (server["env"] is JsonObject env && env.Count > 0)
            {
                var parts = env.Select(kv => $"{kv.Key} = {Str(kv.Value?.GetValue<string>() ?? "")}");
                sb.Append("env = { ").Append(string.Join(", ", parts)).Append(" }\n");
            }
        }
        return sb.ToString();
    }

    static string Str(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
