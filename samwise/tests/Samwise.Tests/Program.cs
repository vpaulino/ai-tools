using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

var repoRoot = FindRepoRoot();
var toolProject = Path.Combine(repoRoot, "src", "Samwise", "Samwise.csproj");
var toolExe = Path.Combine(repoRoot, "src", "Samwise", "bin", "Release", "net8.0", OperatingSystem.IsWindows() ? "Samwise.exe" : "Samwise");

Run("dotnet", "build", toolProject, "-c", "Release");

var tests = new (string Name, Action Body)[]
{
    ("dry-run writes nothing", DryRunWritesNothing),
    ("conservative profile is default", ConservativeProfileIsDefault),
    ("trusted profile allows convenience tools", TrustedProfileAllowsConvenienceTools),
    ("existing hook arrays are appended and deduped", ExistingHookArraysAreAppendedAndDeduped),
    ("incompatible hooks are preserved in no-input mode", IncompatibleHooksArePreservedInNoInputMode),
    ("overlay merges servers and permissions", OverlayMergesServersAndPermissions),
    ("codex renders AGENTS.md, playbooks and config.toml", CodexRendererWritesAgentsPlaybooksAndToml),
    ("codex config.toml is idempotent on rerun", CodexTomlIsIdempotent),
    ("copilot renders instructions, applyTo and vscode mcp", CopilotRendererWritesInstructionsApplyToAndMcp),
    ("platform all renders every platform", AllPlatformRendersEverything)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Body();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failures.Add($"{test.Name}: {ex.Message}");
        Console.WriteLine($"FAIL {test.Name}");
        Console.WriteLine(ex);
    }
}

if (failures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Failures:");
    foreach (var failure in failures) Console.WriteLine($"- {failure}");
    return 1;
}

Console.WriteLine();
Console.WriteLine($"{tests.Length} tests passed.");
return 0;

void DryRunWritesNothing()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--dry-run", "--no-input");

    Assert(!Directory.Exists(Path.Combine(dir.Path, ".claude")), "dry-run created .claude");
    Assert(!File.Exists(Path.Combine(dir.Path, ".mcp.json")), "dry-run created .mcp.json");
}

void ConservativeProfileIsDefault()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--no-input");

    var settings = LoadSettings(dir.Path);
    var allow = Strings(settings["permissions"]!["allow"]);
    var ask = Strings(settings["permissions"]!["ask"]);
    var deny = Strings(settings["permissions"]!["deny"]);

    Assert(allow.Contains("Read"), "conservative profile should allow Read");
    Assert(!allow.Contains("Bash"), "conservative profile should not allow Bash");
    Assert(!allow.Contains("PowerShell"), "conservative profile should not allow PowerShell");
    Assert(!allow.Contains("Edit"), "conservative profile should not allow Edit");
    Assert(!allow.Contains("Write"), "conservative profile should not allow Write");
    Assert(ask.Contains("Bash"), "conservative profile should ask for Bash");
    Assert(ask.Contains("PowerShell"), "conservative profile should ask for PowerShell");
    Assert(ask.Contains("Edit"), "conservative profile should ask for Edit");
    Assert(ask.Contains("Write"), "conservative profile should ask for Write");
    Assert(deny.Contains("Bash(diskpart:*)"), "dangerous shell deny missing");
    Assert(File.Exists(Path.Combine(dir.Path, ".claude", "skills", "start-pbi", "SKILL.md")), "skill was not copied");
}

void TrustedProfileAllowsConvenienceTools()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--permission-level", "trusted", "--no-input");

    var settings = LoadSettings(dir.Path);
    var allow = Strings(settings["permissions"]!["allow"]);

    Assert(allow.Contains("Bash"), "trusted profile should allow Bash");
    Assert(allow.Contains("PowerShell"), "trusted profile should allow PowerShell");
    Assert(allow.Contains("Edit"), "trusted profile should allow Edit");
    Assert(allow.Contains("Write"), "trusted profile should allow Write");
}

void ExistingHookArraysAreAppendedAndDeduped()
{
    using var dir = TempDir();
    var settingsPath = Path.Combine(dir.Path, ".claude", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
    File.WriteAllText(settingsPath, """
    {
      "permissions": { "allow": ["Read"] },
      "hooks": {
        "PreToolUse": [
          {
            "matcher": "CustomTool",
            "hooks": [
              { "type": "command", "command": "echo custom" }
            ]
          }
        ]
      }
    }
    """);

    RunTool("init", "--target", dir.Path, "--no-input");
    var settings = LoadSettings(dir.Path);
    var preToolUse = settings["hooks"]!["PreToolUse"]!.AsArray();

    Assert(preToolUse.Count == 3, $"expected 3 PreToolUse entries after append, got {preToolUse.Count}");
    Assert(preToolUse[0]!["matcher"]!.GetValue<string>() == "CustomTool", "existing hook entry should stay first");
    Assert(preToolUse.Any(n => n!["matcher"]!.GetValue<string>() == "Bash|PowerShell"), "HTTP guard hook was not appended");
    Assert(preToolUse.Any(n => n!["matcher"]!.GetValue<string>() == "mcp__.*"), "MCP read hook was not appended");

    RunTool("init", "--target", dir.Path, "--no-input");
    settings = LoadSettings(dir.Path);
    preToolUse = settings["hooks"]!["PreToolUse"]!.AsArray();
    Assert(preToolUse.Count == 3, $"expected rerun to dedupe hooks, got {preToolUse.Count}");
}

void IncompatibleHooksArePreservedInNoInputMode()
{
    using var dir = TempDir();
    var settingsPath = Path.Combine(dir.Path, ".claude", "settings.json");
    Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
    File.WriteAllText(settingsPath, """
    {
      "hooks": {
        "PreToolUse": { "custom": true }
      }
    }
    """);

    RunTool("init", "--target", dir.Path, "--no-input");
    var settings = LoadSettings(dir.Path);
    Assert(settings["hooks"]!["PreToolUse"] is JsonObject, "incompatible hook event should remain an object");
    Assert(settings["hooks"]!["PreToolUse"]!["custom"]!.GetValue<bool>(), "existing incompatible hook value changed");
}

void OverlayMergesServersAndPermissions()
{
    using var dir = TempDir();
    var overlayPath = Path.Combine(dir.Path, "overlay.json");
    File.WriteAllText(overlayPath, """
    {
      // comments and trailing commas are allowed
      "mcpServers": {
        "local-private-db": {
          "type": "http",
          "url": "http://localhost:5000/mcp"
        },
      },
      "settings": {
        "permissions": {
          "allow": ["Bash(az account show:*)"],
        },
      },
    }
    """);

    RunTool("init", "--target", dir.Path, "--overlay", overlayPath, "--no-input");

    var mcp = JsonNode.Parse(File.ReadAllText(Path.Combine(dir.Path, ".mcp.json")))!.AsObject();
    Assert(mcp["mcpServers"]!["local-private-db"] is not null, "overlay MCP server missing");

    var settings = LoadSettings(dir.Path);
    var allow = Strings(settings["permissions"]!["allow"]);
    Assert(allow.Contains("Bash(az account show:*)"), "overlay permission missing");
}

void CodexRendererWritesAgentsPlaybooksAndToml()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--platform", "codex", "--no-input");

    var agents = Path.Combine(dir.Path, "AGENTS.md");
    Assert(File.Exists(agents), "AGENTS.md not written");
    var agentsText = File.ReadAllText(agents);
    Assert(agentsText.Contains("samwise:begin"), "AGENTS.md missing managed block");
    Assert(agentsText.Contains("## Playbooks"), "AGENTS.md missing playbook index");
    Assert(agentsText.Contains(".samwise/playbooks/migration-strategy.md"), "AGENTS.md missing playbook pointer");

    Assert(File.Exists(Path.Combine(dir.Path, ".samwise", "playbooks", "migration-strategy.md")), "playbook file missing");

    var toml = Path.Combine(dir.Path, ".codex", "config.toml");
    Assert(File.Exists(toml), "config.toml not written");
    var tomlText = File.ReadAllText(toml);
    Assert(tomlText.Contains("[mcp_servers.binlog]"), "config.toml missing binlog server");
    Assert(tomlText.Contains("[mcp_servers.github]"), "config.toml missing github server");

    Assert(!Directory.Exists(Path.Combine(dir.Path, ".claude")), "codex platform should not create .claude");
    Assert(!File.Exists(Path.Combine(dir.Path, ".mcp.json")), "codex platform should not create .mcp.json");
}

void CodexTomlIsIdempotent()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--platform", "codex", "--no-input");
    RunTool("init", "--target", dir.Path, "--platform", "codex", "--no-input");

    var tomlText = File.ReadAllText(Path.Combine(dir.Path, ".codex", "config.toml"));
    int count = CountOccurrences(tomlText, "[mcp_servers.binlog]");
    Assert(count == 1, $"expected binlog server once after rerun, got {count}");
}

void CopilotRendererWritesInstructionsApplyToAndMcp()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--platform", "copilot", "--no-input");

    var baseInstr = Path.Combine(dir.Path, ".github", "copilot-instructions.md");
    Assert(File.Exists(baseInstr), "copilot-instructions.md not written");
    Assert(File.ReadAllText(baseInstr).Contains("samwise:begin"), "copilot-instructions.md missing managed block");

    var skillInstr = Path.Combine(dir.Path, ".github", "instructions", "migration-strategy.instructions.md");
    Assert(File.Exists(skillInstr), "skill instruction file missing");
    Assert(File.ReadAllText(skillInstr).Contains("applyTo:"), "instruction file missing applyTo frontmatter");

    var mcp = Path.Combine(dir.Path, ".vscode", "mcp.json");
    Assert(File.Exists(mcp), "vscode mcp.json not written");
    var node = JsonNode.Parse(File.ReadAllText(mcp))!.AsObject();
    Assert(node["servers"]!["github"] is not null, "vscode mcp.json missing github under servers");

    Assert(!Directory.Exists(Path.Combine(dir.Path, ".claude")), "copilot platform should not create .claude");
}

void AllPlatformRendersEverything()
{
    using var dir = TempDir();
    RunTool("init", "--target", dir.Path, "--platform", "all", "--no-input");

    Assert(File.Exists(Path.Combine(dir.Path, ".claude", "settings.json")), "all: claude settings missing");
    Assert(File.Exists(Path.Combine(dir.Path, "AGENTS.md")), "all: AGENTS.md missing");
    Assert(File.Exists(Path.Combine(dir.Path, ".github", "copilot-instructions.md")), "all: copilot instructions missing");
}

int CountOccurrences(string haystack, string needle)
{
    int count = 0, idx = 0;
    while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0)
    {
        count++;
        idx += needle.Length;
    }
    return count;
}

JsonObject LoadSettings(string target) =>
    JsonNode.Parse(File.ReadAllText(Path.Combine(target, ".claude", "settings.json")))!.AsObject();

HashSet<string> Strings(JsonNode? node)
{
    Assert(node is not null, "expected JSON array node to exist");
    return node!.AsArray().Select(n => n!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
}

void RunTool(params string[] args) => Run(toolExe, args);

void Run(string fileName, params string[] args)
{
    var psi = new ProcessStartInfo(fileName)
    {
        WorkingDirectory = repoRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    foreach (var arg in args) psi.ArgumentList.Add(arg);

    using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}");
    string stdout = process.StandardOutput.ReadToEnd();
    string stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();

    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{fileName} exited {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
}

TempDirectory TempDir() => new();

void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "src", "Samwise", "Samwise.csproj")))
            return dir.FullName;
        dir = dir.Parent;
    }
    throw new InvalidOperationException("Could not find repo root.");
}

sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "samwise-tests", Guid.NewGuid().ToString("N"));

    public TempDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
