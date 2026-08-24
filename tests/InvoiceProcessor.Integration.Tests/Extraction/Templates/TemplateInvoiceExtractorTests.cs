using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Integration.Tests.Fixtures;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Extraction.Templates;

// Tests for TemplateInvoiceExtractor using synthetic PDFs generated in-process.
// Real invoice PDFs are never checked into the repo; the fixture helpers build minimal
// PdfPig-compatible PDFs so we can verify the extraction logic end-to-end.
public sealed class TemplateInvoiceExtractorTests
{
    // A template that recognises ACME invoices: identified by a header anchor,
    // fields extracted with simple anchors + regex.
    private static readonly TemplateExtractorOptions AcmeOptions = new()
    {
        MinTextLength = 50,
        Templates =
        [
            new TemplateEntryOptions
            {
                SupplierId = "acme",
                SupplierName = "ACME S.A.",
                SupplierTaxId = "A12345678",
                IdentificationAnchors = ["ACME S.A.", "A12345678"],
                Fields = new Dictionary<string, FieldEntryOptions>
                {
                    ["invoice_number"] = new() { Anchors = ["Nº Factura"], Pattern = @":\s*(\S+)" },
                    ["issue_date"]     = new() { Anchors = ["Fecha"],      Pattern = @":\s*([\d/]+)" },
                    ["net_amount"]     = new() { Anchors = ["Base imponible", "Base"], Pattern = @":\s*([\d.,]+)" },
                    ["tax_amount"]     = new() { Anchors = ["IVA 21%"],    Pattern = @":\s*([\d.,]+)" },
                    ["total_amount"]   = new() { Anchors = ["Total a pagar", "Total"], Pattern = @":\s*([\d.,]+)" },
                }
            }
        ]
    };

    private static IInvoiceDataExtractor BuildExtractor(TemplateExtractorOptions opts)
    {
        var templateRepo = new AppsettingsTemplateRepository(Options.Create(opts));
        return new TemplateInvoiceExtractor(
            Options.Create(opts), templateRepo,
            new InvoiceProcessor.Infrastructure.Extraction.Ocr.NullOcrTextExtractor());
    }

    private static DocumentContent ToContent(byte[] bytes, string name = "invoice.pdf") =>
        new(name, "application/pdf", new MemoryStream(bytes));

    // ── Scenario: native-text PDF with matching template ─────────────────────

    [Fact]
    public async Task ExtractAsync_WithMatchingTemplate_ExtractsFieldsWithHighConfidence()
    {
        // Given — a PDF whose text contains the ACME identification anchor and field values.
        const string invoiceText =
            "ACME S.A.  CIF A12345678\n" +
            "Nº Factura: FAC-2025-001\n" +
            "Fecha: 15/03/2025\n" +
            "Base imponible: 1.000,00\n" +
            "IVA 21%: 210,00\n" +
            "Total a pagar: 1.210,00\n";

        var pdf = SyntheticPdf.WithText(invoiceText);
        var extractor = BuildExtractor(AcmeOptions);

        // When
        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        // Then
        Assert.False(result.RequiresManualEntry);
        Assert.True(result.OverallConfidence > 0.6m);
        Assert.Equal("FAC-2025-001", result.Fields["invoice_number"].Value);
        Assert.Equal("2025-03-15", result.Fields["issue_date"].Value);
        Assert.Equal("1000.00", result.Fields["net_amount"].Value);
        Assert.Equal("210.00", result.Fields["tax_amount"].Value);
        Assert.Equal("1210.00", result.Fields["total_amount"].Value);
        Assert.Equal(1m, result.Fields["invoice_number"].Confidence);
    }

    // ── Scenario: PDF with no text (scan) ────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_WithNoText_SetsRequiresManualEntryTrue()
    {
        // Given — a minimal PDF that contains no text content (simulates a scanned image).
        var pdf = SyntheticPdf.WithNoText();
        var extractor = BuildExtractor(AcmeOptions);

        // When
        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        // Then
        Assert.True(result.RequiresManualEntry);
        Assert.Equal(0m, result.OverallConfidence);
    }

    // ── Scenario: native-text PDF but no matching template ────────────────────

    [Fact]
    public async Task ExtractAsync_WithNoMatchingTemplate_SetsRequiresManualEntryTrue()
    {
        // Given — a PDF with plenty of text but no identification anchors for any template.
        const string invoiceText =
            "PROVEEDOR DESCONOCIDO S.L.\n" +
            "CIF B99999999\n" +
            "Nº Factura: X-0001\n" +
            "Total: 500,00\n";

        var pdf = SyntheticPdf.WithText(invoiceText);
        var extractor = BuildExtractor(AcmeOptions);

        // When
        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        // Then
        Assert.True(result.RequiresManualEntry);
        Assert.Equal(0m, result.OverallConfidence);
    }

    // ── Scenario: PDF with scarce text (below MinTextLength) ─────────────────

    [Fact]
    public async Task ExtractAsync_WithScarseText_SetsRequiresManualEntryTrue()
    {
        // Given — a PDF whose text is shorter than MinTextLength (50 chars by default).
        var pdf = SyntheticPdf.WithText("OK");   // 2 chars < 50
        var extractor = BuildExtractor(AcmeOptions);

        // When
        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        // Then
        Assert.True(result.RequiresManualEntry);
        Assert.Equal(0m, result.OverallConfidence);
    }

    // ── Scenario: supplier fields injected from template ─────────────────────

    [Fact]
    public async Task ExtractAsync_MatchingTemplate_InjectsSupplierNameAndTaxIdWithFullConfidence()
    {
        const string invoiceText =
            "ACME S.A. A12345678\n" +
            "Nº Factura: F-001\n" +
            "Fecha: 01/01/2026\n" +
            "Base imponible: 100,00\n" +
            "IVA 21%: 21,00\n" +
            "Total a pagar: 121,00\n";

        var pdf = SyntheticPdf.WithText(invoiceText);
        var extractor = BuildExtractor(AcmeOptions);

        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        Assert.Equal("ACME S.A.", result.Fields["supplier_name"].Value);
        Assert.Equal(1m, result.Fields["supplier_name"].Confidence);
        Assert.Equal("A12345678", result.Fields["supplier_tax_id"].Value);
        Assert.Equal(1m, result.Fields["supplier_tax_id"].Confidence);
    }

    // ── Scenario: template matches but required fields are absent ─────────────
    // A template may identify the supplier via its anchors but fail to extract
    // one or more required fields (invoice_number, issue_date, net_amount) —
    // for example when the actual label format in the PDF differs from the pattern.
    // In that case the extractor must return RequiresManualEntry = true so the
    // invoice lands in pending/ and is never routed to failed/.

    // ── Scenario: invoice number with internal spaces is normalised ───────────
    // Suppliers print invoice numbers with stray spaces (e.g. "SF 2603247"); those
    // spaces carry no meaning, so the extractor strips all whitespace.

    [Fact]
    public async Task ExtractAsync_NormalisesInvoiceNumber_StrippingInternalSpaces()
    {
        var opts = new TemplateExtractorOptions
        {
            MinTextLength = 50,
            Templates =
            [
                new TemplateEntryOptions
                {
                    SupplierId = "acme",
                    SupplierName = "ACME S.A.",
                    SupplierTaxId = "A12345678",
                    IdentificationAnchors = ["ACME S.A.", "A12345678"],
                    Fields = new Dictionary<string, FieldEntryOptions>
                    {
                        ["invoice_number"] = new() { Anchors = ["Nº Factura"], Pattern = @":\s*(.+)" },
                        ["issue_date"]     = new() { Anchors = ["Fecha"], Pattern = @":\s*([\d/]+)" },
                        ["net_amount"]     = new() { Anchors = ["Base"], Pattern = @":\s*([\d.,]+)" },
                        ["tax_amount"]     = new() { Anchors = ["IVA 21%"], Pattern = @":\s*([\d.,]+)" },
                        ["total_amount"]   = new() { Anchors = ["Total"], Pattern = @":\s*([\d.,]+)" },
                    }
                }
            ]
        };
        const string invoiceText =
            "ACME S.A.  CIF A12345678\n" +
            "Nº Factura: SF 2603247\n" +
            "Fecha: 15/03/2025\n" +
            "Base: 1.000,00\n" +
            "IVA 21%: 210,00\n" +
            "Total: 1.210,00\n";

        var pdf = SyntheticPdf.WithText(invoiceText);
        var extractor = BuildExtractor(opts);

        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        Assert.Equal("SF2603247", result.Fields["invoice_number"].Value);
    }

    [Fact]
    public async Task ExtractAsync_TemplateMatchesButMissingRequiredFields_SetsRequiresManualEntryTrue()
    {
        // Given — a PDF that contains the ACME identification anchor so the template
        // matches, but the field labels use a format the patterns do not recognise
        // (dots instead of a colon, as some suppliers print them).
        const string invoiceText =
            "ACME S.A.  CIF A12345678\n" +
            "Factura.......... FAC-DOTS-001\n" +  // no colon → invoice_number won't match
            "Fecha............ 15/03/2025\n" +    // no colon → issue_date won't match
            "Base............. 1.000,00\n";        // no colon → net_amount won't match

        var pdf = SyntheticPdf.WithText(invoiceText);
        var extractor = BuildExtractor(AcmeOptions);

        // When
        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        // Then — supplier was identified but required fields are missing → manual entry
        Assert.True(result.RequiresManualEntry,
            "A template that identifies the supplier but cannot extract required fields must set RequiresManualEntry = true.");
    }

    // ── Scenario: field pattern anchors on a line break, not just the next field ──
    // Some suppliers' amounts blocks require the pattern to stop
    // at a literal line break so it doesn't cross into the following line. The extractor
    // rebuilds lines internally and must always join them with "\n", never with the OS's
    // Environment.NewLine — otherwise this kind of pattern silently stops matching on
    // Windows (CRLF) even though it works on macOS/Linux (LF) with the exact same template.

    [Fact]
    public async Task ExtractAsync_ExtractsField_WhenPatternRequiresLineBreakBeforeMoreContent()
    {
        var opts = new TemplateExtractorOptions
        {
            MinTextLength = 50,
            Templates =
            [
                new TemplateEntryOptions
                {
                    SupplierId = "acme",
                    SupplierName = "ACME S.A.",
                    SupplierTaxId = "A12345678",
                    IdentificationAnchors = ["ACME S.A.", "A12345678"],
                    Fields = new Dictionary<string, FieldEntryOptions>
                    {
                        ["invoice_number"] = new() { Anchors = ["Nº Factura"], Pattern = @":\s*(\S+)" },
                        ["issue_date"]     = new() { Anchors = ["Fecha"], Pattern = @":\s*([\d/]+)" },
                        ["net_amount"]     = new() { Anchors = ["IMPORTES"], Pattern = @"[\s\S]*?\n([\d.,]+)\s+[\d.,]+\n" },
                        ["tax_amount"]     = new() { Anchors = ["IMPORTES"], Pattern = @"[\s\S]*?\n[\d.,]+\s+([\d.,]+)\n" },
                        ["total_amount"]   = new() { Anchors = ["Total"], Pattern = @":\s*([\d.,]+)" },
                    }
                }
            ]
        };
        const string invoiceText =
            "ACME S.A.  CIF A12345678\n" +
            "Nº Factura: F-777\n" +
            "Fecha: 01/06/2026\n" +
            "IMPORTES\n" +
            "1.000,00 210,00\n" +
            "Total: 1.210,00\n";

        var pdf = SyntheticPdf.WithText(invoiceText);
        var extractor = BuildExtractor(opts);

        var result = await extractor.ExtractAsync(ToContent(pdf), CancellationToken.None);

        Assert.Equal("1000.00", result.Fields["net_amount"].Value);
        Assert.Equal("210.00", result.Fields["tax_amount"].Value);
    }
}
