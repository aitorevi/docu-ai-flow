using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface ISupplierTrustRepository
{
    Task<SupplierTrust?> GetAsync(string taxId, CancellationToken ct);
    Task SaveAsync(SupplierTrust trust, CancellationToken ct);

    // Every supplier that has ever been confirmed. The dashboard needs the whole set to show
    // which templates are close to earning their autonomy.
    Task<IReadOnlyList<SupplierTrust>> ListAllAsync(CancellationToken ct);
}
