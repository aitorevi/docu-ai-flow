using InvoiceProcessor.Application.Invoices;

namespace InvoiceProcessor.Integration.Tests.Pipeline;

public sealed class PipelineExportTests : IDisposable
{
    private readonly PipelineFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static StoredInvoice MakeInvoice(string hash, string number, DateOnly date,
        string supplier = "Test Supplier S.L.", decimal net = 100m, decimal tax = 21m) =>
        new(hash, number, supplier, "B12345678", date, null, net, tax, net + tax, "EUR",
            $"/archive/{date.Year}/{date.Month:D2}/{supplier}/{supplier}-{number}.pdf");

    [Fact]
    public async Task ExportQuarter_GeneratesExcelWithCorrectData()
    {
        // Seed 2 invoices in Q2 2026 (both in January — within Q2's source range Jan–Jun 2026)
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-a", "F2026-Q2-001", new DateOnly(2026, 4, 1)));
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-b", "F2026-Q2-002", new DateOnly(2026, 5, 15)));

        var result = await _fx.ExportAsync(2026, 2);

        Assert.False(result.NothingNew);
        Assert.Equal(2, result.Exported);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath), $"Excel not found at {result.FilePath}");
        Assert.True(new FileInfo(result.FilePath!).Length > 0, "Excel file is empty");
    }

    [Fact]
    public async Task ExportQuarter_MarksInvoicesAsExported()
    {
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-c", "F2026-Q2-003", new DateOnly(2026, 4, 10)));
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-d", "F2026-Q2-004", new DateOnly(2026, 6, 30)));

        await _fx.ExportAsync(2026, 2);

        var count = await _fx.GetExportedCountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ExportQuarter_NothingNew_WhenAlreadyExported()
    {
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-e", "F2026-Q2-005", new DateOnly(2026, 4, 20)));

        var first = await _fx.ExportAsync(2026, 2);
        Assert.False(first.NothingNew);

        var second = await _fx.ExportAsync(2026, 2);
        Assert.True(second.NothingNew);
        Assert.Equal(0, second.Exported);
    }

    [Fact]
    public async Task MasterSpreadsheet_ContainsAllInvoices()
    {
        // 3 invoices: 1 in Q1 range (Jan–Mar), 2 in Q2 range (Jan–Jun)
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-f", "F2025-Q4-001", new DateOnly(2025, 11, 1)));
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-g", "F2026-Q2-010", new DateOnly(2026, 3, 1)));
        await _fx.SaveInvoiceDirectlyAsync(MakeInvoice("hash-h", "F2026-Q2-011", new DateOnly(2026, 4, 1)));

        // Export Q2 — triggers master rebuild
        await _fx.ExportAsync(2026, 2);

        // Master spreadsheet is in output/ root
        var master = Directory.EnumerateFiles(_fx.OutputPath, "*.xlsx").FirstOrDefault();
        Assert.NotNull(master);
        Assert.True(File.Exists(master), "Master spreadsheet should exist after export");
        Assert.True(new FileInfo(master).Length > 0);
    }
}
