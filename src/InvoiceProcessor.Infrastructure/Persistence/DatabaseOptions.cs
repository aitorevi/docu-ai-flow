namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    // Rewritten to an absolute path against the data root at startup — see DataRoot.
    public string Path { get; set; } = "./data/invoices.db";
}
