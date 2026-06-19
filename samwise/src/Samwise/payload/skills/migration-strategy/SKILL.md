---
name: migration-strategy
description: Plan a migration between two stacks — framework/runtime evolution (e.g. legacy → modern .NET) or platform/infra moves (storage, SDK, cloud provider). Produces an inventory, a dependency-ordered wave plan, risk tiers, a cutover/rollback approach and a verification checklist. Use when the user is starting or scoping a migration, says "migrate X to Y", "move from <stack> to <stack>", or needs a migration plan.
---

# migration-strategy

Produce a concrete, phased plan to migrate from a **source stack** to a **target stack** — without big-bang rewrites and without breaking consumers mid-flight. Works for runtime/framework evolution *and* platform/infrastructure moves.

## Inputs to establish first
- **Source stack** and **target stack** (be specific: runtimes, SDKs, providers, versions).
- **The boundary**: what actually crosses it (APIs, packages, storage calls, SDK usage, config, endpoints).
- **Constraints**: must ship continuously? hard deadline? consumers you can't break?

## 1. Inventory (before any change)
Catalog everything that crosses the migration boundary — this is the spine of the plan.
- Read the source-of-truth entry points (configs, registries, manifests) and enumerate each artifact: **name, current location/ref, purpose, usage frequency, risk-if-missed**.
- Cross-search the codebase for stray references the source-of-truth missed.
- Flag **silent-failure risks** — anything whose omission produces *no error* (empty output, skipped write) rather than a crash. These need extra verification later.
Output a table. 100% enumeration is the gate to leave this phase.

## 2. Sequence into waves
- **Dependency-ordered, leaf-first.** Migrate low-dependency units first to prove the pattern; high-leverage/foundational pieces last.
- **One identity per unit** — a unit keeps its public contract/identity even if its internals change. (See [[migrate-unit]] for executing one unit.)
- **Incremental, not big-bang.** Introduce the target *alongside* the source before moving code/traffic.
- Assign each unit a **risk tier** (low/med/high) from coupling, blast radius, and silent-failure exposure. Sequence to retire risk early but prove patterns on easy wins first.

## 3. Choose the cutover mechanism
Pick per the migration type:
- **Adapter/seam** — put an interface between callers and the thing being migrated; source and target each implement it. Nothing outside the adapter knows which is live. (Essential for platform/SDK/provider swaps.)
- **Parallel-run** — stand source and target up side by side; verify feature parity on production-like data before any switch.
- **Dual-write / gradual read-switch** — write both, read source first, flip reads to target once proven, drop dual-write after stability.
- **Feature flag** the new path so rollback is a flip, not a redeploy.
- **Environment phasing** — dev → staging → prod, validating end-to-end between each, with a soak gate before prod.

## 4. Rollback & decommission
- Rollback must be tested *before* cutover, not improvised.
- Keep the old path alive through a defined quiet period; decommission only after it stops being hit.

## 5. Verification checklist (definition of done for the migration)
- [ ] Every inventory item migrated and accounted for.
- [ ] Round-trip verified (produce → store/transport → consume → assert).
- [ ] Edge cases exercised (null/missing/malformed, special chars).
- [ ] Silent-failure items explicitly checked (not just "no error").
- [ ] Tests pass against **both** paths while both exist (equivalent contracts).
- [ ] Rollback tested; monitoring/alerts on error rate, latency, missing-data patterns.
- [ ] Old path quiet for the agreed window before decommission.

## Guardrails
- Don't mix cleanup/redesign into migration steps — keep diffs narrow and reviewable.
- Prefer additive moves; schedule any breaking removal as a separate, documented step. See [[safe-evolution]].
- When verifying modern-.NET apps, run them in containers — see [[containerize-dotnet]].

## Related
- [[migrate-unit]] — execute the migration of a single unit.
- [[safe-evolution]] — keep consumers working across the transition.
- [[containerize-dotnet]] — containerized test/run for modern .NET.
