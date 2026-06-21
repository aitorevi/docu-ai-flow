using ClosedXML.Excel;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure.Export;
using InvoiceProcessor.Infrastructure.Files;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Export;

public sealed class ClosedXmlQuarterSpreadsheetExporterTests : IDisposable
{
    private readonly string _outputDir;
    private readonly IQuarterSpreadsheetExporter _sut;
    private static readonly Quarter Q1_2026 = new(2026, 1);

    private static readonly StoredInvoice Invoice1 = new(
        "hash1", "F001", "Repsol", "A78374725",
        new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15),
        100m, 21m, 121m, "EUR", "/archive/test.pdf");

    private static readonly StoredInvoice Invoice2 = new(
        "hash2", "F002", "Endesa", "A81948077",
        new DateOnly(2026, 3, 10), null,
        200m, 42m, 242m, "EUR", "/archive/test2.pdf");

    public ClosedXmlQuarterSpreadsheetExporterTests()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_outputDir);

        var options = Options.Create(new FolderOptions { Output = _outputDir });
        _sut = new ClosedXmlQuarterSpreadsheetExporter(options);
    }

    public void Dispose() => Directory.Delete(_outputDir, recursive: true);

    [Fact]
    public async Task ExportAsync_creates_file_with_correct_sheet_name()
    {
        // When
        var path = await _sut.ExportAsync(Q1_2026, [Invoice1, Invoice2], CancellationToken.None);

        // Then
        Assert.True(File.Exists(path));
        using var wb = new XLWorkbook(path);
        Assert.NotNull(wb.Worksheet("Facturas"));
    }

    [Fact]
    public async Task ExportAsync_writes_quarter_columns_as_text()
    {
        // When
        var path = await _sut.ExportAsync(Q1_2026, [Invoice1], CancellationToken.None);

        // Then
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Facturas");
        Assert.Equal("1", ws.Cell(2, 3).GetString());   // Trimestre
        Assert.Equal("2026", ws.Cell(2, 4).GetString()); // Año
    }

    [Fact]
    public async Task ExportAsync_writes_base_with_dot_decimal()
    {
        // When
        var path = await _sut.ExportAsync(Q1_2026, [Invoice1], CancellationToken.None);

        // Then
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Facturas");
        Assert.Equal("100.00", ws.Cell(2, 8).GetString()); // Base
    }

    [Fact]
    public async Task ExportAsync_writes_dates_in_spanish_format()
    {
        // When
        var path = await _sut.ExportAsync(Q1_2026, [Invoice1], CancellationToken.None);

        // Then
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheet("Facturas");
        Assert.Equal("15/01/2026", ws.Cell(2, 2).GetString()); // FechaFactura
    }
}
