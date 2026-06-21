using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IDocumentArchiver
{
    Task<string> ArchiveProcessedAsync(IncomingDocument document, Invoice invoice, CancellationToken ct);
    Task<string> ArchiveFailedAsync(IncomingDocument document, CancellationToken ct);
}
