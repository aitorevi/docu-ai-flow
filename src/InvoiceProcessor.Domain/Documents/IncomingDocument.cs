namespace InvoiceProcessor.Domain.Documents;

public sealed record IncomingDocument(
    DocumentId Id,
    string FileName,
    string Location,
    string ContentHash,
    DateTimeOffset DetectedAt);
