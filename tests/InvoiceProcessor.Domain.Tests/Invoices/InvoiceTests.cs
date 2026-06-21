using InvoiceProcessor.Domain;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Domain.Tests.Invoices;

public sealed class InvoiceTests
{
    private static readonly Supplier DefaultSupplier = new("Repsol", "A78374725", null);
    private static readonly DateOnly DefaultDate = new(2026, 1, 15);

    [Fact]
    public void Create_WithEmptyInvoiceNumber_ReturnsFailure()
    {
        // Given
        var net = new Money(100m, "EUR");
        var tax = new Money(21m, "EUR");
        var total = new Money(121m, "EUR");

        // When
        var result = Invoice.Create(
            InvoiceId.New(), "", DefaultSupplier, DefaultDate, null, net, tax, total, []);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }

    [Fact]
    public void Create_WithIncoherentTotal_ReturnsFailure()
    {
        // Given
        var net = new Money(100m, "EUR");
        var tax = new Money(21m, "EUR");
        var total = new Money(200m, "EUR");  // wrong: 100 + 21 ≠ 200

        // When
        var result = Invoice.Create(
            InvoiceId.New(), "F-001", DefaultSupplier, DefaultDate, null, net, tax, total, []);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error!.Code);
    }

    [Fact]
    public void Create_WithCoherentTotal_ReturnsSuccess()
    {
        // Given
        var net = new Money(100m, "EUR");
        var tax = new Money(21m, "EUR");
        var total = new Money(121m, "EUR");

        // When
        var result = Invoice.Create(
            InvoiceId.New(), "F-001", DefaultSupplier, DefaultDate, null, net, tax, total, []);

        // Then
        Assert.True(result.IsSuccess);
        Assert.Equal("F-001", result.Value!.InvoiceNumber);
    }
}
