namespace InvoiceProcessor.Domain.Invoices;

public readonly record struct InvoiceId(Guid Value)
{
    public static InvoiceId New() => new(Guid.NewGuid());
}
