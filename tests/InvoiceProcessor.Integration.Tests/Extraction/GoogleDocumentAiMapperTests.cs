using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Infrastructure.Extraction.DocumentAi;

namespace InvoiceProcessor.Integration.Tests.Extraction;

// Golden-master tests for GoogleDocumentAiMapper against anonymised fake-data fixtures
// that mirror the Document AI Invoice Parser JSON shape (entities array + text property).
// The buyer company used throughout is "EMPRESA COMPRADORA C.B." with NIF "B00000000".
public sealed class GoogleDocumentAiMapperTests
{
    private const string OwnTaxId = "B00000000";
    private const string OwnName = "EMPRESA COMPRADORA C.B.";

    private static ExtractionResult Map(string fixtureName, string? ownTaxId = OwnTaxId, string? ownName = OwnName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures", "RealInvoices", "gcp",
            fixtureName + ".json");
        return GoogleDocumentAiMapper.ToExtractionResult(File.ReadAllText(path), ownTaxId, ownName);
    }

    private static string? Field(ExtractionResult r, string key) =>
        r.Fields.TryGetValue(key, out var f) ? f.Value : null;

    // Given a standard invoice with all top-level entities present
    // When mapped
    // Then all standard fields are extracted correctly
    [Fact]
    public void Maps_standard_invoice_fields()
    {
        var r = Map("demo_standard");

        Assert.Equal("FAK-2026-001", Field(r, "invoice_number"));
        Assert.Equal("2026-01-15", Field(r, "issue_date"));
        Assert.Equal("PROVEEDOR DEMO S.L.", Field(r, "supplier_name"));
        Assert.Equal("B00000001", Field(r, "supplier_tax_id"));
        Assert.Equal("EUR", Field(r, "currency"));
        Assert.Equal("21", Field(r, "tax_amount"));
        Assert.Equal("121", Field(r, "total_amount"));
        Assert.Equal("100", Field(r, "net_amount"));
    }

    // Given an invoice with a Spanish long-form date ("15 de enero de 2026")
    // and a bad normalizedValue that is not a full ISO date
    // When mapped
    // Then issue_date is correctly parsed from mentionText
    [Fact]
    public void Parses_spanish_long_form_date()
    {
        var r = Map("demo_spanish_date");

        Assert.Equal("2026-01-15", Field(r, "issue_date"));
    }

    // Given an invoice with a numeric date ("01/04/2026") and no normalizedValue
    // When mapped
    // Then issue_date is correctly parsed as YYYY-MM-DD
    [Fact]
    public void Parses_numeric_date_ddmmyyyy()
    {
        var r = Map("demo_numeric_date");

        Assert.Equal("2026-04-01", Field(r, "issue_date"));
    }

    // Given an invoice with no top-level net_amount or total_tax_amount entities
    // but a vat entity with typed sub-properties (vat/tax_amount, vat/amount)
    // When mapped
    // Then tax_amount is recovered from vat/tax_amount and net_amount = total - tax
    [Fact]
    public void Recovers_tax_from_vat_sub_property_and_derives_net()
    {
        var r = Map("demo_vat_recovery");

        Assert.Equal("63.17", Field(r, "tax_amount"));
        Assert.Equal("363.97", Field(r, "total_amount"));
        Assert.Equal("300.80", Field(r, "net_amount"));
    }

    // Given an invoice where the supplier_tax_id entity matches ownTaxId
    // When mapped with that ownTaxId
    // Then supplier_tax_id is absent from the result fields
    [Fact]
    public void Filters_out_own_tax_id_as_supplier()
    {
        var r = Map("demo_own_tax_filter", ownTaxId: OwnTaxId);

        Assert.False(r.Fields.ContainsKey("supplier_tax_id"));
    }

    // Given a JSON payload with no entities property
    // When mapped
    // Then result has empty fields and zero overall confidence
    [Fact]
    public void Empty_entities_returns_empty_result()
    {
        var r = Map("demo_empty");

        Assert.Empty(r.Fields);
        Assert.Equal(0m, r.OverallConfidence);
    }
}
