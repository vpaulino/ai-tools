using System.Text.Json;
using System.Text.Json.Nodes;

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

switch (opts.Command)
{
    case "init":
        return RunInit(payloadRoot, opts);
    case "list":
        return RunList(payloadRoot);
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

    for (int i = 0; i < args.Length; i++)
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
}
