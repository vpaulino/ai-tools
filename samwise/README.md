# Samwise

`samwise` is a .NET tool that helps a developer (Frodo) carry their work: it
bootstraps a repository's AI-assistant setup from a single vendor-neutral core
and renders it for the assistant you use — **Claude Code**, **OpenAI Codex**, or
**GitHub Copilot**.

Run it once in a repo and it installs a shared, organization-neutral set of
skills/playbooks, MCP servers, and settings without copy/paste.

The package is neutral by design. Org-specific values — work-tracker routing,
private MCP servers, private skills, internal endpoints — are supplied at `init`
time through flags, prompts, or an optional overlay file.

## The Core, Rendered Per Platform

Samwise keeps one **core model** (skills/playbooks, MCP servers, instructions,
permission intent, hooks) and translates it into each platform's native layout:

| Capability | Claude Code | OpenAI Codex | GitHub Copilot |
| --- | --- | --- | --- |
| Skills / playbooks | `.claude/skills/<name>/SKILL.md` | `.samwise/playbooks/<name>.md` + an index in `AGENTS.md` | `.github/instructions/<name>.instructions.md` (`applyTo`) |
| Instructions | (skills are model-invoked) | `AGENTS.md` | `.github/copilot-instructions.md` |
| MCP servers | `.mcp.json` (JSON) | `.codex/config.toml` (`[mcp_servers.*]`) | `.vscode/mcp.json` (`servers`) |
| Hooks | `.claude/hooks/` + `settings.json` | written guidance (no native hooks) | written guidance (no native hooks) |
| Permissions | `settings.json` allow/deny/ask | guidance in `AGENTS.md` | guidance in `copilot-instructions.md` |

Pick the platform with `--platform`:

```pwsh
samwise init --platform claude     # default
samwise init --platform codex
samwise init --platform copilot
samwise init --platform all
```

Hooks and permission profiles are **full-fidelity on Claude**. Codex and Copilot
have no hook/permission model, so those become written guidance the assistant is
asked to follow.

The install is idempotent. Re-running preserves existing files and configuration
unless you explicitly opt into replacement behavior. Generated docs (`AGENTS.md`,
`copilot-instructions.md`) maintain a `samwise:begin`/`samwise:end` managed block,
leaving any surrounding content you add untouched.

## Install Scopes

Once published to NuGet.org, the simplest install is:

```pwsh
dotnet tool install --global Samwise
samwise init
```

The examples below use `--add-source <feed>` for installing from a local or private
feed (e.g. CI artifacts) before publication; omit it when installing from NuGet.org.
The package is a normal .NET tool, so it can be installed in three useful ways.

### Project-Local

Best when a repo should carry the tool dependency for everyone.

```pwsh
dotnet new tool-manifest
dotnet tool install Samwise --add-source <feed>
dotnet tool run samwise -- init
```

Or use the helper:

```pwsh
.\scripts\install-project.ps1 -Source .\artifacts
```

Commit `.config/dotnet-tools.json` so other contributors can run:

```pwsh
dotnet tool restore
dotnet tool run samwise -- init
```

### User-Global

Best for a single developer who wants the command available from any folder.

```pwsh
dotnet tool install --global Samwise --add-source <feed>
samwise init
```

Or use the helper:

```pwsh
.\scripts\install-user.ps1 -Source .\artifacts
```

In .NET tool terminology, `--global` means user-wide. On Windows it installs
under `%USERPROFILE%\.dotnet\tools`, not for every user on the machine.

### Machine-Wide

.NET tools do not have a true built-in machine-global mode. To make the command
available machine-wide, install it to a shared tool path and add that path to
the machine `PATH`. This requires an elevated PowerShell session.

```pwsh
.\scripts\install-machine.ps1 -Source .\artifacts -ToolPath "C:\Program Files\Samwise\tools"
```

The helper uses `dotnet tool install --tool-path` or `dotnet tool update
--tool-path` and then adds the shared path to the machine `PATH` if needed.

## Runtime Support

The tool targets `net8.0`, the oldest currently supported modern .NET runtime,
and rolls forward on newer supported runtimes such as .NET 9 and .NET 10.

## Quick Start

```pwsh
cd my-repo
samwise list
samwise init --no-input
```

Target a specific assistant, or all of them:

```pwsh
samwise init --platform codex --no-input
samwise init --platform all --no-input
```

Interactive setup can prompt for optional Azure DevOps and SQL MCP values:

```pwsh
samwise init
```

Non-interactive setup can supply them directly:

```pwsh
samwise init --ado-org contoso --sql-mcp-url http://localhost:5000/mcp --no-input
```

## Permission Profiles

`init` supports two permission levels (full-fidelity on Claude; rendered as
guidance on Codex/Copilot):

| Level | Behavior |
| --- | --- |
| `conservative` | Default. Allows read-oriented operations and asks before edits, writes, Bash, or PowerShell. |
| `trusted` | High-convenience profile. Allows edits, writes, Bash, and PowerShell, with deny/ask guardrails for dangerous commands. |

Examples:

```pwsh
samwise init --permission-level conservative
samwise init --permission-level trusted
```

The conservative profile is intended as the reusable/team-safe baseline. Use the
trusted profile only where the repo and operator are comfortable with a more
hands-off local assistant.

## CLI Reference

```text
samwise init [options]
samwise list
```

| Option | Meaning |
| --- | --- |
| `-t, --target <dir>` | Target repo. Defaults to the current directory. |
| `-p, --platform <name>` | `claude` (default), `codex`, `copilot`, or `all`. |
| `--permission-level <level>` | `conservative` or `trusted`. Defaults to `conservative`. |
| `--ado-org <name>` | Adds the Azure DevOps MCP server for that organization. |
| `--sql-mcp-url <url>` | Adds a SQL/database HTTP MCP endpoint. |
| `--overlay <file>` | Merges extra private servers, skills, or (Claude) settings. |
| `--no-input` | Never prompt; skip optional values and hook replacement prompts. |
| `-f, --force` | Overwrite existing files and incompatible config keys. |
| `-n, --dry-run` | Show the planned changes without writing. |

## Merge Semantics

- Skills, hook scripts, playbook and instruction files are written only if
  absent. `--force` overwrites them.
- `.mcp.json` / `.vscode/mcp.json` preserve existing servers unless `--force`.
- `.codex/config.toml` appends MCP server tables that aren't already present and
  never rewrites existing ones.
- `AGENTS.md` / `copilot-instructions.md` update only the `samwise:begin`/`:end`
  managed block; surrounding content is preserved.
- Permission arrays are unioned with existing values.
- Hook event arrays are composed: existing entries stay first, missing incoming
  entries are appended, and exact duplicates are skipped.
- If an existing hook event has an incompatible JSON shape, interactive runs ask
  before replacing it. In `--no-input` mode the event is left untouched.
- `--dry-run` reports the change set without writing.

## Skills / Playbooks

The package ships these neutral skills (rendered per platform as above):

- `start-pbi`: start work on a work item and pin it to local status.
- `log-time`: log time against the current or specified work item.
- `staff-review`: review code from an evolvability/correctness lens.
- `db-explore`: read-only database exploration through a connected SQL MCP.
- `migration-strategy`: plan phased stack or platform migrations.
- `migrate-unit`: migrate one package/module/component safely.
- `safe-evolution`: evolve APIs, schemas, and contracts without breaking consumers.
- `greenfield-scaffold`: scaffold new projects with good boundaries and tests.
- `containerize-dotnet`: run/test modern .NET apps with containers; avoid
  containerizing .NET Framework projects for normal test loops.

## Hooks (Claude only)

| Hook | Event | Purpose |
| --- | --- | --- |
| `prev-session-summary.ps1` | `SessionStart` | Shows previous-session context and, when Jira is connected, a compact work-item overview. |
| `http-mutation-guard.ps1` | `PreToolUse` | Prompts before external HTTP mutations from shell commands. |
| `mcp-read-allow.ps1` | `PreToolUse` | Auto-approves clearly read-only MCP calls. |

The work-item overview is currently enabled only for Atlassian/Jira. Other
trackers are skipped silently until explicit support is added. Codex and Copilot
have no hook system, so this automation is described as guidance instead.

## MCP Servers

Core servers:

- `microsoft-learn`
- `nuget-vulnerabilities`
- `binlog` (Microsoft Binlog MCP Server — investigate MSBuild `.binlog` files)
- `atlassian`
- `github`

Optional servers:

- `azure-devops`, added only with `--ado-org` or an interactive value.
- `sql`, added only with `--sql-mcp-url` or an interactive value.

No tokens are stored by this tool. Authentication is handled by the MCP client
or the underlying provider tooling at runtime.

## Overlays

Use overlays to layer private configuration without baking it into the package.
`mcpServers` and `skillsDir` apply to every platform; `settings` is Claude-only.

```jsonc
{
  "mcpServers": {
    "private-db": {
      "type": "http",
      "url": "http://localhost:5000/mcp"
    }
  },
  "skillsDir": "./private-skills",
  "settings": {
    "permissions": {
      "allow": ["Bash(az account show:*)"]
    }
  }
}
```

```pwsh
samwise init --overlay my-overlay.json
```

Overlay files may contain comments and trailing commas. Keep private overlays
outside public repositories. The `examples/` folder has one fully-explained
example per override.

## Build And Pack

```pwsh
cd src/Samwise
dotnet build -c Release
dotnet pack -c Release -o ../../artifacts

# run the test suite (builds the tool and exercises every renderer)
dotnet run --project ../../tests/Samwise.Tests -c Release
```

Release packages are configured not to include PDB files by default, avoiding
debug path leakage in normal distribution artifacts.

## Privacy Hygiene

Do not commit generated build output, local packages, PDB files, or local Claude
settings. The `.gitignore` excludes:

- `bin/`
- `obj/`
- `artifacts/`
- `.claude/settings.local.json`
- `*.nupkg`
- `*.snupkg`
- `*.pdb`

Run a privacy scan before publishing or sharing a package, especially if a build
was produced on a personal workstation.
