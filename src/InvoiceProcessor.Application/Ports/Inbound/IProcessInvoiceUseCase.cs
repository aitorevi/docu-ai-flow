using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain.Documents;

namespace InvoiceProcessor.Application.Ports.Inbound;

public interface IProcessInvoiceUseCase
{
    Task<ProcessInvoiceResult> ExecuteAsync(IncomingDocument document, CancellationToken ct);
}
