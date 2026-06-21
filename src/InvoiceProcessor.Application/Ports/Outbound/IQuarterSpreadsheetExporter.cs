using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IQuarterSpreadsheetExporter
{
    Task<string> ExportAsync(Quarter quarter, IReadOnlyCollection<StoredInvoice> invoices, CancellationToken ct);
}
