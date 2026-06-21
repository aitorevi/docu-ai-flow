# Skill: plan-to-md

Cómo estructurar un plan de feature en markdown. El plan es el contrato entre el humano, el planner y el coder.

## Ubicación

```
workflow/plans/<feature-name>.md
```

Naming: kebab-case, descriptivo, sin fechas ni números de versión.

Ejemplos:
```
workflow/plans/health-endpoint.md
workflow/plans/items-list.md
workflow/plans/user-authentication.md
```

## Formato estándar

```markdown
# Plan: <nombre de la feature>

## Context

Qué problema resuelve y por qué se hace ahora.
Una o dos frases máximo.

## Decisions

Decisiones técnicas tomadas y alternativas descartadas con su razón.

| Decision | Rationale |
|---|---|
| Usar Results.Ok() en lugar de objetos raw | Consistencia con el patrón minimal API |
| Sin base de datos por ahora | Scope limitado, se añade en siguiente iteración |

## Tasks

Lista ordenada de tareas concretas. Cada tarea es implementable por el coder sin ambigüedad.

- [ ] Tarea 1
- [ ] Tarea 2
- [ ] Tarea 3

## Acceptance criteria

Condiciones medibles que determinan si la feature está completa.

- [ ] `GET /api/items` devuelve 200 con array JSON
- [ ] Si no hay items, devuelve array vacío (no 404)
- [ ] Existe al menos un integration test por criterio
```

## Reglas

- Sin secciones vacías — si no hay decisiones relevantes, omite la sección
- Los criterios de aceptación son medibles y verificables, no subjetivos
- Las tareas deben ser lo suficientemente pequeñas para un commit cada una
- El plan no cambia una vez en `in-progress/` — si hay cambios, vuelve a `plans/`
