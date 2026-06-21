using InvoiceProcessor.Domain.Invoices;
using InvoiceProcessor.Infrastructure.Idempotency;

namespace InvoiceProcessor.Integration.Tests.Idempotency;

public sealed class JsonFileProcessedDocumentLogTests : IDisposable
{
    private readonly string _logPath;
    private readonly JsonFileProcessedDocumentLog _sut;

    public JsonFileProcessedDocumentLogTests()
    {
        _logPath = Path.Combine(Path.GetTempPath(), $"idempotency-{Guid.NewGuid():N}.json");
        _sut = new JsonFileProcessedDocumentLog(_logPath);
    }

    public void Dispose()
    {
        if (File.Exists(_logPath)) File.Delete(_logPath);
    }

    [Fact]
    public async Task WasProcessedAsync_WhenHashNotSeen_ReturnsFalse()
    {
        // Given
        const string hash = "abc123";

        // When
        var result = await _sut.WasProcessedAsync(hash, CancellationToken.None);

        // Then
        Assert.False(result);
    }

    [Fact]
    public async Task WasProcessedAsync_AfterMarkProcessed_ReturnsTrue()
    {
        // Given
        const string hash = "def456";
        var id = InvoiceId.New();

        // When
        await _sut.MarkProcessedAsync(hash, id, CancellationToken.None);
        var result = await _sut.WasProcessedAsync(hash, CancellationToken.None);

        // Then
        Assert.True(result);
    }

    [Fact]
    public async Task WasProcessedAsync_AfterRestart_StillReturnsTrueForPersistedHash()
    {
        // Given: write a hash with one instance
        const string hash = "ghi789";
        var id = InvoiceId.New();
        await _sut.MarkProcessedAsync(hash, id, CancellationToken.None);

        // When: create a new instance pointing to same file (simulates restart)
        var newInstance = new JsonFileProcessedDocumentLog(_logPath);
        var result = await newInstance.WasProcessedAsync(hash, CancellationToken.None);

        // Then
        Assert.True(result);
    }
}
