---
name: audit-plugin
description: Audit Claude marketplace plugin markdown quality, score maturity (Rookie/Intermediate/Mature), and provide prioritized recommendations.
---

# audit-plugin

Audit a Claude marketplace plugin by reading all markdown files in the plugin folder.
Produce a practical report with maturity scoring and clear recommendations.

## Dimensions and scoring

Evaluate each dimension as **Rookie**, **Intermediate**, or **Mature**.

### 1) Structure
Assess whether the plugin has the expected files and organization:
- `README.md`
- Entry-point skill/instruction markdown
- Example markdown files
- Frontmatter/manifest presence where applicable

Scoring:
- **Rookie**: major structure gaps; key files missing.
- **Intermediate**: core files exist but some important gaps or weak organization.
- **Mature**: complete structure with clear, consistent organization.

### 2) Description quality
Assess whether docs clearly explain:
- Purpose
- Intended users/use cases
- Constraints/non-goals

Scoring:
- **Rookie**: purpose unclear or generic, limited context.
- **Intermediate**: purpose is mostly clear but has missing scope/constraints.
- **Mature**: clear purpose, audience, use cases, and constraints.

### 3) Instruction quality
Assess whether instructions are specific, actionable, and testable:
- Concrete steps and expectations
- Avoids vague/circular phrasing
- Includes safety/guardrails when relevant

Scoring:
- **Rookie**: vague or ambiguous instructions; hard to execute reliably.
- **Intermediate**: mostly actionable with some ambiguity or missing details.
- **Mature**: precise, actionable, consistent, and low ambiguity.

### 4) Examples quality
Assess whether examples include:
- Typical invocations
- Important edge cases
- Inputs/outputs or expected behavior

Scoring:
- **Rookie**: no examples or low-value examples.
- **Intermediate**: examples cover common paths but miss edge cases/context.
- **Mature**: examples cover core and edge scenarios with clear expectations.

### 5) Metadata quality
Assess frontmatter metadata completeness and usefulness:
- `name`
- `description`
- `version`
- `author`
- `applyTo` (when relevant)

Scoring:
- **Rookie**: metadata mostly missing or inconsistent.
- **Intermediate**: partial metadata; some important fields missing or weak.
- **Mature**: complete, consistent, and useful metadata.

## Output format

Return a markdown report with these sections and order:

1. `# Plugin Audit: <plugin-name>`
2. `**Overall maturity: <Rookie|Intermediate|Mature>**`
3. `## Dimension Scores` as a markdown table
4. `## Findings & Recommendations`
   - `### Must-add`
   - `### Nice-to-have`
   - `### Consider removing`
5. `## Per-file notes`

## Prioritization rules
- Prioritize recommendations by impact on usability and maintainability.
- Keep recommendations concrete and actionable.
- Explicitly identify additions, improvements, and removals.
