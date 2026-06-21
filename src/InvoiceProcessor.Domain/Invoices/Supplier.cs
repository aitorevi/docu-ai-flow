namespace InvoiceProcessor.Domain.Invoices;

public sealed record Supplier(string Name, string? TaxId, string? Address);
