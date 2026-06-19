# Working agreement (Samwise)

This repository is set up to be worked with an AI assistant. A set of **playbooks**
is available for recurring engineering work; consult the relevant one before acting.

- **Work tracking is tracker-agnostic.** If a work-tracking integration is connected
  (Jira/Atlassian, Azure DevOps Boards, or GitHub Issues), use it to find and update
  items; don't assume a specific one.
- **Migrations** between two stacks (framework/runtime evolution or platform/infra
  moves) should be planned and executed incrementally, keeping public contracts stable
  and consumers working — never big-bang.
- **Evolve, don't break.** Prefer additive, backward-compatible change; schedule any
  breaking removal as a separate, documented step.
- **Containerized testing for modern .NET.** Run and test modern .NET (Core / 5-9+)
  apps in containers with containerized dependencies. Do **not** containerize
  .NET Framework (net4xx) projects for normal test loops — test those natively.
- **Greenfields** start with clean boundaries (ports & adapters), contract-first
  integration, config/secrets separation, a test pyramid, and CI gates.
