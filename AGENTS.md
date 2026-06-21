# Project context

Learning project to understand opencode progressively.
Each phase lives in its own branch and builds on the previous one.

## Folder structure

```
backend/    → .NET 10 Web API
frontend/   → Astro 6 static/SSR site
workflow/   → plan lifecycle (plans → in-progress → reviewing → done)
docs/       → technical references
.opencode/  → agents and skills
```

Each directory has its own AGENTS.md with specific context.

## Stack

| Layer    | Technology | Why                                      |
|----------|------------|------------------------------------------|
| Backend  | .NET 10    | Typed, fast, minimal API pattern         |
| Frontend | Astro 6    | Component islands, ships minimal JS      |

## Commit and branch conventions

Branch naming: `feat/`, `fix/`, `chore/`, `docs/` — one branch per concept, always from `main`.

Commit format: `type(scope): short description in imperative English`

Types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`

One concept per commit. Follow the skill defined in `.opencode/skills/commit.md`.

## Setup

After cloning, activate the git hooks:

```bash
git config core.hooksPath .githooks
```

## Rules — always enforced

- **Never commit directly to `main`** — always work on a feature branch
- **Commit messages in English** — follow `.opencode/skills/commit.md`
- **Every new endpoint requires at least one test** — follow `.opencode/skills/tdd.md`
- **Never move a plan to `in-progress/` without explicit human approval**

## How to invoke the orchestrator

Start every development task by calling `@orchestrator`.
It will explore, plan, implement, review and validate following the workflow in `.opencode/skills/orchestrate.md`.
