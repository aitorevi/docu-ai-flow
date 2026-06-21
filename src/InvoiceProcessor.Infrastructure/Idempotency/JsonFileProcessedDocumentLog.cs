using System.Text.Json;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Infrastructure.Idempotency;

public sealed class JsonFileProcessedDocumentLog : IProcessedDocumentLog
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly HashSet<string> _hashes;

    public JsonFileProcessedDocumentLog(string filePath)
    {
        _filePath = filePath;
        _hashes = LoadFromDisk(filePath);
    }

    public async Task<bool> WasProcessedAsync(string contentHash, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try { return _hashes.Contains(contentHash); }
        finally { _lock.Release(); }
    }

    public async Task MarkProcessedAsync(string contentHash, InvoiceId invoiceId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _hashes.Add(contentHash);
            await AppendToDiskAsync(contentHash, invoiceId, ct);
        }
        finally { _lock.Release(); }
    }

    private static HashSet<string> LoadFromDisk(string filePath)
    {
        if (!File.Exists(filePath)) return [];

        var hashes = new HashSet<string>();
        var lines = File.ReadAllLines(filePath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<LogEntry>(line);
                if (entry?.Hash is not null) hashes.Add(entry.Hash);
            }
            catch (JsonException) { /* skip malformed lines */ }
        }
        return hashes;
    }

    private async Task AppendToDiskAsync(string contentHash, InvoiceId invoiceId, CancellationToken ct)
    {
        var entry = new LogEntry(contentHash, invoiceId.Value, DateTimeOffset.UtcNow);
        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        await File.AppendAllTextAsync(_filePath, line, ct);
    }

    private sealed record LogEntry(string Hash, Guid InvoiceId, DateTimeOffset ProcessedAt);
}
