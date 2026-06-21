# Rules y Hooks en opencode — realidad vs nomenclatura

El plan original usaba los términos "rules" y "hooks" como si fueran entidades propias de opencode. La realidad es más sencilla y más honesta.

---

## Lo que opencode llama "rules"

En opencode no hay un sistema de archivos de reglas separado. Las reglas son simplemente **instrucciones en texto** que el agente lee al inicio de cada sesión. Hay dos formas de definirlas:

### 1. AGENTS.md

El fichero `AGENTS.md` de cada directorio es el lugar principal para instrucciones. opencode lo lee automáticamente cuando trabaja en ese directorio.

```
AGENTS.md          → instrucciones globales del proyecto
backend/AGENTS.md  → instrucciones específicas del backend
frontend/AGENTS.md → instrucciones específicas del frontend
```

### 2. opencode.json — campo `instructions`

Puedes cargar ficheros de instrucciones adicionales desde `opencode.json`:

```json
{
  "instructions": [
    "docs/guidelines.md",
    ".opencode/skills/commit.md"
  ]
}
```

Esto añade esos ficheros al contexto del agente en cada sesión. Útil para referenciar skills o guías externas sin duplicar contenido en AGENTS.md.

**Cuándo usar uno u otro:**
- `AGENTS.md` → contexto del proyecto, convenciones, comandos
- `instructions` en `opencode.json` → cargar skills o documentos de referencia de forma automática

---

## Lo que opencode llama "hooks"

opencode **no tiene un sistema propio de hooks**. Lo que el plan describía como hooks son dos cosas distintas:

### 1. Git hooks nativos

Son scripts que git ejecuta en momentos concretos del flujo de trabajo. Viven en `.git/hooks/` y git los llama automáticamente.

```
.git/hooks/pre-commit   → se ejecuta antes de cada git commit
.git/hooks/commit-msg   → valida el mensaje del commit
```

Estos no se versionan (`.git/` no está en el repo). Para compartirlos con el equipo se usa una carpeta como `.githooks/` y se configura git para que los use:

```bash
git config core.hooksPath .githooks
```

### 2. Instrucciones de comportamiento en agentes/skills

Lo que el plan llamaba "hook post-plan" (verificar que el plan está en `workflow/plans/` antes de implementar) no es un hook real — es una instrucción en la skill `orchestrate.md` que el agente debe seguir.

**La diferencia clave:**
- Git hook → código que se ejecuta automáticamente, el agente no interviene
- Instrucción en skill → el agente lee y aplica, pero puede no hacerlo si el contexto se pierde

---

## Mapa real de implementación

| Lo que queremos | Cómo se implementa realmente |
|---|---|
| No commitear a `main` | Git hook `pre-commit` que comprueba la rama |
| Tests obligatorios para endpoints | Instrucción en `AGENTS.md` + skill `tdd.md` |
| Commits en inglés | Instrucción en `AGENTS.md` + skill `commit.md` |
| No mover plan sin aprobación | Instrucción en `orchestrate.md` (skill) |
| Lint/format antes de commit | Git hook `pre-commit` real |

---

## Plugins de opencode

opencode sí tiene un sistema de plugins (`.opencode/plugins/`) que puede extender herramientas y añadir integraciones. Pero no son equivalentes a hooks — son extensiones del agente, no automatismos del flujo git.
