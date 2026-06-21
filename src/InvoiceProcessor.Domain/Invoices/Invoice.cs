namespace InvoiceProcessor.Domain.Invoices;

using SharpMonads.Core;

public sealed class Invoice
{
    public InvoiceId Id { get; }
    public string InvoiceNumber { get; }
    public Supplier Supplier { get; }
    public DateOnly IssueDate { get; }
    public DateOnly? DueDate { get; }
    public Money NetAmount { get; }
    public Money TaxAmount { get; }
    public Money TotalAmount { get; }
    public IReadOnlyList<InvoiceLine> Lines { get; }

    private Invoice(InvoiceId id, string invoiceNumber, Supplier supplier, DateOnly issueDate,
        DateOnly? dueDate, Money netAmount, Money taxAmount, Money totalAmount, IReadOnlyList<InvoiceLine> lines)
    {
        Id = id; InvoiceNumber = invoiceNumber; Supplier = supplier; IssueDate = issueDate; DueDate = dueDate;
        NetAmount = netAmount; TaxAmount = taxAmount; TotalAmount = totalAmount; Lines = lines;
    }

    public static Result<Invoice, Error> Create(
        InvoiceId id, string invoiceNumber, Supplier supplier, DateOnly issueDate, DateOnly? dueDate,
        Money netAmount, Money taxAmount, Money totalAmount, IReadOnlyList<InvoiceLine> lines)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return Result<Invoice, Error>.Failure(Error.Validation("El número de factura es obligatorio."));

        return netAmount.Add(taxAmount).Bind(expected =>
            Math.Abs(expected.Amount - totalAmount.Amount) > 0.01m
                ? Result<Invoice, Error>.Failure(Error.Validation(
                    $"Total incoherente: {netAmount.Amount} + {taxAmount.Amount} ≠ {totalAmount.Amount}."))
                : Result<Invoice, Error>.Success(new Invoice(
                    id, invoiceNumber, supplier, issueDate, dueDate, netAmount, taxAmount, totalAmount, lines)));
    }
}
