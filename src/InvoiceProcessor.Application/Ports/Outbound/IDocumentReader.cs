using InvoiceProcessor.Domain.Documents;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IDocumentReader
{
    Task<DocumentContent> OpenAsync(IncomingDocument document, CancellationToken ct);
}
