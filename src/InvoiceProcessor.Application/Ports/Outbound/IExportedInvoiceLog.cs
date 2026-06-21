using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IExportedInvoiceLog
{
    Task<bool> WasExportedAsync(string contentHash, CancellationToken ct);
    Task MarkExportedAsync(IEnumerable<string> contentHashes, Quarter quarter, DateTimeOffset exportedAt, CancellationToken ct);
}
