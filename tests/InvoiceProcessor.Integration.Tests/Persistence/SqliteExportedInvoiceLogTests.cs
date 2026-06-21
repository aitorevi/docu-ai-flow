using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Persistence;

public sealed class SqliteExportedInvoiceLogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteExportedInvoiceLog _log;

    public SqliteExportedInvoiceLogTests()
    {
        _dbPath = Path.ChangeExtension(Path.GetTempFileName(), ".db");
        var options = Options.Create(new DatabaseOptions { Path = _dbPath });
        _log = new SqliteExportedInvoiceLog(options);
        _log.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task WasExportedAsync_returns_false_for_unknown_hash()
    {
        var result = await _log.WasExportedAsync("unknown", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task MarkExportedAsync_then_WasExported_returns_true()
    {
        // Given
        var quarter = new Quarter(2026, 1);

        // When
        await _log.MarkExportedAsync(["abc123"], quarter, DateTimeOffset.UtcNow, CancellationToken.None);

        // Then
        Assert.True(await _log.WasExportedAsync("abc123", CancellationToken.None));
    }

    [Fact]
    public async Task MarkExportedAsync_is_idempotent()
    {
        // Given
        var quarter = new Quarter(2026, 1);

        // When — mark twice, no exception
        await _log.MarkExportedAsync(["abc123"], quarter, DateTimeOffset.UtcNow, CancellationToken.None);
        await _log.MarkExportedAsync(["abc123"], quarter, DateTimeOffset.UtcNow, CancellationToken.None);

        // Then — still just one entry (no exception thrown)
        Assert.True(await _log.WasExportedAsync("abc123", CancellationToken.None));
    }
}
