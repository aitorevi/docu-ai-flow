# Skill: orchestrate

Flujo obligatorio para toda tarea de desarrollo. El orquestador no salta pasos.

## Fases

### 1. EXPLORACIÓN

Haz preguntas al humano hasta entender completamente el problema.
No asumas. No propongas soluciones todavía.

Preguntas mínimas:
- ¿Qué comportamiento exacto se espera?
- ¿Hay restricciones técnicas conocidas?
- ¿Afecta a backend, frontend o ambos?

### 2. DEBATE

Propón enfoques alternativos con sus trade-offs.
Itera hasta obtener **acuerdo explícito** del humano sobre el enfoque a seguir.
Sin acuerdo explícito, no avanzas.

### 3. PLANIFICACIÓN

Según el alcance:
- Solo backend → invoca `planner-backend`
- Solo frontend → invoca `planner-frontend`
- Fullstack → invoca ambos, coordina los planes

Persiste el plan en `workflow/plans/<nombre-feature>.md`.
Formato del plan: sigue `skills/plan-to-md.md`.

### 4. APROBACIÓN

Presenta el plan al humano.
- Si hay correcciones → vuelve a los planners
- Si aprueba explícitamente → mueve el plan a `workflow/in-progress/`

**Nunca muevas un plan a `in-progress/` sin aprobación explícita.**

### 5. IMPLEMENTACIÓN

Según el plan:
- Backend → invoca `coder-backend`
- Frontend → invoca `coder-frontend`
- Fullstack → backend primero, frontend después

Los coders hacen commits atómicos durante la implementación.
Al terminar, el coder mueve el plan a `workflow/reviewing/`.

### 6. REVISIÓN

Invoca `reviewer`.
El reviewer audita contra el plan aprobado y las convenciones.

- Si hay issues críticos → reviewer mueve el plan a `workflow/in-progress/`, vuelve a paso 5
- Si todo correcto → continúa

### 7. VALIDACIÓN

Presenta el informe del reviewer al humano.
- Si el humano aprueba → commit final si aplica + push
- Mueve el plan a `workflow/done/`

## Cuándo invocar a cada agente

| Situación | Agentes |
|---|---|
| Lógica de servidor, endpoints, datos | `planner-backend` → `coder-backend` |
| UI, páginas, componentes | `planner-frontend` → `coder-frontend` |
| Feature completa con API y UI | ambos planners → ambos coders |
