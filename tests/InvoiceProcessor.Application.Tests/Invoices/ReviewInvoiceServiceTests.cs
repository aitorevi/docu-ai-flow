using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace InvoiceProcessor.Application.Tests.Invoices;

public sealed class ReviewInvoiceServiceTests
{
    private readonly IPendingInvoiceRepository _pending = Substitute.For<IPendingInvoiceRepository>();
    private readonly ISupplierTrustRepository _trust = Substitute.For<ISupplierTrustRepository>();
    private readonly IProcessedInvoiceRepository _repository = Substitute.For<IProcessedInvoiceRepository>();
    private readonly IDocumentArchiver _archiver = Substitute.For<IDocumentArchiver>();
    private readonly IProcessedDocumentLog _log = Substitute.For<IProcessedDocumentLog>();
    private readonly IMasterSpreadsheetWriter _master = Substitute.For<IMasterSpreadsheetWriter>();

    private readonly IReviewInvoiceUseCase _sut;

    private const string Hash = "abc123";
    private const string TaxId = "B12345674";

    public ReviewInvoiceServiceTests()
    {
        _sut = new ReviewInvoiceService(
            _pending, _trust, _repository, _archiver, _log, _master,
            Options.Create(new ExtractionOptions { SupplierTrustThreshold = 3 }));
    }

    private static PendingInvoice APendingInvoice() => new(
        ContentHash: Hash,
        PendingPath: "/pending/aurora-test.pdf",
        OriginalFileName: "test.pdf",
        InvoiceNumber: "F-001",
        SupplierName: "Suministros Aurora",
        SupplierTaxId: TaxId,
        IssueDate: new DateOnly(2026, 1, 15),
        DueDate: new DateOnly(2026, 2, 15),
        NetAmount: 100m,
        TaxAmount: 21m,
        TotalAmount: 121m,
        Currency: "EUR",
        Confidence: new Dictionary<string, CapturedField>(),
        DetectedAt: DateTimeOffset.UtcNow);

    private static CorrectedInvoiceFields AsSubmitted(PendingInvoice p) => new(
        p.InvoiceNumber, p.SupplierName, p.SupplierTaxId, p.IssueDate, p.DueDate,
        p.NetAmount, p.TaxAmount, p.TotalAmount, p.Currency);

    private void GivenPending(PendingInvoice pending)
    {
        _pending.FindByContentHashAsync(Hash, Arg.Any<CancellationToken>()).Returns(pending);
        _archiver.ArchiveConfirmedAsync(pending.PendingPath, Arg.Any<Invoice>(), Arg.Any<CancellationToken>())
            .Returns("/archive/2026/01/aurora/aurora-f-001.pdf");
    }

    // ── Confirm ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmAsync_UntouchedFields_CountsAsUnmodifiedAndAdvancesTrust()
    {
        var pending = APendingInvoice();
        GivenPending(pending);
        _trust.GetAsync(TaxId, Arg.Any<CancellationToken>())
            .Returns(new SupplierTrust(TaxId, 1, IsTrusted: false));

        var result = await _sut.ConfirmAsync(Hash, AsSubmitted(pending), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.WasModified);
        Assert.Equal(2, result.Value.Trust.ConsecutiveUnmodifiedCount);
        Assert.False(result.Value.Trust.IsTrusted);
        await _repository.Received(1).SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
        await _pending.Received(1).DeleteAsync(Hash, Arg.Any<CancellationToken>());
        await _master.Received(1).RebuildAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_ReachingTheThreshold_MakesTheSupplierTrusted()
    {
        var pending = APendingInvoice();
        GivenPending(pending);
        _trust.GetAsync(TaxId, Arg.Any<CancellationToken>())
            .Returns(new SupplierTrust(TaxId, 2, IsTrusted: false));   // threshold is 3 here

        var result = await _sut.ConfirmAsync(Hash, AsSubmitted(pending), CancellationToken.None);

        Assert.True(result.Value.Trust.IsTrusted);
        await _trust.Received(1).SaveAsync(
            Arg.Is<SupplierTrust>(t => t.IsTrusted && t.ConsecutiveUnmodifiedCount == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_WithACorrectedField_ResetsTrustToZero()
    {
        // A supplier that had already earned trust makes one mistake: back to square one.
        var pending = APendingInvoice();
        GivenPending(pending);
        _trust.GetAsync(TaxId, Arg.Any<CancellationToken>())
            .Returns(new SupplierTrust(TaxId, 3, IsTrusted: true));

        // The template misread the invoice number; the human fixes it. (Amounts stay coherent —
        // Invoice.Create enforces net + tax = total, so a correction has to respect that too.)
        var corrected = AsSubmitted(pending) with { InvoiceNumber = "F-002" };
        var result = await _sut.ConfirmAsync(Hash, corrected, CancellationToken.None);

        Assert.True(result.Value.WasModified);
        Assert.Equal(0, result.Value.Trust.ConsecutiveUnmodifiedCount);
        Assert.False(result.Value.Trust.IsTrusted);
    }

    [Fact]
    public async Task ConfirmAsync_ArchivesAndPersistsBeforeTouchingTrust()
    {
        // Order matters: if archiving or saving throws, the counter must not have moved, or a
        // retry would count the same confirmation twice.
        var pending = APendingInvoice();
        _pending.FindByContentHashAsync(Hash, Arg.Any<CancellationToken>()).Returns(pending);
        _archiver.ArchiveConfirmedAsync(pending.PendingPath, Arg.Any<Invoice>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new IOException("disk full"));

        await Assert.ThrowsAsync<IOException>(() =>
            _sut.ConfirmAsync(Hash, AsSubmitted(pending), CancellationToken.None));

        await _trust.DidNotReceive().SaveAsync(Arg.Any<SupplierTrust>(), Arg.Any<CancellationToken>());
        await _pending.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmAsync_WhenTheInvoiceIsNotPending_Fails()
    {
        _pending.FindByContentHashAsync(Hash, Arg.Any<CancellationToken>()).Returns((PendingInvoice?)null);

        var result = await _sut.ConfirmAsync(Hash, AsSubmitted(APendingInvoice()), CancellationToken.None);

        Assert.False(result.IsSuccess);
    }

    // ── Reject ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_ForgetsTheHashSoTheSamePdfCanBeSubmittedAgain()
    {
        var pending = APendingInvoice();
        GivenPending(pending);
        _archiver.RejectPendingAsync(pending.PendingPath, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.RejectAsync(Hash, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.PdfWasMissing);
        await _log.Received(1).RemoveAsync(Hash, Arg.Any<CancellationToken>());
        await _pending.Received(1).DeleteAsync(Hash, Arg.Any<CancellationToken>());
        // A rejection says nothing about the supplier's template, so trust is untouched.
        await _trust.DidNotReceive().SaveAsync(Arg.Any<SupplierTrust>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RejectAsync_WhenThePdfIsGone_StillClearsTheQueueEntry()
    {
        var pending = APendingInvoice();
        GivenPending(pending);
        _archiver.RejectPendingAsync(pending.PendingPath, Arg.Any<CancellationToken>()).Returns(false);

        var result = await _sut.RejectAsync(Hash, CancellationToken.None);

        Assert.True(result.Value.PdfWasMissing);
        await _pending.Received(1).DeleteAsync(Hash, Arg.Any<CancellationToken>());
    }

    // ── Requeue ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequeueAsync_ReturnsThePdfUnderItsOriginalName()
    {
        var pending = APendingInvoice();
        GivenPending(pending);
        _archiver.RequeuePendingAsync(pending.PendingPath, "test.pdf", Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.RequeueAsync(Hash, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // The original name, not the pending one: the pending copy carries a supplier prefix, and
        // reusing it would stack another prefix on every round trip.
        await _archiver.Received(1).RequeuePendingAsync(
            pending.PendingPath, "test.pdf", Arg.Any<CancellationToken>());
        // Without forgetting the hash the requeued PDF is skipped as a duplicate on arrival.
        await _log.Received(1).RemoveAsync(Hash, Arg.Any<CancellationToken>());
    }
}
