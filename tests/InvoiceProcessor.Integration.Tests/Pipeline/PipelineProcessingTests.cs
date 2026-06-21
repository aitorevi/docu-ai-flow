using InvoiceProcessor.Integration.Tests.Fixtures;

namespace InvoiceProcessor.Integration.Tests.Pipeline;

public sealed class PipelineProcessingTests : IDisposable
{
    private readonly PipelineFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task HappyPath_InvoiceIsArchivedAndSaved()
    {
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var pdfPath = await _fx.PlaceInboxAsync("factura.pdf");

        var result = await _fx.ProcessAsync(pdfPath);

        Assert.True(result.Success);
        Assert.NotNull(result.InvoiceId);

        var invoices = await _fx.GetAllInvoicesAsync();
        Assert.Single(invoices);
        Assert.Equal("F2026-001", invoices[0].InvoiceNumber);
        Assert.Equal("Test Supplier S.L.", invoices[0].SupplierName);

        Assert.False(File.Exists(pdfPath), "PDF should be moved out of inbox");
        var archived = Directory.EnumerateFiles(_fx.ArchivePath, "*.pdf", SearchOption.AllDirectories).ToList();
        Assert.Single(archived);
        // "Test Supplier S.L." → CanonicalizeSupplierName strips suffix, lowercases, hyphens → "test-supplier"
        Assert.Contains("test-supplier", archived[0]);
    }

    [Fact]
    public async Task DuplicateInvoice_SecondRunIsSkipped()
    {
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var pdf1 = await _fx.PlaceInboxAsync("a.pdf");

        var first = await _fx.ProcessAsync(pdf1);
        Assert.True(first.Success);

        // Same bytes → same hash — duplicate path short-circuits before calling the extractor
        var pdf2 = await _fx.PlaceInboxAsync("b.pdf");

        var second = await _fx.ProcessAsync(pdf2);
        Assert.True(second.Success);
        Assert.Null(second.InvoiceId); // skipped — no new invoice created

        var invoices = await _fx.GetAllInvoicesAsync();
        Assert.Single(invoices); // still only 1 row
    }

    [Fact]
    public async Task LowConfidence_MovedToFailed()
    {
        _fx.StubExtraction(PipelineFixture.LowConfidence);
        var pdfPath = await _fx.PlaceInboxAsync("bad.pdf");

        var result = await _fx.ProcessAsync(pdfPath);

        Assert.False(result.Success);
        Assert.Contains("Confianza", result.FailureReason);

        var invoices = await _fx.GetAllInvoicesAsync();
        Assert.Empty(invoices);

        Assert.False(File.Exists(pdfPath), "PDF should be moved out of inbox");
        var failed = Directory.EnumerateFiles(_fx.FailedPath, "*.pdf", SearchOption.AllDirectories).ToList();
        Assert.Single(failed);
    }

    [Fact]
    public async Task BusinessRules_QuarterAssignment()
    {
        // Issue date: 2026-04-01 → Q2 2026
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var pdfPath = await _fx.PlaceInboxAsync("q2.pdf");

        await _fx.ProcessAsync(pdfPath);

        var invoices = await _fx.GetAllInvoicesAsync();
        Assert.Single(invoices);
        var quarter = invoices[0].RealQuarter;
        Assert.Equal(2026, quarter.Year);
        Assert.Equal(2, quarter.Number);
    }

    [Fact]
    public async Task BusinessRules_MultipleInvoices_SameSupplier()
    {
        // Two PDFs with different bytes (→ different hashes) but same supplier
        var bytesA = MinimalPdf.Bytes();
        byte[] bytesB = [.. MinimalPdf.Bytes(), (byte)0xFF];

        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var pdf1 = await _fx.PlaceInboxAsync("inv-a.pdf", bytesA);
        var res1 = await _fx.ProcessAsync(pdf1);
        Assert.True(res1.Success);

        // Stub a different result for the second invoice (different invoice number)
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2B);
        var pdf2 = await _fx.PlaceInboxAsync("inv-b.pdf", bytesB);
        var res2 = await _fx.ProcessAsync(pdf2);
        Assert.True(res2.Success);

        var invoices = await _fx.GetAllInvoicesAsync();
        Assert.Equal(2, invoices.Count);
        Assert.Contains(invoices, i => i.InvoiceNumber == "F2026-001");
        Assert.Contains(invoices, i => i.InvoiceNumber == "F2026-002");

        // Both archived under the same supplier folder (suffix stripped, lowercased, hyphens)
        var supplierDir = Path.Combine(_fx.ArchivePath, "2026", "04", "test-supplier");
        var archivedFiles = Directory.EnumerateFiles(supplierDir, "*.pdf").ToList();
        Assert.Equal(2, archivedFiles.Count);
    }
}
