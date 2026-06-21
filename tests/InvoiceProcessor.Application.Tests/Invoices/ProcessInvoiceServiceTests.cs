using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpMonads.Core;

namespace InvoiceProcessor.Application.Tests.Invoices;

public sealed class ProcessInvoiceServiceTests
{
    private readonly IDocumentReader _reader = Substitute.For<IDocumentReader>();
    private readonly IInvoiceDataExtractor _extractor = Substitute.For<IInvoiceDataExtractor>();
    private readonly ISupplierNormalizer _supplierNormalizer = Substitute.For<ISupplierNormalizer>();
    private readonly IProcessedInvoiceRepository _repository = Substitute.For<IProcessedInvoiceRepository>();
    private readonly IDocumentArchiver _archiver = Substitute.For<IDocumentArchiver>();
    private readonly IProcessedDocumentLog _log = Substitute.For<IProcessedDocumentLog>();

    private readonly IProcessInvoiceUseCase _sut;

    private static readonly IncomingDocument TestDocument = new(
        DocumentId.New(), "test.pdf", "/inbox/test.pdf", "abc123", DateTimeOffset.UtcNow);

    public ProcessInvoiceServiceTests()
    {
        _sut = new ProcessInvoiceService(
            _reader, _extractor, _supplierNormalizer, _repository, _archiver, _log,
            NullLogger<ProcessInvoiceService>.Instance,
            Options.Create(new ExtractionOptions()));
    }

    [Fact]
    public async Task ExecuteAsync_WhenDocumentAlreadyProcessed_DoesNotCallExtractor()
    {
        // Given
        _log.WasProcessedAsync(TestDocument.ContentHash, Arg.Any<CancellationToken>())
            .Returns(true);

        // When
        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        // Then
        Assert.True(result.Success);
        await _extractor.DidNotReceive().ExtractAsync(Arg.Any<DocumentContent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMapperFails_CallsArchiveFailedAsync()
    {
        // Given
        _log.WasProcessedAsync(TestDocument.ContentHash, Arg.Any<CancellationToken>())
            .Returns(false);

        var stream = new MemoryStream([1, 2, 3]);
        var content = new DocumentContent("test.pdf", "application/pdf", stream);
        _reader.OpenAsync(TestDocument, Arg.Any<CancellationToken>())
            .Returns(content);

        // Extraction returns data that will fail mapping (empty fields)
        var extraction = new ExtractionResult(
            new Dictionary<string, ExtractedField>(),
            [],
            0.9m);
        _extractor.ExtractAsync(content, Arg.Any<CancellationToken>())
            .Returns(extraction);

        _supplierNormalizer.Normalize(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new Supplier("Unknown", null, null));

        _archiver.ArchiveFailedAsync(TestDocument, Arg.Any<CancellationToken>())
            .Returns("/failed/test.pdf");

        // When
        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        // Then
        Assert.False(result.Success);
        await _archiver.Received(1).ArchiveFailedAsync(TestDocument, Arg.Any<CancellationToken>());
        await _repository.DidNotReceive().SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMappingSucceeds_SavesInvoiceAndMarksProcessed()
    {
        // Given
        _log.WasProcessedAsync(TestDocument.ContentHash, Arg.Any<CancellationToken>())
            .Returns(false);

        var stream = new MemoryStream([1, 2, 3]);
        var content = new DocumentContent("test.pdf", "application/pdf", stream);
        _reader.OpenAsync(TestDocument, Arg.Any<CancellationToken>())
            .Returns(content);

        var fields = new Dictionary<string, ExtractedField>
        {
            ["invoice_number"] = new("F-001", 0.95m),
            ["issue_date"] = new("2026-01-15", 0.95m),
            ["net_amount"] = new("100.00", 0.95m),
            ["tax_amount"] = new("21.00", 0.95m),
            ["total_amount"] = new("121.00", 0.95m),
            ["currency"] = new("EUR", 0.95m),
        };
        var extraction = new ExtractionResult(fields, [], 0.95m);
        _extractor.ExtractAsync(content, Arg.Any<CancellationToken>())
            .Returns(extraction);

        _supplierNormalizer.Normalize(Arg.Any<string?>(), Arg.Any<string?>())
            .Returns(new Supplier("Repsol", "A78374725", null));

        _archiver.ArchiveProcessedAsync(Arg.Any<IncomingDocument>(), Arg.Any<Invoice>(), Arg.Any<CancellationToken>())
            .Returns("/archive/2026/01/Repsol/Repsol-F-001.pdf");

        // When
        var result = await _sut.ExecuteAsync(TestDocument, CancellationToken.None);

        // Then
        Assert.True(result.Success);
        Assert.NotNull(result.InvoiceId);
        await _repository.Received(1).SaveAsync(Arg.Any<StoredInvoice>(), Arg.Any<CancellationToken>());
        await _log.Received(1).MarkProcessedAsync(
            TestDocument.ContentHash, Arg.Any<InvoiceId>(), Arg.Any<CancellationToken>());
    }
}
