using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Persistence;

public sealed class SqliteSentInvoiceLogTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteSentInvoiceLog _log;

    public SqliteSentInvoiceLogTests()
    {
        _dbPath = Path.ChangeExtension(Path.GetTempFileName(), ".db");
        var options = Options.Create(new DatabaseOptions { Path = _dbPath });
        _log = new SqliteSentInvoiceLog(options);
        _log.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task WasSentAsync_returns_false_for_unknown_hash()
    {
        var result = await _log.WasSentAsync("unknown", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task MarkSentAsync_then_WasSent_returns_true()
    {
        // Given
        var quarter = new Quarter(2026, 1);

        // When
        await _log.MarkSentAsync(["abc123"], quarter, DateTimeOffset.UtcNow, CancellationToken.None);

        // Then
        Assert.True(await _log.WasSentAsync("abc123", CancellationToken.None));
    }

    [Fact]
    public async Task MarkSentAsync_is_idempotent()
    {
        // Given
        var quarter = new Quarter(2026, 1);

        // When — mark twice, no exception
        await _log.MarkSentAsync(["abc123"], quarter, DateTimeOffset.UtcNow, CancellationToken.None);
        await _log.MarkSentAsync(["abc123"], quarter, DateTimeOffset.UtcNow, CancellationToken.None);

        // Then — still just one entry (no exception thrown)
        Assert.True(await _log.WasSentAsync("abc123", CancellationToken.None));
    }
}
