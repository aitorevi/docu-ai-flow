using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Integration.Tests.Fixtures;

namespace InvoiceProcessor.Integration.Tests.Pipeline;

// The human-in-the-loop, end to end and through the real SQLite, filesystem and DI graph.
// The fixture sets Extraction:SupplierTrustThreshold = 3 so a test can actually walk a supplier
// from "everything gets reviewed" to "this one files itself".
public sealed class PipelineReviewTests : IDisposable
{
    private readonly PipelineFixture _fx = new();

    private const string SupplierTaxId = "B12345678";

    public void Dispose() => _fx.Dispose();

    private async Task<PendingInvoice> ProcessAndGetPendingAsync(ExtractionResult extraction, string fileName, byte[]? bytes = null)
    {
        _fx.StubExtraction(extraction);
        var path = await _fx.PlaceInboxAsync(fileName, bytes);
        await _fx.ProcessAsync(path);
        return (await _fx.PendingAsync()).Single(p => p.OriginalFileName == fileName);
    }

    [Fact]
    public async Task Confirming_MovesThePdfFromPendingToArchiveAndRecordsTheInvoice()
    {
        var pending = await ProcessAndGetPendingAsync(PipelineFixture.ValidInvoiceQ2, "factura.pdf");
        Assert.True(File.Exists(pending.PendingPath));

        var result = await _fx.ConfirmAsync(pending);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasModified);

        var stored = Assert.Single(await _fx.GetAllInvoicesAsync());
        Assert.Equal("F2026-001", stored.InvoiceNumber);

        Assert.False(File.Exists(pending.PendingPath), "the PDF should have left pending/");
        Assert.Single(Directory.EnumerateFiles(_fx.ArchivePath, "*.pdf", SearchOption.AllDirectories));
        Assert.Empty(await _fx.PendingAsync());
    }

    [Fact]
    public async Task ThreeCleanConfirmations_EarnTheSupplierItsAutonomy()
    {
        // 1 and 2: reviewed, counter climbing, still not trusted.
        var first = await ProcessAndGetPendingAsync(PipelineFixture.ValidInvoiceQ2, "a.pdf", MinimalPdf.Bytes());
        Assert.False((await _fx.ConfirmAsync(first)).Value.Trust.IsTrusted);

        var second = await ProcessAndGetPendingAsync(
            PipelineFixture.ValidInvoiceQ2B, "b.pdf", [.. MinimalPdf.Bytes(), (byte)0x01]);
        Assert.False((await _fx.ConfirmAsync(second)).Value.Trust.IsTrusted);

        // 3: reaches the threshold.
        var third = await ProcessAndGetPendingAsync(
            PipelineFixture.ValidInvoiceQ1, "c.pdf", [.. MinimalPdf.Bytes(), (byte)0x02]);
        var trust = (await _fx.ConfirmAsync(third)).Value.Trust;
        Assert.True(trust.IsTrusted);
        Assert.Equal(3, trust.ConsecutiveUnmodifiedCount);

        // 4: the same supplier is now filed without anyone looking at it.
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2C);
        var fourth = await _fx.PlaceInboxAsync("d.pdf", [.. MinimalPdf.Bytes(), (byte)0x03]);
        var result = await _fx.ProcessAsync(fourth);

        Assert.Null(result.FailureReason);
        Assert.Empty(await _fx.PendingAsync());
        Assert.Equal(4, (await _fx.GetAllInvoicesAsync()).Count);
    }

    [Fact]
    public async Task ACorrectionResetsTheCounterToZero()
    {
        var first = await ProcessAndGetPendingAsync(PipelineFixture.ValidInvoiceQ2, "a.pdf", MinimalPdf.Bytes());
        Assert.Equal(1, (await _fx.ConfirmAsync(first)).Value.Trust.ConsecutiveUnmodifiedCount);

        var second = await ProcessAndGetPendingAsync(
            PipelineFixture.ValidInvoiceQ2B, "b.pdf", [.. MinimalPdf.Bytes(), (byte)0x01]);

        // The human fixes the invoice number the template got wrong.
        var corrected = PipelineFixture.AsSubmitted(second) with { InvoiceNumber = "F2026-002-BIS" };
        var result = await _fx.ConfirmAsync(second, corrected);

        Assert.True(result.Value.WasModified);
        Assert.Equal(0, result.Value.Trust.ConsecutiveUnmodifiedCount);
        Assert.False(result.Value.Trust.IsTrusted);

        // The corrected value is what gets stored, not what the extractor read.
        Assert.Contains(await _fx.GetAllInvoicesAsync(), i => i.InvoiceNumber == "F2026-002-BIS");
    }

    [Fact]
    public async Task Rejecting_SendsThePdfToFailedAndLetsItBeSubmittedAgain()
    {
        var pending = await ProcessAndGetPendingAsync(PipelineFixture.ValidInvoiceQ2, "factura.pdf");

        var result = await _fx.RejectAsync(pending);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.PdfWasMissing);
        Assert.Empty(await _fx.PendingAsync());
        Assert.Single(Directory.EnumerateFiles(_fx.FailedPath, "*.pdf"));
        Assert.Empty(await _fx.GetAllInvoicesAsync());

        // The hash was forgotten, so the same PDF is processed again rather than skipped.
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var again = await _fx.PlaceInboxAsync("factura.pdf", MinimalPdf.Bytes());
        var reprocessed = await _fx.ProcessAsync(again);
        Assert.Equal("Pendiente de revisión", reprocessed.FailureReason);
    }

    [Fact]
    public async Task Requeueing_ReturnsThePdfToTheInboxUnderItsOriginalName()
    {
        // The reason to requeue: the template was wrong, you fixed it, now run it again.
        var pending = await ProcessAndGetPendingAsync(PipelineFixture.RequiresManualEntry, "escaneo.pdf");
        Assert.True(pending.RequiresManualEntry);

        var result = await _fx.RequeueAsync(pending);

        Assert.True(result.IsSuccess);
        Assert.Empty(await _fx.PendingAsync());
        Assert.Empty(Directory.EnumerateFiles(_fx.PendingPath, "*.pdf"));

        // Back under its own name — no stacked supplier prefix from the pending copy.
        var returned = Assert.Single(Directory.EnumerateFiles(_fx.InboxPath, "*.pdf"));
        Assert.Equal("escaneo.pdf", Path.GetFileName(returned));

        // And it is processed again instead of being skipped as already seen.
        _fx.StubExtraction(PipelineFixture.ValidInvoiceQ2);
        var reprocessed = await _fx.ProcessAsync(returned);
        Assert.Equal("Pendiente de revisión", reprocessed.FailureReason);
    }

    [Fact]
    public async Task ManualEntry_CanBeFilledInFromABlankFormAndConfirmed()
    {
        var blank = await ProcessAndGetPendingAsync(PipelineFixture.RequiresManualEntry, "escaneo.pdf");
        Assert.Equal(string.Empty, blank.InvoiceNumber);
        Assert.Equal(0m, blank.TotalAmount);

        // Everything typed by hand: by definition a modification, so no trust is earned.
        var typed = new CorrectedInvoiceFields(
            InvoiceNumber: "MAN-001",
            SupplierName: "Suministros Aurora",
            SupplierTaxId: SupplierTaxId,
            IssueDate: new DateOnly(2026, 3, 3),
            DueDate: null,
            NetAmount: 50m, TaxAmount: 10.50m, TotalAmount: 60.50m, Currency: "EUR");

        var result = await _fx.ConfirmAsync(blank, typed);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.WasModified);
        Assert.Equal(0, result.Value.Trust.ConsecutiveUnmodifiedCount);

        var stored = Assert.Single(await _fx.GetAllInvoicesAsync());
        Assert.Equal("MAN-001", stored.InvoiceNumber);
        Assert.Equal(60.50m, stored.TotalAmount);
    }
}
