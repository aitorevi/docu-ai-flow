# Skill: branch

## Nomenclatura

```
<type>/<short-description-in-kebab-case>
```

| Tipo | Cuándo |
|---|---|
| `feat/` | Feature nueva o extensión de funcionalidad |
| `fix/` | Corrección de bug |
| `chore/` | Setup, tooling, dependencias |
| `docs/` | Solo documentación |

Ejemplos:
```
feat/health-endpoint
feat/items-list
fix/cors-header-missing
chore/add-eslint
docs/api-reference
```

## Reglas

- Siempre desde `main` — nunca desde otra feature branch
- Una rama por concepto — si necesitas "y", son dos ramas
- Nombre en inglés, kebab-case, sin números de ticket
- Nunca commitear directamente a `main`

## Flujo estándar

```
git checkout main
git pull
git checkout -b feat/my-feature

# ... trabajo y commits atómicos ...

# cuando esté listo: pull request o merge a main
```
