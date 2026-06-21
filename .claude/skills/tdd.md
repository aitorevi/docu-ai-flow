# Skill: tdd

Ciclo obligatorio para toda implementación de comportamiento nuevo.

## Referencias

- Conceptos: `docs/references/tdd/red-green-refactor.md`
- Naming: `docs/references/tdd/test-naming.md`

## El ciclo

### 1. Red — escribe el test primero

```csharp
// Given — contexto y precondiciones
// When  — la acción que se ejecuta
// Then  — el resultado esperado
```

El test debe **fallar por la razón correcta**:
- Si falla porque el tipo no existe → crea el tipo mínimo para compilar
- Si falla porque el método no existe → crea el método vacío
- Si falla porque el resultado es incorrecto → estás en Red válido

Commit tras Red:
```
test: add failing test for <behavior>
```

### 2. Green — mínimo código para pasar

- No anticipes casos futuros
- El código más simple posible, aunque sea feo
- Si necesitas hardcodear, hazlo — lo limpiarás en Refactor

Commit tras Green:
```
feat: implement <behavior> to pass test
```

### 3. Refactor — mejora sin romper

- El test siempre en verde durante el refactor
- Si un test se rompe, deshaz el cambio — no lo fuerces
- Elimina duplicación, mejora nombres, extrae métodos

Commit tras Refactor (solo si hay cambios reales):
```
refactor: clean up <area>
```

## Cuándo aplica esta skill

- Toda feature nueva con lógica de negocio
- Todo endpoint nuevo en el backend
- No aplica a: configuración, scaffolding, archivos estáticos
