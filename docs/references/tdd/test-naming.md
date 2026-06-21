# Test Naming

El nombre del test es la primera documentación del comportamiento. Si el nombre habla de código, el test no documenta nada útil.

## Estructura

```
Should_<resultado esperado>_When_<condición o acción>
```

## Ejemplos

```csharp
// Correcto — habla de negocio
Should_RejectOrder_When_StockIsInsufficient()
Should_ApplyDiscount_When_CustomerIsVIP()
Should_SendConfirmationEmail_When_OrderIsPlaced()
Should_ReturnEmptyList_When_NoItemsExist()
Should_ReturnNotFound_When_ItemIdDoesNotExist()

// Evitar — habla de implementación
Test_CheckStock()
ValidateOrder_Returns_False()
GetItems_ReturnsOk()
```

## Reglas

1. **Habla de negocio, no de código** — usa conceptos del dominio, no nombres de métodos
2. **El resultado va primero** — `Should_Reject` no `Should_Stock_Be_Insufficient`
3. **Condición concreta, no genérica** — `When_StockIsInsufficient` no `When_Invalid`
4. **Un test, una regla de negocio** — si el nombre tiene "And", son dos tests
5. **Tiempo presente** — `Should_Return`, no `Should_Have_Returned`

## Tests parametrizados

El parámetro `reason` es documentación viva — explica por qué ese valor es inválido:

```csharp
[Theory]
[InlineData(0,   "no items in cart")]
[InlineData(-1,  "negative quantity")]
[InlineData(999, "quantity exceeds warehouse limit")]
public void Should_RejectItem_When_QuantityIsInvalid(
    int quantity, string reason)
```

## Estructura interna: Given / When / Then

```csharp
public void Should_RejectOrder_When_StockIsInsufficient()
{
    // Given
    var stock = new Stock(availableUnits: 2);
    var order = new Order(requestedUnits: 5);

    // When
    var result = stock.CanFulfill(order);

    // Then
    Assert.False(result);
}
```
