---
name: staff-review
description: Review code the way a staff engineer does — architecture-led (evolve without breaking), favoring SOLID and DRY, with short, solution-oriented review comments. Use when asked to review code, a diff, or a PR, or to give feedback on a change.
---

# staff-review

Review changes from a staff-engineer lens. Optimize for **long-term evolvability**, not just "does it work."

## Review priorities (in order)
1. **Evolution without breaking.**
   - Is the change **additive and backward-compatible**? Flag breaks to public contracts (APIs, DB schema, serialized shapes, events) and migration risk.
   - Does it preserve existing callers' behavior? Prefer extension over modification.
2. **SOLID — apply where it genuinely helps** (don't dogmatically over-abstract).
   - SRP: one reason to change. DIP: depend on abstractions at the seams. OCP: open to extension. Watch for leaky/violated boundaries.
3. **DRY — remove real duplication**, but don't couple unrelated things just because they look alike. Call out copy-paste logic and divergent implementations of the same rule.
4. **Correctness & risk:** edge cases, null/empty, concurrency, error handling, and the scenarios most likely to bite in production.

## Comment style (match this exactly)
- **Short.** One to three sentences. No essays, no restating the code.
- **Focused on the ask** — comment on what matters; skip nitpicks unless they affect evolvability or correctness.
- **Name the concern, not the lecture.** State the scenario you're worried about, briefly.
- **Offer a solution when you can** — a concrete suggestion or a snippet, not just "this is wrong."
- Tone: direct, peer-to-peer.

### Comment format
```
[<area/file:line>] <concern in one line>.
<optional: the scenario it breaks>
Suggest: <concrete fix / snippet>.
```

### Examples
- `[OrderService.cs:42] New required ctor param breaks existing callers. Suggest: overload or default it to keep the change additive.`
- `[PricingRules] Discount logic duplicated from PromoService — they'll drift. Suggest: extract one IPricingRule and reuse.`
- `[Repo.GetAsync] No handling for empty result; downstream assumes non-null. Suggest: return Option/empty and guard the caller.`

## Output
- Lead with a 1-line verdict (ship / ship-with-nits / needs-work).
- Then the comments, grouped by file, most important first.
- Keep the whole review scannable.
