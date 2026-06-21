namespace InvoiceProcessor.Domain.Invoices;

public sealed record InvoiceLine(
    string Description,
    decimal Quantity,
    Money UnitPrice,
    Money LineTotal);
