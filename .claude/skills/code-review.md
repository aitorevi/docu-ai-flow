# Skill: code-review

Checklist para el reviewer. El objetivo no es encontrar defectos estéticos — es garantizar que el código hace lo que el plan dice y que lo hará mañana también.

## Checklist

### 1. Fidelidad al plan

- [ ] El código implementa exactamente lo que el plan describe
- [ ] No hay funcionalidad extra no planificada ("gold plating")
- [ ] Los criterios de aceptación del plan están cubiertos

### 2. Tests

- [ ] Existe al menos un test por comportamiento implementado
- [ ] Los nombres de test hablan de negocio (ver `docs/references/tdd/test-naming.md`)
- [ ] Los tests fallarían si se elimina la implementación
- [ ] Estructura Given / When / Then presente o implícita

### 3. Convenciones

- [ ] Naming sigue las convenciones del directorio (ver AGENTS.md correspondiente)
- [ ] Commits atómicos y mensajes semánticos (ver `skills/commit.md`)
- [ ] Sin código comentado
- [ ] Sin TODOs sin contexto

### 4. Manejo de errores

- [ ] Los errores están manejados explícitamente, no silenciados
- [ ] Los endpoints devuelven códigos HTTP correctos
- [ ] Las validaciones de entrada están presentes

### 5. Calidad general

- [ ] El código es legible sin necesidad de comentarios
- [ ] No hay duplicación obvia
- [ ] Las dependencias son las mínimas necesarias

## Output del reviewer

```markdown
## Veredicto: APROBADO | RECHAZADO

### Issues críticos (bloquean el merge)
- ...

### Issues menores (recomendaciones)
- ...
```

Si hay issues críticos → el plan vuelve a `workflow/in-progress/`.
Si solo hay issues menores → el humano decide si aprobar o pedir correcciones.
