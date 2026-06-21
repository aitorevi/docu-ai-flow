namespace InvoiceProcessor.Infrastructure.Suppliers;

// The buyer (our own company). It appears on every purchase invoice as the receiver,
// so its tax id / name must never be taken as the supplier's.
public sealed class CompanyOptions
{
    public string? TaxId { get; init; }
    public string? Name { get; init; }
}
