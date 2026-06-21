# Skill: create-endpoint

Patrón para crear endpoints en .NET 10 minimal API. Seguir este patrón garantiza consistencia entre todos los endpoints del proyecto.

## Estructura en Program.cs

```csharp
// Agrupación por recurso
var items = app.MapGroup("/api/items");

items.MapGet("/", GetAllItems);
items.MapGet("/{id:int}", GetItemById);
```

## Patrón de handler

Cada endpoint es un método estático separado, no una lambda inline:

```csharp
static async Task<IResult> GetAllItems()
{
    // lógica aquí
    return Results.Ok(items);
}

static async Task<IResult> GetItemById(int id)
{
    var item = FindItem(id);
    if (item is null) return Results.NotFound();
    return Results.Ok(item);
}
```

## Códigos de respuesta estándar

| Situación | Resultado |
|---|---|
| Éxito con datos | `Results.Ok(data)` |
| Creado | `Results.Created($"/api/items/{id}", data)` |
| Sin contenido | `Results.NoContent()` |
| No encontrado | `Results.NotFound()` |
| Petición inválida | `Results.BadRequest("mensaje descriptivo")` |
| Error inesperado | `Results.Problem("mensaje")` |

## Ciclo TDD para un endpoint nuevo

### Red — integration test primero

```csharp
[Fact]
public async Task Should_ReturnEmptyList_When_NoItemsExist()
{
    // Given
    var client = _factory.CreateClient();

    // When
    var response = await client.GetAsync("/api/items");

    // Then
    response.EnsureSuccessStatusCode();
    var items = await response.Content.ReadFromJsonAsync<List<Item>>();
    Assert.Empty(items);
}
```

### Green — endpoint mínimo

```csharp
app.MapGet("/api/items", () => Results.Ok(Array.Empty<object>()));
```

### Refactor — estructura limpia con MapGroup y handler separado

## Validación de entrada

Siempre valida antes de procesar:

```csharp
static IResult CreateItem(ItemRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name))
        return Results.BadRequest("Name is required");

    // procesar...
}
```

## Commits durante la implementación

```
test: add failing test for GET /api/items
feat(backend): add GET /api/items endpoint
refactor(backend): extract items handler to separate method
```
