---
name: greenfield-scaffold
description: Start a NEW project/service following software-design best practices — clean boundaries (ports & adapters), contract-first integration, config/secrets separation, a test pyramid, container-first local dev, and CI gates. Use when creating a greenfield app/service/library from scratch, or when the user says "new project", "scaffold a service", "start fresh".
---

# greenfield-scaffold

Lay down a new project so it's evolvable, testable, and migration-friendly from day one — the opposite end of the work that [[migration-strategy]] has to clean up later.

## Design principles
- **Ports & adapters.** Business logic depends on interfaces, never directly on a storage SDK, cloud provider, or framework. Pattern: *logic → interface → adapter → SDK/provider*. This is what makes future provider/stack swaps painless.
- **Contract-first integration.** When services talk, define the contract (JSON/schema) first; each side owns its models against a shared, versioned, strongly-typed definition so drift fails at compile time.
- **Clear separation of concerns.** Give each service one job (orchestration vs. rendering/delivery vs. persistence); don't bleed responsibilities across boundaries.
- **SOLID where it earns its keep** — don't over-abstract; abstract at the seams that will actually change.
- **Fail loud on bad config**, never silent. Validate configuration at startup.

## Configuration & secrets
- No hardcoded endpoints, credentials, or provider names.
- Config flows: environment → config files (per-environment) → dependency injection. Same code across environments.
- Secrets live in a vault (Key Vault / Secrets Manager), never in source. Config files in source contain no secrets.

## Test strategy (pyramid)
- **Unit** — mocked dependencies; fast, isolated, frequent.
- **Integration** — against **containerized** dependencies/emulators (DB, storage, queues); no cloud creds needed locally.
- **Smoke** — critical paths against a staging-like environment.
- Test fixtures (sample data, configs) live in source control so new edge cases are data, not code.

## Container-first local dev
- For modern **.NET** apps, run and test in containers from the start — see [[containerize-dotnet]]. Developers shouldn't need VPN/cloud creds to run the test suite; CI uses the same containers for parity.

## CI gates from day one
- Build + all test levels run in CI; deployment blocked on failure.
- Same code to every environment; behavior toggled by **feature flags**, enabling gradual rollout and flag-flip rollback.

## Related
- [[containerize-dotnet]] — the container setup this assumes for .NET.
- [[safe-evolution]] — how the project should change once it has consumers.
- [[staff-review]] — design review lens.
