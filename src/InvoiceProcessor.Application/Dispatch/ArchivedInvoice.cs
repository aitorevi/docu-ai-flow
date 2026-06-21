namespace InvoiceProcessor.Application.Dispatch;

public sealed record ArchivedInvoice(string FilePath, string ContentHash, DateOnly? InvoiceDate = null);
