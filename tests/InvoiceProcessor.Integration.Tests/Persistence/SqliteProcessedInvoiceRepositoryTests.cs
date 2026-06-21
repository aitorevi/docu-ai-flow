using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Persistence;

public sealed class SqliteProcessedInvoiceRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteProcessedInvoiceRepository _repository;

    public SqliteProcessedInvoiceRepositoryTests()
    {
        _dbPath = Path.ChangeExtension(Path.GetTempFileName(), ".db");
        var options = Options.Create(new DatabaseOptions { Path = _dbPath });
        _repository = new SqliteProcessedInvoiceRepository(options);
        _repository.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static StoredInvoice MakeInvoice(string hash, DateOnly issueDate, string number = "F001") =>
        new(
            ContentHash: hash,
            InvoiceNumber: number,
            SupplierName: "Repsol",
            SupplierTaxId: "A78374725",
            IssueDate: issueDate,
            DueDate: null,
            NetAmount: 100m,
            TaxAmount: 21m,
            TotalAmount: 121m,
            Currency: "EUR",
            ArchivedPath: "/archive/repsol.pdf"
        );

    [Fact]
    public async Task SaveAsync_stores_invoice()
    {
        // Given
        var invoice = MakeInvoice("abc123", new DateOnly(2026, 1, 15));

        // When
        await _repository.SaveAsync(invoice, CancellationToken.None);
        var all = await Collect(_repository.ListAllAsync(CancellationToken.None));

        // Then
        Assert.Single(all);
        Assert.Equal("abc123", all[0].ContentHash);
        Assert.Equal("Repsol", all[0].SupplierName);
    }

    [Fact]
    public async Task SaveAsync_is_idempotent()
    {
        // Given
        var invoice = MakeInvoice("abc123", new DateOnly(2026, 1, 15));

        // When
        await _repository.SaveAsync(invoice, CancellationToken.None);
        await _repository.SaveAsync(invoice, CancellationToken.None); // same hash again

        // Then — no exception, only one row
        var all = await Collect(_repository.ListAllAsync(CancellationToken.None));
        Assert.Single(all);
    }

    [Fact]
    public async Task ListByDateRangeAsync_returns_invoices_in_range()
    {
        // Given
        await _repository.SaveAsync(MakeInvoice("h1", new DateOnly(2025, 12, 15), "F1"), CancellationToken.None);
        await _repository.SaveAsync(MakeInvoice("h2", new DateOnly(2026, 3, 10), "F2"), CancellationToken.None);
        await _repository.SaveAsync(MakeInvoice("h3", new DateOnly(2026, 6, 1), "F3"), CancellationToken.None);

        // When — Q1 2026 range
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 3, 31);
        var result = await Collect(_repository.ListByDateRangeAsync(start, end, CancellationToken.None));

        // Then — only F2 falls in range
        Assert.Single(result);
        Assert.Equal("h2", result[0].ContentHash);
    }

    [Fact]
    public async Task MarkDeclaredAsync_sets_quarter_only_if_null()
    {
        // Given
        var invoice = MakeInvoice("abc123", new DateOnly(2026, 1, 15));
        await _repository.SaveAsync(invoice, CancellationToken.None);

        var quarter = new Quarter(2026, 1);

        // When
        await _repository.MarkDeclaredAsync(["abc123"], quarter, CancellationToken.None);
        var all = await Collect(_repository.ListAllAsync(CancellationToken.None));

        // Then — declared quarter is set
        Assert.Equal(2026, all[0].DeclaredYear);
        Assert.Equal(1, all[0].DeclaredQuarter);

        // When — try to overwrite with Q2
        var quarter2 = new Quarter(2026, 2);
        await _repository.MarkDeclaredAsync(["abc123"], quarter2, CancellationToken.None);
        var all2 = await Collect(_repository.ListAllAsync(CancellationToken.None));

        // Then — still Q1, not overwritten
        Assert.Equal(1, all2[0].DeclaredQuarter);
    }

    private static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
