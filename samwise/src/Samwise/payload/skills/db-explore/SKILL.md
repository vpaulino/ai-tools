---
name: db-explore
description: Inspect a database (schema, columns, sample rows) through whatever SQL/database MCP server is connected. Use when the user asks about a table/entity, wants to see sample data, check a column, or validate a data assumption against the database.
---

# db-explore

Read-only exploration of a database via a connected **SQL/database MCP server** (e.g. an Azure Data API Builder HTTP MCP, or any database MCP that exposes query/entity tools).

## Guardrails
- **Read-only by default.** Don't issue inserts/updates/deletes unless the user explicitly asks and the server permits writes.
- Assume the target may be a shared/dev database — don't dump huge result sets; default to a small `limit` (e.g. 5–20 rows).

## Steps
1. **Find the database MCP.** List the connected MCP tools and pick the one exposing schema/query/entity operations. If more than one could match, confirm with the user.
2. **Resolve the entity/table** the user means. If ambiguous, search/list the available entities and confirm before querying.
3. To **describe schema** — return columns + types (and a single sample row only if that's the only way to infer shape).
4. To **sample data** — fetch the requested table with a small row limit; show key columns, not every field, unless asked.
5. Present results as a compact table. Note the row count and that it's capped.

## Tips
- If you don't know the table names, ask the server to list entities/tables first rather than guessing.
- If the server is disconnected, surface that plainly and suggest the user check that the MCP (and any backing container/service) is running.
