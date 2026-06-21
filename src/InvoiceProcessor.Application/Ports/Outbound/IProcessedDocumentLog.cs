using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IProcessedDocumentLog
{
    Task<bool> WasProcessedAsync(string contentHash, CancellationToken ct);
    Task MarkProcessedAsync(string contentHash, InvoiceId invoiceId, CancellationToken ct);
}
