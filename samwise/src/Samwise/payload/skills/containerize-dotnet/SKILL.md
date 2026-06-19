---
name: containerize-dotnet
description: Run and test a .NET application in containers — the default way to test modern .NET apps locally and in CI, with containerized dependencies (DB, storage/queue emulators). IMPORTANT — applies to modern .NET (Core / 5 / 6 / 7 / 8 / 9+) only; does NOT apply to .NET Framework (net4xx), which should be tested natively. Use when running/testing a .NET app, setting up integration tests, or asked to containerize a service.
---

# containerize-dotnet

**Default rule: when testing a .NET application, run it in a container** — and back its integration tests with containerized dependencies. This gives environment parity between dev and CI and removes the need for cloud creds locally.

## First: detect the target framework — this gates everything
Read the project's `<TargetFramework>` / `<TargetFrameworks>`:

- **Modern .NET** — `net5.0`+, `net6.0`/`7.0`/`8.0`/`9.0`, `netcoreappX`, or a `netstandardX` library hosted by a modern test runner → **containerize** (Linux containers). Proceed below.
- **.NET Framework** — `net48`, `net472`, `net47`, `net46…`, or non-SDK-style projects → **do NOT containerize for testing.** Build and run it natively on Windows. Linux containers can't run .NET Framework, and Windows containers add cost/complexity that defeats the purpose. Say so explicitly and fall back to host-based `dotnet test` / MSBuild + the native test runner.
- **Mixed / multi-target** — containerize the modern TFM for test runs; test the `net4xx` target natively.

If unsure, list the TFMs you found and state which path you're taking before doing anything.

## App container (modern .NET) — multi-stage
```dockerfile
# build + test stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore
RUN dotnet build -c Release --no-restore
# (run tests here in CI: RUN dotnet test -c Release --no-build)

# runtime stage (aspnet for web, runtime for console/worker)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /src/bin/Release/net8.0/publish ./
ENTRYPOINT ["dotnet", "YourApp.dll"]
```
Match the SDK/runtime tag to the detected TFM (use `:9.0` for net9.0, etc.).

## Dependencies for integration tests — compose
Bring up the app's backing services as containers so integration tests run against realistic instances, not mocks or the cloud:
```yaml
services:
  app:
    build: .
    depends_on: [db, storage]
    environment:
      ConnectionStrings__Db: "Server=db;Database=app;User Id=sa;Password=Your_strong_pwd1;TrustServerCertificate=true"
  db:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment: { ACCEPT_EULA: "Y", MSSQL_SA_PASSWORD: "Your_strong_pwd1" }
  storage:        # Azure Blob/Queue/Table emulator
    image: mcr.microsoft.com/azure-storage/azurite
```
Common emulators: **Azurite** (Azure Storage), **LocalStack** (AWS), a matching **database** image pinned to the production major version, message brokers as needed.

## Running tests
- Local quick loop: `dotnet test` against compose-managed dependencies.
- CI: build the image, run `dotnet test` inside it (or `docker compose run app dotnet test`) so the test environment matches production.
- Prefer ephemeral containers per test run; pin image tags for reproducibility.

## Guardrails
- Never silently containerize a `.NET Framework` project — call it out and use the native path.
- Keep secrets/connection strings in compose env or a vault, not baked into images.
- Pin base-image and dependency tags; don't rely on `latest` in CI.

## Related
- [[greenfield-scaffold]] — sets this up from day one.
- [[migration-strategy]] / [[migrate-unit]] — verification of modern-.NET units runs here.
