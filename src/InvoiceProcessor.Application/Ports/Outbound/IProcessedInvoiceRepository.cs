using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IProcessedInvoiceRepository
{
    Task SaveAsync(StoredInvoice invoice, CancellationToken ct);
    IAsyncEnumerable<StoredInvoice> ListByDateRangeAsync(DateOnly start, DateOnly end, CancellationToken ct);
    IAsyncEnumerable<StoredInvoice> ListAllAsync(CancellationToken ct);
    Task MarkDeclaredAsync(IEnumerable<string> contentHashes, Quarter quarter, CancellationToken ct);
}
