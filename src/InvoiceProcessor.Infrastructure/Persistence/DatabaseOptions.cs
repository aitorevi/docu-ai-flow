namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public string Path { get; init; } = "./data/invoices.db";
}
