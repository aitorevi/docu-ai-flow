using InvoiceProcessor.Domain;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Domain.Tests.Invoices;

public sealed class MoneyTests
{
    [Fact]
    public void Create_WithEmptyCurrency_ReturnsFailure()
    {
        // Given / When
        var result = Money.Create(10m, "");

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }

    [Fact]
    public void Create_WithNonIsoCurrency_ReturnsFailure()
    {
        // Given / When
        var result = Money.Create(10m, "EU");

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }

    [Fact]
    public void Create_WithValidCurrency_ReturnsSuccess()
    {
        // Given / When
        var result = Money.Create(100m, "EUR");

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value!.Amount);
        Assert.Equal("EUR", result.Value.Currency);
    }

    [Fact]
    public void Add_WithSameCurrency_ReturnsSummedAmount()
    {
        // Given
        var a = new Money(10m, "EUR");
        var b = new Money(5m, "EUR");

        // When
        var result = a.Add(b);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal(15m, result.Value!.Amount);
        Assert.Equal("EUR", result.Value.Currency);
    }

    [Fact]
    public void Add_WithDifferentCurrencies_ReturnsFailure()
    {
        // Given
        var a = new Money(10m, "EUR");
        var b = new Money(5m, "USD");

        // When
        var result = a.Add(b);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }
}
