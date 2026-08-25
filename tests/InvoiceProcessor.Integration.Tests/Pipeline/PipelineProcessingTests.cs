using InvoiceProcessor.Integration.Tests.Fixtures;

namespace InvoiceProcessor.Integration.Tests.Pipeline;

public sealed class PipelineProcessingTests : IDisposable
{
    private readonly PipelineFixture _fx = new();

    // The supplier every canned extraction in the fixture belongs to.
    private const string SupplierTaxId = "B12345678";

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task NewSupplier_IsHeldForReviewInsteadOfBeingFiled()
    {
        // No supplier is trusted on day one, so nothing is filed unseen.
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var pdfPath = await _fx.PlaceInboxAsync("factura.pdf");

        var result = await _fx.ProcessAsync(pdfPath);

        Assert.True(result.Success);
        Assert.Equal("Pendiente de revisión", result.FailureReason);
        Assert.Empty(await _fx.GetAllInvoicesAsync());

        var pending = await _fx.PendingAsync();
        var held = Assert.Single(pending);
        Assert.Equal("F2026-001", held.InvoiceNumber);
        Assert.False(held.RequiresManualEntry);

        // The PDF is parked in pending/, not archived and not failed.
        Assert.False(File.Exists(pdfPath), "PDF should be moved out of inbox");
        Assert.Single(Directory.EnumerateFiles(_fx.PendingPath, "*.pdf"));
        Assert.Empty(Directory.EnumerateFiles(_fx.ArchivePath, "*.pdf", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task TrustedSupplier_InvoiceIsArchivedAndSaved()
    {
        await _fx.TrustSupplierAsync(SupplierTaxId);
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
        Assert.Empty(await _fx.PendingAsync());
    }

    [Fact]
    public async Task DuplicateInvoice_SecondRunIsSkipped()
    {
        await _fx.TrustSupplierAsync(SupplierTaxId);
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

        // And it left the inbox, or the watcher would re-poll it forever.
        Assert.False(File.Exists(pdf2));
        Assert.Single(Directory.EnumerateFiles(_fx.DuplicatesPath, "*.pdf"));
    }

    [Fact]
    public async Task ReIssuedInvoice_SameNumberAndTaxId_IsCaughtAsADuplicate()
    {
        // Different bytes, so the content hash misses it — but declaring it again would double
        // the quarter's VAT. The natural key (number + tax id) is what catches this.
        await _fx.TrustSupplierAsync(SupplierTaxId);
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var first = await _fx.PlaceInboxAsync("original.pdf", MinimalPdf.Bytes());
        await _fx.ProcessAsync(first);

        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);   // same invoice, re-sent
        byte[] differentBytes = [.. MinimalPdf.Bytes(), (byte)0xFF];
        var second = await _fx.PlaceInboxAsync("reenviada.pdf", differentBytes);

        var result = await _fx.ProcessAsync(second);

        Assert.Equal("Posible duplicado", result.FailureReason);
        Assert.Single(await _fx.GetAllInvoicesAsync());
        Assert.Single(Directory.EnumerateFiles(_fx.DuplicatesPath, "*.pdf"));
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
    public async Task Unreadable_IsHeldForManualEntryNotFailed()
    {
        _fx.StubExtraction(PipelineFixture.RequiresManualEntry);
        var pdfPath = await _fx.PlaceInboxAsync("escaneo.pdf");

        var result = await _fx.ProcessAsync(pdfPath);

        Assert.True(result.Success);
        Assert.Equal("Alta manual requerida", result.FailureReason);

        var held = Assert.Single(await _fx.PendingAsync());
        Assert.True(held.RequiresManualEntry);
        Assert.Equal(string.Empty, held.InvoiceNumber);   // blank form for a human to fill in
        Assert.Empty(Directory.EnumerateFiles(_fx.FailedPath, "*.pdf"));
        Assert.Single(Directory.EnumerateFiles(_fx.PendingPath, "*.pdf"));
    }

    [Fact]
    public async Task BusinessRules_QuarterAssignment()
    {
        await _fx.TrustSupplierAsync(SupplierTaxId);
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
        await _fx.TrustSupplierAsync(SupplierTaxId);

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
