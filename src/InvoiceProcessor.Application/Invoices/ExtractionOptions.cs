namespace InvoiceProcessor.Application.Invoices;

public sealed class ExtractionOptions
{
    public decimal ConfidenceThreshold { get; init; } = 0.6m;
}
