using ClosedXML.Excel;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Export;
using InvoiceProcessor.Infrastructure.Files;
using Microsoft.Extensions.Options;
using InvoiceProcessor.Domain.Dispatch;
using System.Runtime.CompilerServices;

namespace InvoiceProcessor.Integration.Tests.Export;

public sealed class ClosedXmlMasterSpreadsheetWriterTests : IDisposable
{
    private readonly string _outputDir;
    private readonly ClosedXmlMasterSpreadsheetWriter _writer;

    public ClosedXmlMasterSpreadsheetWriterTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_outputDir);

        var folderOptions = Options.Create(new FolderOptions { Output = _outputDir });
        var invoices = new List<StoredInvoice>
        {
            new("h1", "F001", "Repsol", "A78374725",
                new DateOnly(2026, 1, 15), null, 100m, 21m, 121m, "EUR",
                "/archive/repsol.pdf", DeclaredYear: null, DeclaredQuarter: null),
            new("h2", "F002", "Endesa", "A81948077",
                new DateOnly(2026, 3, 10), new DateOnly(2026, 4, 10), 200m, 42m, 242m, "EUR",
                "/archive/endesa.pdf", DeclaredYear: 2026, DeclaredQuarter: 1)
        };

        _writer = new ClosedXmlMasterSpreadsheetWriter(
            new StubRepository(invoices), folderOptions);
    }

    public void Dispose() => Directory.Delete(_outputDir, recursive: true);

    [Fact]
    public async Task RebuildAsync_creates_excel_with_correct_headers()
    {
        await _writer.RebuildAsync(CancellationToken.None);

        var path = Path.Combine(_outputDir, "maestro_facturas.xlsx");
        Assert.True(File.Exists(path));

        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Facturas");
        Assert.Equal("CIF", ws.Cell(1, 1).GetString());
        Assert.Equal("Proveedor", ws.Cell(1, 2).GetString());
        Assert.Equal("NumFactura", ws.Cell(1, 3).GetString());
        Assert.Equal("TrimDeclarado", ws.Cell(1, 8).GetString());
    }

    [Fact]
    public async Task RebuildAsync_writes_Pendiente_for_undeclared_invoice()
    {
        await _writer.RebuildAsync(CancellationToken.None);

        var path = Path.Combine(_outputDir, "maestro_facturas.xlsx");
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Facturas");

        // Row 2 = h1 (undeclared)
        Assert.Equal("Pendiente", ws.Cell(2, 8).GetString());
        Assert.Equal("", ws.Cell(2, 9).GetString());
    }

    [Fact]
    public async Task RebuildAsync_writes_declared_quarter_for_declared_invoice()
    {
        await _writer.RebuildAsync(CancellationToken.None);

        var path = Path.Combine(_outputDir, "maestro_facturas.xlsx");
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Facturas");

        // Row 3 = h2 (declared Q1 2026)
        Assert.Equal("1", ws.Cell(3, 8).GetString());
        Assert.Equal("2026", ws.Cell(3, 9).GetString());
    }

    private sealed class StubRepository(List<StoredInvoice> invoices) : IProcessedInvoiceRepository
    {
        public Task SaveAsync(StoredInvoice invoice, CancellationToken ct) => Task.CompletedTask;

        public async IAsyncEnumerable<StoredInvoice> ListAllAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var inv in invoices)
                yield return inv;
            await Task.CompletedTask;
        }

        public IAsyncEnumerable<StoredInvoice> ListByDateRangeAsync(
            DateOnly start, DateOnly end, CancellationToken ct) => throw new NotImplementedException();

        public Task MarkDeclaredAsync(
            IEnumerable<string> contentHashes, Quarter quarter, CancellationToken ct) => Task.CompletedTask;
    }
}
