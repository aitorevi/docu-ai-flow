using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IProcessedDocumentLog
{
    Task<bool> WasProcessedAsync(string contentHash, CancellationToken ct);
    Task MarkProcessedAsync(string contentHash, InvoiceId invoiceId, CancellationToken ct);

    // Forgets a hash. Needed when a pending invoice is rejected or requeued: it was marked
    // processed on entering pending/, and without forgetting it the same PDF would be skipped
    // as a duplicate and could never be reviewed again.
    Task RemoveAsync(string contentHash, CancellationToken ct);
}
