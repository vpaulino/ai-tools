---
name: safe-evolution
description: Evolve an existing application or library WITHOUT breaking its consumers — expand/contract (parallel change), additive-first changes, two-step releases (additive then breaking-removal), deprecation and contract tests. Use when changing a public API, DB schema, serialized shape, or event contract, or when the user asks to evolve/refactor something that has callers.
---

# safe-evolution

Change things that have consumers (public APIs, DB schemas, serialized shapes, events, queue messages) so the change is **additive and reversible first**, breaking only as a deliberate, separate step.

## Core rule
**Additive before breaking.** New abstractions/fields/paths coexist with the old ones. Removal of the old path is a *separate, documented* release — never bundled with the introduction of the new one.

## Expand → migrate → contract (parallel change)
1. **Expand** — add the new shape/API/column alongside the old. Both work. Consumers untouched.
2. **Migrate** — move producers and consumers to the new shape incrementally; backfill data; dual-write/dual-read if needed.
3. **Contract** — once nothing uses the old path, remove it. This is the breaking step.

## Two-step release pattern
- **Release N (additive):** introduce the new interface/constructor/implementation; keep the old path working; mark it obsolete.
- **Release N+1 (breaking):** remove the old path; require the new pattern. Label it clearly as breaking; bump the major version.

## Contract discipline
- Don't change public type names, namespaces, or method signatures without explicit approval — prefer an overload or a new member.
- Deprecate before deleting: `[Obsolete]` (or the language equivalent) with a message pointing at the replacement.
- For data/wire contracts: additive fields are safe; renaming/removing/retyping is breaking — version the contract and keep a shared, typed definition so drift fails at compile time, not at runtime.
- Use semantic versioning honestly: minor = additive, major = breaking.

## Verification
- **Contract tests** (reflection / schema / round-trip) prove the old surface is intact during the additive phase.
- Round-trip serialization tests for any data shape change.
- Keep diffs narrow — don't mix style/redesign into an evolution change.

## Related
- [[migrate-unit]] / [[migration-strategy]] — apply this discipline across a migration.
- [[staff-review]] — the review lens that enforces "evolve without breaking".
- [[containerize-dotnet]] — run integration/contract tests in containers for modern .NET.
