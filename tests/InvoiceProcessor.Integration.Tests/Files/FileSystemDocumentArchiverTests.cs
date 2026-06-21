using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;
using InvoiceProcessor.Infrastructure.Files;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Files;

public sealed class FileSystemDocumentArchiverTests : IDisposable
{
    private readonly string _root;
    private readonly string _archive;
    private readonly string _failed;
    private readonly FileSystemDocumentArchiver _sut;

    public FileSystemDocumentArchiverTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"archiver-tests-{Guid.NewGuid():N}");
        _archive = Path.Combine(_root, "archive");
        _failed = Path.Combine(_root, "failed");

        var options = new FolderOptions
        {
            Inbox = Path.Combine(_root, "inbox"),
            Archive = _archive,
            Failed = _failed,
            Output = Path.Combine(_root, "output"),
            MaxConcurrency = 1,
            PollSeconds = 5
        };
        _sut = new FileSystemDocumentArchiver(Options.Create(options));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Invoice BuildInvoice(string number, DateOnly issueDate, string supplierName)
    {
        var supplier = new Supplier(supplierName, null, null);
        var money = new Money(100m, "EUR");
        var tax = new Money(21m, "EUR");
        var total = new Money(121m, "EUR");
        Invoice? invoice = null;
        Invoice.Create(InvoiceId.New(), number, supplier, issueDate, null, money, tax, total, [])
            .Match(
                onSuccess: inv => { invoice = inv; return true; },
                onFailure: _ => false);
        return invoice!;
    }

    private static string CreateTempPdf(string dir, string name)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, [0x25, 0x50, 0x44, 0x46]); // %PDF magic bytes
        return path;
    }

    [Fact]
    public async Task ArchiveProcessedAsync_MovesFileToYearMonthSupplierPath()
    {
        // Given
        var inbox = Path.Combine(_root, "inbox");
        var pdfPath = CreateTempPdf(inbox, "factura.pdf");
        var doc = new IncomingDocument(DocumentId.New(), "factura.pdf", pdfPath, "hash1", DateTimeOffset.UtcNow);
        var invoice = BuildInvoice("F2026-0042", new DateOnly(2026, 1, 15), "Repsol");

        // When
        var result = await _sut.ArchiveProcessedAsync(doc, invoice, CancellationToken.None);

        // Then
        Assert.True(File.Exists(result));
        Assert.Contains(Path.Combine("2026", "01", "repsol"), result);
        Assert.Contains("repsol-f2026-0042.pdf", result);
        Assert.False(File.Exists(pdfPath));
    }

    [Fact]
    public async Task ArchiveFailedAsync_MovesFileToFailedWithTimestampPrefix()
    {
        // Given
        var inbox = Path.Combine(_root, "inbox");
        var pdfPath = CreateTempPdf(inbox, "corrupto.pdf");
        var doc = new IncomingDocument(DocumentId.New(), "corrupto.pdf", pdfPath, "hash2", DateTimeOffset.UtcNow);

        // When
        var result = await _sut.ArchiveFailedAsync(doc, CancellationToken.None);

        // Then
        Assert.True(File.Exists(result));
        Assert.StartsWith(_failed, result);
        Assert.Contains("corrupto.pdf", result);
        Assert.False(File.Exists(pdfPath));
    }

    [Fact]
    public async Task ArchiveProcessedAsync_StripsAccentsFromSupplierFolderName()
    {
        // Given
        var inbox = Path.Combine(_root, "inbox");
        var pdfPath = CreateTempPdf(inbox, "factura.pdf");
        var doc = new IncomingDocument(DocumentId.New(), "factura.pdf", pdfPath, "hashA", DateTimeOffset.UtcNow);
        var invoice = BuildInvoice("F2026-0010", new DateOnly(2026, 3, 1), "Buscando Sueños");

        // When
        var result = await _sut.ArchiveProcessedAsync(doc, invoice, CancellationToken.None);

        // Then
        Assert.Contains(Path.Combine("2026", "03", "buscando-suenos"), result);
    }

    [Fact]
    public async Task ArchiveProcessedAsync_StripsLegalSuffixFromSupplierFolderName()
    {
        // Given
        var inbox = Path.Combine(_root, "inbox");
        var pdfPath = CreateTempPdf(inbox, "factura.pdf");
        var doc = new IncomingDocument(DocumentId.New(), "factura.pdf", pdfPath, "hashB", DateTimeOffset.UtcNow);
        var invoice = BuildInvoice("F2026-0011", new DateOnly(2026, 3, 1), "Colchones Mivis S.L.");

        // When
        var result = await _sut.ArchiveProcessedAsync(doc, invoice, CancellationToken.None);

        // Then
        Assert.Contains(Path.Combine("2026", "03", "colchones-mivis"), result);
    }

    [Fact]
    public async Task ArchiveProcessedAsync_WhenCollision_AddsNumberSuffix()
    {
        // Given: two files with the same invoice number
        var inbox = Path.Combine(_root, "inbox");
        var pdf1 = CreateTempPdf(inbox, "first.pdf");
        var pdf2 = CreateTempPdf(inbox, "second.pdf");

        var invoice = BuildInvoice("F2026-0042", new DateOnly(2026, 1, 15), "Repsol");

        var doc1 = new IncomingDocument(DocumentId.New(), "first.pdf", pdf1, "hash3", DateTimeOffset.UtcNow);
        var doc2 = new IncomingDocument(DocumentId.New(), "second.pdf", pdf2, "hash4", DateTimeOffset.UtcNow);

        // When
        var result1 = await _sut.ArchiveProcessedAsync(doc1, invoice, CancellationToken.None);

        // Recreate the source file for doc2
        File.WriteAllBytes(pdf2, [0x25, 0x50, 0x44, 0x46]);
        var result2 = await _sut.ArchiveProcessedAsync(doc2, invoice, CancellationToken.None);

        // Then: different paths, second one has (2) suffix
        Assert.NotEqual(result1, result2);
        Assert.Contains("(2)", result2);
    }
}
