# Skill: commit

## Format

```
type(scope): short description in imperative English
```

- **type** — what kind of change (see table below)
- **scope** — optional, the area affected: `backend`, `frontend`, `agents`, `skills`, `workflow`
- **description** — imperative, lowercase, no period, max 72 chars

## Types

| Type       | When to use                                         |
|------------|-----------------------------------------------------|
| `feat`     | New behavior visible to the user or other agents    |
| `fix`      | Corrects a bug or wrong behavior                    |
| `chore`    | Setup, tooling, dependencies — no behavior change   |
| `docs`     | Documentation only                                  |
| `refactor` | Restructures code without changing behavior         |
| `test`     | Adds or modifies tests only                         |

## Rules

- One concept per commit — if you need "and", it's two commits
- Stage only the files relevant to that concept
- Never commit generated files (`bin/`, `obj/`, `node_modules/`, `dist/`)
- Never commit secrets or environment-specific config
- **Never add** `Co-Authored-By` lines, `🤖 Generated with [Claude Code](https://claude.com/claude-code)` footers, or any AI attribution in commit messages or PR bodies — not under any circumstance

## When to commit during TDD

```
test: add failing test for <behavior>       ← after Red
feat: implement <behavior> to pass test     ← after Green
refactor: clean up <area>                   ← after Refactor (only if there are changes)
```

## Examples

```
feat(backend): add GET /api/items endpoint
fix(frontend): correct API base URL in items page
chore: add gitattributes to normalize line endings
docs: add backend/AGENTS.md with .NET conventions
test(backend): add failing test for empty items list
refactor(backend): extract item validation to helper
feat(skills): add commit skill
```
