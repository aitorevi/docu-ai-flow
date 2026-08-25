using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface ISupplierTrustRepository
{
    Task<SupplierTrust?> GetAsync(string taxId, CancellationToken ct);
    Task SaveAsync(SupplierTrust trust, CancellationToken ct);
}
