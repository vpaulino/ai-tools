using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;

// ---------------------------------------------------------------------------
// samwise - the loyal tool that helps a developer carry their work.
// Bootstraps a repository's AI-assistant setup from a vendor-neutral core model,
// rendered per platform: Claude Code, OpenAI Codex, or GitHub Copilot.
//
//   samwise init [--platform claude|codex|copilot|all] [--permission-level conservative|trusted]
//   samwise list
//
// Idempotent: existing files/keys are preserved unless --force is passed.
// ---------------------------------------------------------------------------

var opts = ParseArgs(args);

string payloadRoot = Path.Combine(AppContext.BaseDirectory, "payload");
if (!Directory.Exists(payloadRoot))
{
    Console.Error.WriteLine($"FATAL: payload not found next to the tool ({payloadRoot}).");
    return 2;
}

if (opts.Help)
{
    PrintUsage();
    return 0;
}

switch (opts.Command)
{
    case "init":
        return RunInit(payloadRoot, opts);
    case "list":
        return RunList(payloadRoot);
    case "audit":
        return RunAudit(payloadRoot, opts);
    default:
        PrintUsage();
        return opts.Command is null ? 0 : 1;
}

static int RunInit(string payloadRoot, Options o)
{
    string target = Path.GetFullPath(o.Target);
    IReadOnlyList<IRenderer> renderers;
    try { renderers = RendererFactory.ForPlatform(o.Platform); }
    catch (ArgumentException ex) { Console.Error.WriteLine($"FATAL: {ex.Message}"); return 1; }

    Console.WriteLine($"Samwise: bootstrapping into {target}");
    Console.WriteLine($"Platform(s): {string.Join(", ", renderers.Select(r => r.Platform))}");
    Console.WriteLine($"Permission level: {FormatPermissionLevel(o.PermissionLevel)}");
    if (o.DryRun) Console.WriteLine("(dry-run - no files will be written)");

    var merge = new MergeOptions(o.Force, o.DryRun, o.NoInput);
    var log = new ChangeLog();

    // ---- build the vendor-neutral core model ----
    var skills = SkillLoader.LoadFrom(Path.Combine(payloadRoot, "skills")).ToList();

    string mcpSrc = Path.Combine(payloadRoot, "mcp.servers.json");
    var servers = JsonNode.Parse(File.ReadAllText(mcpSrc))!["mcpServers"]!.AsObject();

    string? adoOrg = ResolveAdoOrg(o.AdoOrg, o.NoInput);
    string? sqlUrl = ResolveSqlMcpUrl(o.SqlMcpUrl, o.NoInput);
    if (!string.IsNullOrWhiteSpace(adoOrg)) servers["azure-devops"] = BuildAdoServer(adoOrg);
    if (!string.IsNullOrWhiteSpace(sqlUrl)) servers["sql"] = BuildHttpServer(sqlUrl);

    JsonObject? overlaySettings = ApplyOverlayToModel(o.OverlayPath, servers, skills);

    var model = new CoreModel { PayloadRoot = payloadRoot, Skills = skills, Servers = servers };
    var ctx = new RenderContext(target, payloadRoot, o.PermissionLevel, merge, overlaySettings);

    // ---- render for each selected platform ----
    foreach (var renderer in renderers)
    {
        Console.WriteLine($"\n-- rendering: {renderer.Platform} --");
        renderer.Render(model, ctx, log);
    }

    Console.WriteLine();
    log.Summarize();
    Console.WriteLine(o.DryRun
        ? "\nDry run complete. Re-run without --dry-run to apply."
        : "\nDone. Restart your AI assistant in this repo to pick up the new setup.");
    return 0;
}

// Fold an optional overlay file into the core model. Returns Claude-only
// overlay settings (permissions/hooks) to merge during the Claude render.
static JsonObject? ApplyOverlayToModel(string? overlayPath, JsonObject servers, List<Skill> skills)
{
    if (overlayPath is null) return null;
    overlayPath = Path.GetFullPath(overlayPath);
    if (!File.Exists(overlayPath))
    {
        Console.Error.WriteLine($"WARNING: overlay file not found: {overlayPath} - skipping.");
        return null;
    }

    Console.WriteLine($"Applying overlay: {overlayPath}");
    var overlay = Io.LoadObject(overlayPath);

    if (overlay["mcpServers"] is JsonObject ovServers)
        foreach (var kv in ovServers)
            servers[kv.Key] = kv.Value!.DeepClone();

    if (overlay["skillsDir"] is JsonValue sd && sd.GetValue<string>() is string skillsDir)
    {
        string abs = Path.IsPathRooted(skillsDir)
            ? skillsDir
            : Path.Combine(Path.GetDirectoryName(overlayPath)!, skillsDir);
        if (Directory.Exists(abs))
            skills.AddRange(SkillLoader.LoadFrom(abs));
        else
            Console.Error.WriteLine($"WARNING: overlay skillsDir not found: {abs} - skipping.");
    }

    return overlay["settings"] as JsonObject; // Claude-only (permissions/hooks)
}

static int RunList(string payloadRoot)
{
    Console.WriteLine("Samwise installs the following neutral core (rendered per --platform):\n");

    string skillsRoot = Path.Combine(payloadRoot, "skills");
    if (Directory.Exists(skillsRoot))
    {
        Console.WriteLine("Skills / playbooks:");
        foreach (var d in Directory.GetDirectories(skillsRoot).OrderBy(x => x))
            Console.WriteLine($"  - {Path.GetFileName(d)}");
    }

    string mcp = Path.Combine(payloadRoot, "mcp.servers.json");
    if (File.Exists(mcp))
    {
        Console.WriteLine("\nMCP servers:");
        var node = JsonNode.Parse(File.ReadAllText(mcp))?["mcpServers"]?.AsObject();
        if (node is not null)
            foreach (var kv in node)
                Console.WriteLine($"  - {kv.Key} ({kv.Value?["type"]?.GetValue<string>() ?? "?"})");
        Console.WriteLine("  - azure-devops (added at init via --ado-org / prompt)");
        Console.WriteLine("  - sql (added at init via --sql-mcp-url / prompt)");
    }

    Console.WriteLine("\nPlatforms (--platform):");
    Console.WriteLine("  - claude  : .claude/skills, .claude/hooks, .mcp.json, .claude/settings.json");
    Console.WriteLine("  - codex   : AGENTS.md + .samwise/playbooks/*, .codex/config.toml");
    Console.WriteLine("  - copilot : .github/copilot-instructions.md + .github/instructions/*, .vscode/mcp.json");
    Console.WriteLine("  - all     : render every platform");

    Console.WriteLine("\nPermission profiles: conservative (default), trusted.");
    Console.WriteLine("Org-specific extras can be layered with --overlay <file.json>.");
    return 0;
}

static int RunAudit(string payloadRoot, Options o)
{
    if (string.IsNullOrWhiteSpace(o.Audit.PluginName))
    {
        Console.Error.WriteLine("FATAL: missing plugin name. Usage: samwise audit <plugin-name> [options]");
        return 1;
    }

    string target = Path.GetFullPath(o.Target);
    string pluginName = o.Audit.PluginName.Trim();
    if (!TryResolvePluginDir(target, o.Audit.PluginsDir, pluginName, out string pluginDir, out string resolveError))
    {
        Console.Error.WriteLine($"FATAL: {resolveError}");
        return 1;
    }

    var markdownFiles = Directory.GetFiles(pluginDir, "*.md", SearchOption.AllDirectories)
        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (markdownFiles.Count == 0)
    {
        Console.Error.WriteLine($"FATAL: plugin '{pluginName}' has no markdown files to audit ({pluginDir}).");
        return 1;
    }

    var merge = new MergeOptions(o.Force, o.DryRun, o.NoInput);
    var log = new ChangeLog();

    string auditSkillSrc = Path.Combine(payloadRoot, "skills", "audit-plugin");
    if (!Directory.Exists(auditSkillSrc))
    {
        Console.Error.WriteLine($"FATAL: audit skill payload not found ({auditSkillSrc}).");
        return 2;
    }
    Io.CopyTree(auditSkillSrc, Path.Combine(target, ".claude", "skills", "audit-plugin"),
        merge.Force, merge.DryRun, log, "claude:skills/audit-plugin");

    string conservativeSettings = Path.Combine(payloadRoot, "settings.fragment.json");
    if (!File.Exists(conservativeSettings))
    {
        Console.Error.WriteLine($"FATAL: settings profile not found ({conservativeSettings}).");
        return 2;
    }
    Io.MergeSettingsObject(
        JsonNode.Parse(File.ReadAllText(conservativeSettings))!.AsObject(),
        Path.Combine(target, ".claude", "settings.json"),
        merge,
        log);

    string promptTemplatePath = Path.Combine(payloadRoot, "audit-prompt.md");
    if (!File.Exists(promptTemplatePath))
    {
        Console.Error.WriteLine($"FATAL: audit prompt template not found ({promptTemplatePath}).");
        return 2;
    }
    string prompt = RenderAuditPrompt(File.ReadAllText(promptTemplatePath), pluginName, pluginDir, markdownFiles, target);

    Console.WriteLine($"Samwise: auditing plugin '{pluginName}'");
    Console.WriteLine($"Target repo: {target}");
    Console.WriteLine($"Plugin directory: {pluginDir}");
    Console.WriteLine($"Markdown files: {markdownFiles.Count}");
    bool headless = !o.Audit.Interactive || !string.IsNullOrWhiteSpace(o.Audit.OutputFile);
    Console.WriteLine($"Mode: {(headless ? "headless" : "interactive")}");
    if (!string.IsNullOrWhiteSpace(o.Audit.OutputFile))
        Console.WriteLine($"Output file: {Path.GetFullPath(Path.Combine(target, o.Audit.OutputFile))}");

    Console.WriteLine();
    log.Summarize();

    if (o.DryRun)
    {
        Console.WriteLine("\nDry run complete. Re-run without --dry-run to execute Claude audit.");
        Console.WriteLine("\nPrompt preview:");
        Console.WriteLine(prompt);
        return 0;
    }

    return LaunchClaudeAudit(target, prompt, o.Audit);
}

static bool TryResolvePluginDir(string targetDir, string pluginsDirOption, string pluginName, out string pluginDir, out string error)
{
    var candidates = new List<string>();
    if (!string.IsNullOrWhiteSpace(pluginsDirOption))
    {
        string pluginsRoot = Path.IsPathRooted(pluginsDirOption)
            ? pluginsDirOption
            : Path.Combine(targetDir, pluginsDirOption);
        candidates.Add(Path.GetFullPath(Path.Combine(pluginsRoot, pluginName)));
    }
    candidates.Add(Path.GetFullPath(Path.Combine(targetDir, pluginName)));

    foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
    {
        if (Directory.Exists(candidate))
        {
            pluginDir = candidate;
            error = "";
            return true;
        }
    }

    pluginDir = "";
    error = $"plugin '{pluginName}' not found. Checked: {string.Join(", ", candidates)}";
    return false;
}

static string RenderAuditPrompt(string template, string pluginName, string pluginDir, IReadOnlyList<string> markdownFiles, string targetDir)
{
    string relPluginDir = Path.GetRelativePath(targetDir, pluginDir).Replace('\\', '/');
    string fileList = string.Join("\n", markdownFiles.Select(f => $"- {Path.GetRelativePath(targetDir, f).Replace('\\', '/')}"));
    return template
        .Replace("{{PLUGIN_NAME}}", pluginName)
        .Replace("{{PLUGIN_DIR}}", relPluginDir)
        .Replace("{{MARKDOWN_FILE_LIST}}", fileList);
}

static int LaunchClaudeAudit(string targetDir, string prompt, AuditOptions audit)
{
    if (!IsCommandOnPath("claude"))
    {
        Console.Error.WriteLine("FATAL: 'claude' CLI was not found on PATH. Install with: npm install -g @anthropic-ai/claude-code");
        return 1;
    }

    bool headless = !audit.Interactive || !string.IsNullOrWhiteSpace(audit.OutputFile);
    var psi = new ProcessStartInfo("claude")
    {
        WorkingDirectory = targetDir,
        UseShellExecute = false
    };
    psi.ArgumentList.Add("--allowedTools");
    psi.ArgumentList.Add("Read,Glob,Grep,LS");

    if (headless)
    {
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add(prompt);
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
    }
    else
    {
        psi.RedirectStandardInput = true;
    }

    using var process = Process.Start(psi);
    if (process is null)
    {
        Console.Error.WriteLine("FATAL: could not start 'claude' CLI process.");
        return 1;
    }

    if (headless)
    {
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        string stdout = stdoutTask.GetAwaiter().GetResult();
        string stderr = stderrTask.GetAwaiter().GetResult();

        if (!string.IsNullOrWhiteSpace(audit.OutputFile))
        {
            string outputPath = Path.GetFullPath(Path.Combine(targetDir, audit.OutputFile));
            string? outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
                Directory.CreateDirectory(outputDir);
            File.WriteAllText(outputPath, stdout);
            Console.WriteLine($"Audit report saved to: {outputPath}");
        }

        if (stdout.Length > 0) Console.Write(stdout);
        if (stderr.Length > 0) Console.Error.Write(stderr);
        return process.ExitCode;
    }

    process.StandardInput.WriteLine(prompt);
    process.StandardInput.Flush();
    process.WaitForExit();
    return process.ExitCode;
}

static bool IsCommandOnPath(string command)
{
    string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
    if (OperatingSystem.IsWindows())
    {
        string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM";
        var exts = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (string dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string ext in exts)
            {
                string candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate)) return true;
            }
            if (File.Exists(Path.Combine(dir, command))) return true;
        }
        return false;
    }

    foreach (string dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
        if (File.Exists(Path.Combine(dir, command))) return true;
    }
    return false;
}

static string? ResolveAdoOrg(string? explicitOrg, bool noInput)
{
    if (!string.IsNullOrWhiteSpace(explicitOrg)) return explicitOrg.Trim();
    if (noInput || Console.IsInputRedirected)
    {
        Console.WriteLine("Azure DevOps: no --ado-org given and input is non-interactive - skipping the azure-devops server.");
        return null;
    }
    Console.Write("Azure DevOps organization name (e.g. 'contoso' for dev.azure.com/contoso; Enter to skip): ");
    string? entered = Console.ReadLine();
    return string.IsNullOrWhiteSpace(entered) ? null : entered.Trim();
}

static JsonObject BuildAdoServer(string org) => new()
{
    ["type"] = "stdio",
    ["command"] = "npx",
    ["args"] = new JsonArray("-y", "@azure-devops/mcp", org, "--authentication", "azcli")
};

static JsonObject BuildHttpServer(string url) => new()
{
    ["type"] = "http",
    ["url"] = url
};

static string? ResolveSqlMcpUrl(string? explicitUrl, bool noInput)
{
    if (!string.IsNullOrWhiteSpace(explicitUrl)) return explicitUrl.Trim();
    if (noInput || Console.IsInputRedirected) return null;
    Console.Write("SQL/database MCP server URL (http endpoint, e.g. http://host:5000/mcp; Enter to skip): ");
    string? entered = Console.ReadLine();
    return string.IsNullOrWhiteSpace(entered) ? null : entered.Trim();
}

static Options ParseArgs(string[] args)
{
    var o = new Options
    {
        Command = args.Length > 0 && !args[0].StartsWith('-') ? args[0] : null,
        Target = Directory.GetCurrentDirectory()
    };

    int startIndex = 0;
    if (o.Command is not null) startIndex = 1;
    if (string.Equals(o.Command, "audit", StringComparison.OrdinalIgnoreCase)
        && startIndex < args.Length
        && !args[startIndex].StartsWith('-'))
    {
        o.Audit.PluginName = args[startIndex];
        startIndex++;
    }

    for (int i = startIndex; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--target" or "-t" when i + 1 < args.Length:
                o.Target = args[++i];
                break;
            case "--platform" or "-p" when i + 1 < args.Length:
                o.Platform = args[++i].Trim().ToLowerInvariant();
                break;
            case "--permission-level" when i + 1 < args.Length:
                o.PermissionLevel = ParsePermissionLevel(args[++i]);
                break;
            case "--ado-org" when i + 1 < args.Length:
                o.AdoOrg = args[++i];
                break;
            case "--sql-mcp-url" when i + 1 < args.Length:
                o.SqlMcpUrl = args[++i];
                break;
            case "--overlay" when i + 1 < args.Length:
                o.OverlayPath = args[++i];
                break;
            case "--force" or "-f":
                o.Force = true;
                break;
            case "--dry-run" or "-n":
                o.DryRun = true;
                break;
            case "--no-input":
                o.NoInput = true;
                break;
            case "--plugins-dir" when i + 1 < args.Length:
                o.Audit.PluginsDir = args[++i];
                break;
            case "--output" when i + 1 < args.Length:
                o.Audit.OutputFile = args[++i];
                break;
            case "--interactive":
                o.Audit.Interactive = true;
                break;
            case "--no-interactive":
                o.Audit.Interactive = false;
                break;
            case "--help" or "-h":
                o.Help = true;
                break;
        }
    }

    return o;
}

static PermissionLevel ParsePermissionLevel(string value) =>
    value.Trim().ToLowerInvariant() switch
    {
        "conservative" => PermissionLevel.Conservative,
        "trusted" or "trust" => PermissionLevel.Trusted,
        _ => throw new ArgumentException($"Unknown permission level '{value}'. Use conservative or trusted.")
    };

static string FormatPermissionLevel(PermissionLevel level) =>
    level switch
    {
        PermissionLevel.Conservative => "conservative",
        PermissionLevel.Trusted => "trusted",
        _ => level.ToString()
    };

static void PrintUsage()
{
    Console.WriteLine(
"""
samwise - bootstrap a repository's AI-assistant setup (Claude Code / Codex / Copilot).

Usage:
  samwise init [options]   Install skills/playbooks, MCP servers & settings into a repo.
  samwise list             Show the neutral core and per-platform outputs.
  samwise audit <plugin> [options]
                           Audit a Claude marketplace plugin's markdown quality.

Options:
  -t, --target <dir>              Target repo (default: current directory).
  -p, --platform <name>           claude (default), codex, copilot, or all.
      --permission-level <level>  conservative (default) or trusted.
      --ado-org <name>            Azure DevOps organization for the azure-devops MCP server.
                                  If omitted, init prompts for it (Enter to skip).
      --sql-mcp-url <url>         HTTP endpoint of a SQL/database MCP server (optional).
                                  If omitted, init prompts for it (Enter to skip).
      --overlay <file>            JSON overlay merged on top of the neutral core. May contain
                                  "mcpServers", "skillsDir", and (Claude only) "settings".
      --plugins-dir <path>        Plugin parent folder for 'audit' (default: plugins; falls back to repo root).
      --output <file>             For 'audit', save headless Claude output to file (relative to target unless absolute).
      --interactive               For 'audit', keep Claude in interactive mode (default).
      --no-interactive            For 'audit', run Claude headless via -p and print report.
      --no-input                  Never prompt; skip optional servers and hook replacement prompts.
  -f, --force                     Overwrite existing files / incompatible config keys.
  -n, --dry-run                   Show planned changes without writing.

The core is organization-neutral. Org-specific values come only from flags, prompts, or an
--overlay file - nothing org-specific is baked into the package.
Idempotent: re-running is safe. Existing files and config keys are kept unless --force.
Auth is interactive at runtime (GitHub OAuth, Azure DevOps az login) - no tokens are stored.
Hooks/permission profiles are full-fidelity on Claude; on Codex/Copilot they become written guidance.
""");
}

enum PermissionLevel
{
    Conservative,
    Trusted
}

sealed class Options
{
    public string? Command { get; set; }
    public string Target { get; set; } = "";
    public string Platform { get; set; } = "claude";
    public PermissionLevel PermissionLevel { get; set; } = PermissionLevel.Conservative;
    public string? AdoOrg { get; set; }
    public string? SqlMcpUrl { get; set; }
    public string? OverlayPath { get; set; }
    public bool Force { get; set; }
    public bool DryRun { get; set; }
    public bool NoInput { get; set; }
    public bool Help { get; set; }
    public AuditOptions Audit { get; } = new();
}

sealed class AuditOptions
{
    public string? PluginName { get; set; }
    public string PluginsDir { get; set; } = "plugins";
    public string? OutputFile { get; set; }
    public bool Interactive { get; set; } = true;
}
