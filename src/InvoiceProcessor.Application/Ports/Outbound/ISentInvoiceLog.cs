using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface ISentInvoiceLog
{
    Task<bool> WasSentAsync(string contentHash, CancellationToken ct);
    Task MarkSentAsync(IEnumerable<string> contentHashes, Quarter quarter, DateTimeOffset sentAt, CancellationToken ct);
}
