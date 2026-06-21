namespace InvoiceProcessor.Infrastructure.Suppliers;

public sealed record SupplierEntry(string CanonicalName, string? TaxId, string[] Aliases);
