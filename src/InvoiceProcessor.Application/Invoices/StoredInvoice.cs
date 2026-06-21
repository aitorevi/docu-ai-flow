using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Invoices;

public sealed record StoredInvoice(
    string ContentHash, string InvoiceNumber, string SupplierName, string? SupplierTaxId,
    DateOnly IssueDate, DateOnly? DueDate, decimal NetAmount, decimal TaxAmount, decimal TotalAmount,
    string Currency, string ArchivedPath, int? DeclaredYear = null, int? DeclaredQuarter = null)
{
    public static StoredInvoice From(Invoice i, string hash, string archivedPath) => new(
        hash, i.InvoiceNumber, i.Supplier.Name, i.Supplier.TaxId, i.IssueDate, i.DueDate,
        i.NetAmount.Amount, i.TaxAmount.Amount, i.TotalAmount.Amount, i.TotalAmount.Currency, archivedPath);

    public Quarter RealQuarter => Quarter.Real(IssueDate);
}
