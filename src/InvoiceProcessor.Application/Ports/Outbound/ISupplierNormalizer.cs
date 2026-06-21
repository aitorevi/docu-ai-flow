using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface ISupplierNormalizer
{
    Supplier Normalize(string? rawName, string? rawTaxId);
}
