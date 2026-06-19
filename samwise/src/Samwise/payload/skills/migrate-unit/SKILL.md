---
name: migrate-unit
description: Execute the migration of a SINGLE unit (package, assembly, module, or component) from a source stack to a target stack — multi-targeting for framework evolution, or adapter-swap for platform/SDK moves. Covers baseline, classification, conversion, validation gates and a definition-of-done. Use when actually migrating one package/module, e.g. "migrate this library to .NET 8", "move this storage call to the new SDK".
---

# migrate-unit

Migrate one unit end to end while keeping its **public contract stable**. This is the execution arm of [[migration-strategy]] — run it per unit, in the wave order the strategy defined.

## 1. Baseline & classify
- Record: path, package/assembly name, root namespace, public namespaces/types/signatures, and all direct dependencies (project + package).
- Identify stack-specific references (legacy runtime APIs, host-specific code, provider SDKs, ORM coupling).
- Classify the unit:
  - **Portable** — library, low coupling, mostly POCOs/abstractions → migrate fully.
  - **Partial** — portable logic with stack-specific edges → migrate the core, isolate the edges.
  - **Host-first / stays-legacy** — app shell, web host, installers, Windows-only APIs → keep on the source stack or extract a compatibility library.

## 2A. Framework/runtime evolution (multi-target)
For runtime moves (e.g. .NET Framework → modern .NET):
- Convert the project to **SDK-style multi-target**: `<TargetFrameworks>legacy;modern</TargetFrameworks>` — add the new TFM *alongside* the old, don't fork the project.
- Split source into `legacy/` (compiles for the old TFM only) and `portable/` (both TFMs); exclude `legacy/**` for the modern TFM via a conditional `<Compile Remove>`.
- **Move files via version control** (not delete/recreate) to preserve encoding and history.
- Move types one batch at a time, preserving exact namespace/name/signature. Handle blockers in this order:
  1. Replace with a supported API (equivalent behavior).
  2. Isolate platform behavior behind an abstraction.
  3. Use target-specific files (`.Legacy.cs` / `.Modern.cs`).
  4. Minimal `#if` only as last resort.
  5. Leave the feature legacy-only if porting isn't justified.
- **Vet every `PackageReference`** for dual-TFM support; pull only what the portable surface needs onto the modern target.

## 2B. Platform/SDK/provider swap (adapter)
For storage/SDK/cloud moves:
- Define a lean **interface** for the capability; implement it for both source and target.
- Route all callers through the interface — no caller references either SDK directly.
- Initialize the adapter from configuration; **fail loud** on invalid config. Keep secrets in a vault, not source.
- Gate the live implementation behind a feature flag for instant rollback.

## 3. Validation gates (after each batch)
- [ ] Builds cleanly (both TFMs, if multi-targeting).
- [ ] Direct dependents still compile.
- [ ] **Contract tests** pass — reflection checks that visibility/namespace/type/member signatures are unchanged.
- [ ] **Behavior tests** pass for any logic (guards, conversions, semantics).
- [ ] **Architecture tests** hold — the portable/new surface doesn't depend on stack-specific packages.
- [ ] For modern-.NET units, tests run in a container ([[containerize-dotnet]]); integration tests use containerized dependencies/emulators.

## 4. Definition of done
- [ ] Public surface unchanged (or changes explicitly approved + documented).
- [ ] What moved / what stayed on the source stack (with reasons) written up.
- [ ] Packaging verified (e.g. one NuGet emitting both TFM assets; identity unchanged).
- [ ] Tests are part of this change, not a follow-up.

## Red flags — stop and reassess
- Unit identity about to change unintentionally.
- Many unrelated files needing `#if`.
- A top-level/app unit being migrated before its dependencies.
- Broad refactoring overtaking the migration.
- Behavior changes that aren't easily explained or tested.

## Related
- [[migration-strategy]] — the wave plan this executes against.
- [[safe-evolution]] — additive-first / two-step release discipline.
- [[staff-review]] — review lens for the resulting diff.
