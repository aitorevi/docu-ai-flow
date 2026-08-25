using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IProcessedInvoiceRepository
{
    Task SaveAsync(StoredInvoice invoice, CancellationToken ct);
    IAsyncEnumerable<StoredInvoice> ListByDateRangeAsync(DateOnly start, DateOnly end, CancellationToken ct);
    IAsyncEnumerable<StoredInvoice> ListAllAsync(CancellationToken ct);
    Task MarkDeclaredAsync(IEnumerable<string> contentHashes, Quarter quarter, CancellationToken ct);

    // Natural-key lookup: same invoice number from the same supplier. Catches a re-issued or
    // re-sent copy whose bytes differ, so the content-hash log misses it — which would otherwise
    // declare the same VAT twice.
    Task<bool> ExistsByNaturalKeyAsync(string invoiceNumber, string supplierTaxId, CancellationToken ct);
}
