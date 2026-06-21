using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain.Documents;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IInvoiceDataExtractor
{
    Task<ExtractionResult> ExtractAsync(DocumentContent content, CancellationToken ct);
}
