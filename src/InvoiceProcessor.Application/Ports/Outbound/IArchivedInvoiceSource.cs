using InvoiceProcessor.Application.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IArchivedInvoiceSource
{
    IAsyncEnumerable<ArchivedInvoice> ListByDateRangeAsync(DateOnly start, DateOnly end, CancellationToken ct);
}
