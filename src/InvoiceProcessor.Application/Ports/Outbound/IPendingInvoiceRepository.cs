using InvoiceProcessor.Application.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IPendingInvoiceRepository
{
    Task SaveAsync(PendingInvoice pending, CancellationToken ct);
    IAsyncEnumerable<PendingInvoice> ListAllAsync(CancellationToken ct);
    Task<PendingInvoice?> FindByContentHashAsync(string contentHash, CancellationToken ct);
    Task DeleteAsync(string contentHash, CancellationToken ct);
    // Number of pending invoices for a supplier — feeds the "N pending for this supplier" badge.
    Task<int> CountByTaxIdAsync(string? taxId, CancellationToken ct);
}
