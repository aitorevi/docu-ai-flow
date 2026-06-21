# Skill: test-strategy

Cuándo usar qué tipo de test. La cobertura no es el objetivo — la confianza en el comportamiento sí.

## Tipos de test

### Unit test

Prueba una unidad de lógica aislada, sin dependencias externas.

Cuándo usarlo:
- Lógica de negocio pura (validaciones, cálculos, transformaciones)
- Casos límite y edge cases
- Todo lo que puedas probar sin levantar un servidor

```csharp
// Backend — xUnit
public void Should_RejectOrder_When_StockIsInsufficient()
```

### Integration test

Prueba que varias piezas funcionan juntas, incluyendo infraestructura real o simulada.

Cuándo usarlo:
- Endpoints HTTP completos (request → response)
- Acceso a base de datos
- Interacción entre capas

```csharp
// Backend — WebApplicationFactory
public async Task Should_ReturnItems_When_GetApiItems()
```

### End-to-end test

Prueba el flujo completo desde el navegador hasta el backend.

Cuándo usarlo:
- Flujos críticos de usuario (solo los más importantes)
- No para cada feature — son lentos y frágiles

## Regla de proporciones

```
Unit tests      → muchos, rápidos, baratos
Integration     → los necesarios para cada endpoint
E2E             → solo flujos críticos
```

## Cobertura mínima por tipo de componente

| Componente | Tipo recomendado | Cobertura mínima |
|---|---|---|
| Lógica de negocio | Unit | Todo el comportamiento |
| Endpoint .NET | Integration | Happy path + error principal |
| Página Astro | Manual o E2E | Solo flujos críticos |
| Utilidad/lógica frontend | Unit (Vitest) | Todo el comportamiento |
| Componente Astro | E2E (Playwright) | Solo flujos críticos |

## Organización de tests en .NET

```
backend.Tests/          → proyecto de tests separado
├── Unit/               → tests unitarios
└── Integration/        → tests de integración con WebApplicationFactory
```

## Organización de tests en Astro

```
frontend/src/tests/     → tests Vitest
├── utils/              → tests de funciones de utilidad
└── types/              → tests de type guards y transformaciones
```

```bash
# Comandos Vitest
npm run test            # ejecutar una vez
npm run test:watch      # modo watch
npm run test:coverage   # con cobertura
```

**Qué testear con Vitest:** funciones puras en `src/utils/`, type guards, transformaciones de datos.
**Qué NO testear con Vitest:** páginas Astro completas, llamadas HTTP, renderizado de componentes — eso es E2E.
