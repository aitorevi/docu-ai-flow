using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Invoices;
using NSubstitute;

namespace InvoiceProcessor.Application.Tests.Invoices;

public sealed class ExtractionToInvoiceMapperTests
{
    private readonly ISupplierNormalizer _supplierNormalizer = Substitute.For<ISupplierNormalizer>();

    public ExtractionToInvoiceMapperTests()
    {
        _supplierNormalizer.Normalize(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new Supplier("Repsol", "A78374725", null));
    }

    private static IReadOnlyDictionary<string, ExtractedField> ValidFields() =>
        new Dictionary<string, ExtractedField>
        {
            ["invoice_number"] = new("F2026-0042", 0.95m),
            ["issue_date"] = new("2026-01-15", 0.92m),
            ["due_date"] = new("2026-02-15", 0.85m),
            ["net_amount"] = new("100.00", 0.90m),
            ["tax_amount"] = new("21.00", 0.89m),
            ["total_amount"] = new("121.00", 0.93m),
            ["currency"] = new("EUR", 0.99m),
            ["supplier_name"] = new("Repsol", 0.91m),
            ["supplier_tax_id"] = new("A78374725", 0.88m),
        };

    [Fact]
    public void Map_returns_success_with_valid_fields()
    {
        // Given
        var extraction = new ExtractionResult(ValidFields(), [], 0.9m);

        // When
        var result = ExtractionToInvoiceMapper.Map(extraction, _supplierNormalizer);

        // Then
        Assert.True(result.IsSuccess);
        var invoice = result.Value;
        Assert.Equal("F2026-0042", invoice.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 1, 15), invoice.IssueDate);
        Assert.Equal(new DateOnly(2026, 2, 15), invoice.DueDate);
        Assert.Equal(100.00m, invoice.NetAmount.Amount);
        Assert.Equal(21.00m, invoice.TaxAmount.Amount);
        Assert.Equal(121.00m, invoice.TotalAmount.Amount);
        Assert.Equal("EUR", invoice.TotalAmount.Currency);
    }

    [Fact]
    public void Map_returns_failure_when_invoice_number_missing()
    {
        // Given
        var fields = ValidFields().ToDictionary();
        fields.Remove("invoice_number");
        var extraction = new ExtractionResult(fields, [], 0.9m);

        // When
        var result = ExtractionToInvoiceMapper.Map(extraction, _supplierNormalizer);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Map_returns_failure_when_date_unparseable()
    {
        // Given
        var fields = ValidFields().ToDictionary();
        fields["issue_date"] = new ExtractedField("not-a-date", 0.92m);
        var extraction = new ExtractionResult(fields, [], 0.9m);

        // When
        var result = ExtractionToInvoiceMapper.Map(extraction, _supplierNormalizer);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Map_returns_failure_when_total_incoherent()
    {
        // Given: net + tax = 100 + 21 = 121 but total is 130
        var fields = ValidFields().ToDictionary();
        fields["total_amount"] = new ExtractedField("130.00", 0.93m);
        var extraction = new ExtractionResult(fields, [], 0.9m);

        // When
        var result = ExtractionToInvoiceMapper.Map(extraction, _supplierNormalizer);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Map_returns_failure_when_confidence_below_threshold()
    {
        // Given: overall confidence below 0.6
        var extraction = new ExtractionResult(ValidFields(), [], 0.5m);

        // When
        var result = ExtractionToInvoiceMapper.Map(extraction, _supplierNormalizer);

        // Then
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public void Map_respects_custom_confidence_threshold()
    {
        // Given: confidence 0.8 passes the default 0.6 but must fail a custom 0.9 threshold
        var extraction = new ExtractionResult(ValidFields(), [], 0.8m);

        // When — passing a higher threshold
        var result = ExtractionToInvoiceMapper.Map(extraction, _supplierNormalizer, confidenceThreshold: 0.9m);

        // Then: should fail because 0.8 < 0.9
        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }
}
