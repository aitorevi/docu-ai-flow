using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Extraction.Ocr;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Integration.Tests.Fixtures;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InvoiceProcessor.Integration.Tests.Extraction.Templates;

// Tests for the OCR fallback: when PdfPig recovers no text layer (a scan), the extractor
// asks IOcrTextExtractor for text and, if it gets some, runs the normal template pipeline
// on it. Any OCR failure degrades to manual entry. OCR must never run for native-text PDFs.
public sealed class TemplateInvoiceExtractorOcrTests
{
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
                    ["net_amount"]     = new() { Anchors = ["Base"],       Pattern = @":\s*([\d.,]+)" },
                    ["tax_amount"]     = new() { Anchors = ["IVA 21%"],    Pattern = @":\s*([\d.,]+)" },
                    ["total_amount"]   = new() { Anchors = ["Total"],      Pattern = @":\s*([\d.,]+)" },
                }
            }
        ]
    };

    private const string AcmeInvoiceText =
        "ACME S.A.  CIF A12345678\n" +
        "Nº Factura: FAC-2025-001\n" +
        "Fecha: 15/03/2025\n" +
        "Base imponible: 1.000,00\n" +
        "IVA 21%: 210,00\n" +
        "Total a pagar: 1.210,00\n";

    private static TemplateInvoiceExtractor Build(IOcrTextExtractor ocr)
    {
        var repo = new AppsettingsTemplateRepository(Options.Create(AcmeOptions));
        return new TemplateInvoiceExtractor(Options.Create(AcmeOptions), repo, ocr);
    }

    private static DocumentContent ToContent(byte[] bytes) =>
        new("invoice.pdf", "application/pdf", new MemoryStream(bytes));

    // ── Scan + OCR returns valid text → fields extracted ─────────────────────

    [Fact]
    public async Task ExtractAsync_ScanPdf_OcrEnabled_MockReturnsValidText_ExtractsFields()
    {
        var ocr = Substitute.For<IOcrTextExtractor>();
        ocr.ExtractTextAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
           .Returns(AcmeInvoiceText);
        var extractor = Build(ocr);

        var result = await extractor.ExtractAsync(ToContent(SyntheticPdf.WithNoText()), CancellationToken.None);

        Assert.False(result.RequiresManualEntry);
        Assert.True(result.SourcedFromOcr);
        Assert.Equal("FAC-2025-001", result.Fields["invoice_number"].Value);
        Assert.Equal("2025-03-15", result.Fields["issue_date"].Value);
        Assert.Equal("1000.00", result.Fields["net_amount"].Value);
    }

    // ── Scan + OCR returns empty → manual entry ──────────────────────────────

    [Fact]
    public async Task ExtractAsync_ScanPdf_OcrEnabled_MockReturnsEmpty_ReturnsManualEntry()
    {
        var ocr = Substitute.For<IOcrTextExtractor>();
        ocr.ExtractTextAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
           .Returns(string.Empty);

        var result = await Build(ocr).ExtractAsync(ToContent(SyntheticPdf.WithNoText()), CancellationToken.None);

        Assert.True(result.RequiresManualEntry);
    }

    // ── Scan + OCR throws → manual entry (graceful degradation) ──────────────

    [Fact]
    public async Task ExtractAsync_ScanPdf_OcrEnabled_MockThrows_ReturnsManualEntry()
    {
        var ocr = Substitute.For<IOcrTextExtractor>();
        ocr.ExtractTextAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
           .Throws(new InvalidOperationException("ocr boom"));

        var result = await Build(ocr).ExtractAsync(ToContent(SyntheticPdf.WithNoText()), CancellationToken.None);

        Assert.True(result.RequiresManualEntry);
    }

    // ── Scan + OCR disabled (null object) → manual entry, as before ──────────

    [Fact]
    public async Task ExtractAsync_ScanPdf_OcrDisabled_NullOcr_ReturnsManualEntry()
    {
        var result = await Build(new NullOcrTextExtractor())
            .ExtractAsync(ToContent(SyntheticPdf.WithNoText()), CancellationToken.None);

        Assert.True(result.RequiresManualEntry);
    }

    // ── Native-text PDF → OCR is never consulted ─────────────────────────────

    [Fact]
    public async Task ExtractAsync_NativeTextPdf_OcrEnabled_OcrNeverCalled()
    {
        var ocr = Substitute.For<IOcrTextExtractor>();
        var extractor = Build(ocr);

        var result = await extractor.ExtractAsync(ToContent(SyntheticPdf.WithText(AcmeInvoiceText)), CancellationToken.None);

        Assert.False(result.RequiresManualEntry);
        Assert.False(result.SourcedFromOcr);
        Assert.Equal("FAC-2025-001", result.Fields["invoice_number"].Value);
        await ocr.DidNotReceive().ExtractTextAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }
}
