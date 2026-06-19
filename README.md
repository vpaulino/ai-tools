# ai-tools

A home for AI / developer tooling and the NuGet packages built around it.
Each tool lives in its own top-level folder with its own README, and ships as
an independent package.

## Tools

| Tool | Folder | What it does | Package |
| --- | --- | --- | --- |
| **Samwise** | [`samwise/`](samwise/) | Bootstraps a repository's AI-assistant setup (skills/playbooks, MCP servers, settings) for Claude Code, OpenAI Codex, and GitHub Copilot from one vendor-neutral core. | `Samwise` (.NET tool) |

More tools and packages will be added here over time.

## Getting started

```pwsh
git clone https://github.com/vpaulino/ai-tools.git
cd ai-tools
```

Then open the folder of the tool you want — start with [`samwise/`](samwise/),
whose README covers install, usage, and how it renders for each assistant.

### Build & test Samwise

```pwsh
cd samwise/src/Samwise
dotnet build -c Release
dotnet pack  -c Release -o ../../artifacts

# run the test suite (builds the tool and exercises every renderer)
dotnet run --project ../../tests/Samwise.Tests -c Release
```

## Repository conventions

- One folder per tool; each is self-contained and independently packable.
- Build output (`bin/`, `obj/`, `artifacts/`, `*.nupkg`, `*.pdb`) and local
  assistant settings are git-ignored — see [`.gitignore`](.gitignore).
- Nothing organization-specific is committed; tools take org-specific values at
  runtime via flags, prompts, or overlay files.

## License

[MIT](LICENSE) © 2026 Vitor Paulino
