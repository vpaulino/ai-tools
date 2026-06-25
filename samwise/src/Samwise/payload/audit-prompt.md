Audit the Claude marketplace plugin `{{PLUGIN_NAME}}` located at `{{PLUGIN_DIR}}`.

Use the installed `audit-plugin` skill rubric for the full evaluation criteria and output format.

Read every markdown file listed below and any markdown files they reference inside the same plugin folder.

Markdown files to audit:
{{MARKDOWN_FILE_LIST}}

Rules:
- Operate read-only.
- Base findings on evidence from the files.
- Score each dimension as Rookie, Intermediate, or Mature.
- Provide prioritized recommendations as Must-add, Nice-to-have, and Consider removing.
- Include per-file notes for important issues and strengths.

Return the final result as a single markdown report following the exact structure defined by the `audit-plugin` skill.
