using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace InvoiceProcessor.Application.Tests.Invoices;

public sealed class ProcessInvoiceServiceTests
{
    private readonly IDocumentReader _reader = Substitute.For<IDocumentReader>();
    private readonly IInvoiceDataExtractor _extractor = Substitute.For<IInvoiceDataExtractor>();
    private readonly ISupplierNormalizer _supplierNormalizer = Substitute.For<ISupplierNormalizer>();
    private readonly IProcessedInvoiceRepository _repository = Substitute.For<IProcessedInvoiceRepository>();
    private readonly IDocumentArchiver _archiver = Substitute.For<IDocumentArchiver>();
    private readonly IProcessedDocumentLog _log = Substitute.For<IProcessedDocumentLog>();
    private readonly ISupplierTrustRepository _trust = Substitute.For<ISupplierTrustRepository>();
    private readonly IPendingInvoiceRepository _pending = Substitute.For<IPendingInvoiceRepository>();

    private readonly IProcessInvoiceUseCase _sut;

    private const string SupplierTaxId = "B12345674";

    private static readonly IncomingDocument TestDocument = new(
        DocumentId.New(), "test.pdf", "/inbox/test.pdf", "abc123", DateTimeOffset.UtcNow);

    public ProcessInvoiceServiceTests()
    {
        _sut = new ProcessInvoiceService(
            _reader, _extractor, _supplierNormalizer, _repository, _archiver, _log,
            _trust, _pending,
            NullLogger<ProcessInvoiceService>.Instance,
            Options.Create(new ExtractionOptions()));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private DocumentContent GivenAnUnseenDocument()
    {
        _log.WasProcessedAsync(TestDocument.ContentHash, Arg.Any<CancellationToken>()).Returns(false);
        var content = new DocumentContent("test.pdf", "application/pdf", new MemoryStream([1, 2, 3]));
        _reader.OpenAsync(TestDocument, Arg.Any<CancellationToken>()).Returns(content);
        return content;
    }

    private void GivenExtraction(DocumentContent content, ExtractionResult extraction) =>
        _extractor.ExtractAsync(content, Arg.Any<CancellationToken>()).Returns(extraction);

    private static ExtractionResult AValidExtraction() => new(
        new Dictionary<string, ExtractedField>
        {
            ["invoice_number"] = new("F-001", 0.95m),
            ["issue_date"] = new("2026-01-15", 0.95m),
            ["net_amount"] = new("100.00", 0.95m),
            ["tax_amount"] = new("21.00", 0.95m),
            ["total_amount"] = new("121.00", 0.95m),
        },
        [], 0.95m);

    private void GivenTheSupplierIs(string name, string? taxId) =>
        _supplierNormalizer.Normalize(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new Supplier(name, taxId, null));

    private void GivenSupplierTrust(SupplierTrust? trust) =>
        _trust.GetAsync(SupplierTaxId, Arg.Any<CancellationToken>()).Returns(trust);

    // ── Duplicates ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenDocumentAlreadyProcessed_MovesItOutOfTheInboxWithoutExtracting()
    {
        _log.WasProcessedAsync(TestDocument.ContentHash, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.True(result.Success);
        await _extractor.DidNotReceive().ExtractAsync(Arg.Any<DocumentContent>(), Arg.Any<CancellationToken>());
        // It must leave the inbox, or the watcher re-polls the same file forever.
        await _archiver.Received(1).ArchiveDuplicateAsync(TestDocument, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenSameNumberAndTaxIdAlreadyStored_TreatsItAsADuplicate()
    {
        // Given — a re-issued copy: different bytes, so the hash log missed it, but the same
        // invoice number from the same supplier. Declaring it again would double the VAT.
        var content = GivenAnUnseenDocument();
        GivenExtraction(content, AValidExtraction());
        GivenTheSupplierIs("Suministros Aurora", SupplierTaxId);
        _repository.ExistsByNaturalKeyAsync("F-001", SupplierTaxId, Arg.Any<CancellationToken>()).Returns(true);

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Posible duplicado", result.FailureReason);
        await _archiver.Received(1).ArchiveDuplicateAsync(TestDocument, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenSupplierHasNoTaxId_SkipsTheNaturalKeyCheck()
    {
        // (number, null) is not an identity — two different suppliers can both issue "1".
        var content = GivenAnUnseenDocument();
        GivenExtraction(content, AValidExtraction());
        GivenTheSupplierIs("Sin CIF", null);

        await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        await _repository.DidNotReceive().ExistsByNaturalKeyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ── Manual entry ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenExtractionRequiresManualEntry_HoldsAnEmptyInvoiceForReview()
    {
        // Given — nothing could be understood (no text layer, or no template matched).
        var content = GivenAnUnseenDocument();
        GivenExtraction(content, new ExtractionResult(
            new Dictionary<string, ExtractedField>(), [], 0m, RequiresManualEntry: true));

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Alta manual requerida", result.FailureReason);
        await _archiver.Received(1).ArchivePendingAsync(
            TestDocument, string.Empty, Arg.Any<CancellationToken>());
        await _pending.Received(1).SaveAsync(
            Arg.Is<PendingInvoice>(p => p.RequiresManualEntry && p.ContentHash == TestDocument.ContentHash),
            Arg.Any<CancellationToken>());
        // The PDF is held for a human, not thrown at failed/.
        await _archiver.DidNotReceive().ArchiveFailedAsync(Arg.Any<IncomingDocument>(), Arg.Any<CancellationToken>());
        // Mapping an empty result would only produce a misleading "missing field" error.
        _supplierNormalizer.DidNotReceive().Normalize(Arg.Any<string?>(), Arg.Any<string?>());
    }

    // ── Trust gate ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenSupplierIsNotYetTrusted_HoldsTheInvoiceForReview()
    {
        var content = GivenAnUnseenDocument();
        GivenExtraction(content, AValidExtraction());
        GivenTheSupplierIs("Suministros Aurora", SupplierTaxId);
        GivenSupplierTrust(new SupplierTrust(SupplierTaxId, 3, IsTrusted: false));

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Pendiente de revisión", result.FailureReason);
        await _pending.Received(1).SaveAsync(
            Arg.Is<PendingInvoice>(p => p.InvoiceNumber == "F-001" && !p.RequiresManualEntry),
            Arg.Any<CancellationToken>());
        // Not archived and not counted yet — it is not a processed invoice until a human says so.
        await _repository.DidNotReceive().SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
        await _archiver.DidNotReceive().ArchiveProcessedAsync(
            Arg.Any<IncomingDocument>(), Arg.Any<Invoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenSupplierHasNeverBeenSeen_HoldsTheInvoiceForReview()
    {
        var content = GivenAnUnseenDocument();
        GivenExtraction(content, AValidExtraction());
        GivenTheSupplierIs("Suministros Aurora", SupplierTaxId);
        GivenSupplierTrust(null);   // no trust record at all

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.Equal("Pendiente de revisión", result.FailureReason);
        await _pending.Received(1).SaveAsync(Arg.Any<PendingInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenSupplierIsTrusted_ArchivesWithoutHumanReview()
    {
        var content = GivenAnUnseenDocument();
        GivenExtraction(content, AValidExtraction());
        GivenTheSupplierIs("Suministros Aurora", SupplierTaxId);
        GivenSupplierTrust(new SupplierTrust(SupplierTaxId, 20, IsTrusted: true));
        _archiver.ArchiveProcessedAsync(Arg.Any<IncomingDocument>(), Arg.Any<Invoice>(), Arg.Any<CancellationToken>())
            .Returns("/archive/2026/01/aurora/aurora-f-001.pdf");

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Null(result.FailureReason);
        await _repository.Received(1).SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
        await _log.Received(1).MarkProcessedAsync(
            TestDocument.ContentHash, Arg.Any<InvoiceId>(), Arg.Any<CancellationToken>());
        await _pending.DidNotReceive().SaveAsync(Arg.Any<PendingInvoice>(), Arg.Any<CancellationToken>());
    }

    // ── Invalid extraction ───────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenMapperFails_ArchivesAsFailed()
    {
        var content = GivenAnUnseenDocument();
        // Extraction claims high confidence but carries no fields — mapping must reject it.
        GivenExtraction(content, new ExtractionResult(new Dictionary<string, ExtractedField>(), [], 0.9m));
        GivenTheSupplierIs("Unknown", null);
        _archiver.ArchiveFailedAsync(TestDocument, Arg.Any<CancellationToken>()).Returns("/failed/test.pdf");

        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        Assert.False(result.Success);
        await _archiver.Received(1).ArchiveFailedAsync(TestDocument, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
    }
}
