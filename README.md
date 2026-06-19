# ai-tools

[![ci](https://github.com/vpaulino/ai-tools/actions/workflows/ci.yml/badge.svg)](https://github.com/vpaulino/ai-tools/actions/workflows/ci.yml)

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

### Install a published tool

Once a tool is published to NuGet.org, install it directly (no `--add-source`):

```pwsh
dotnet tool install --global Samwise
samwise init
```

### Build & test Samwise

```pwsh
cd samwise/src/Samwise
dotnet build -c Release
dotnet pack  -c Release -o ../../artifacts

# run the test suite (builds the tool and exercises every renderer)
dotnet run --project ../../tests/Samwise.Tests -c Release
```

## Releasing (CI/CD)

- **CI** — `.github/workflows/ci.yml` builds and runs the full test suite on every
  push and pull request to `main`.
- **Publish** — `.github/workflows/publish.yml` triggers on a version tag (`v*`).
  It tests, packs the tool with the tag's version, pushes it to NuGet.org, and
  creates a GitHub Release with the `.nupkg` attached.

Release flow:

```pwsh
# work on a branch, open a PR, merge to main (CI must be green)
git tag v0.7.1
git push origin v0.7.1        # -> publish workflow ships Samwise 0.7.1 to NuGet
```

Publishing uses **NuGet Trusted Publishing** (OIDC) — no API key is stored. A
trusted-publishing policy on nuget.org (owner `vpaulino`, repository `ai-tools`,
workflow `publish.yml`) authorizes the workflow; at release time GitHub issues a
short-lived token that `NuGet/login@v1` exchanges for a 1-hour publish key.

The tag drives the package version (via `-p:Version=`), so the `<Version>` in the
csproj is only the local/dev default.

## Repository conventions

- One folder per tool; each is self-contained and independently packable.
- Build output (`bin/`, `obj/`, `artifacts/`, `*.nupkg`, `*.pdb`) and local
  assistant settings are git-ignored — see [`.gitignore`](.gitignore).
- Nothing organization-specific is committed; tools take org-specific values at
  runtime via flags, prompts, or overlay files.

## License

[MIT](LICENSE) © 2026 Vitor Paulino
