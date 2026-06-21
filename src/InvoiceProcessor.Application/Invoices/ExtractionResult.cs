namespace InvoiceProcessor.Application.Invoices;

public sealed record ExtractionResult(
    IReadOnlyDictionary<string, ExtractedField> Fields,
    IReadOnlyList<ExtractedLine> Lines,
    decimal OverallConfidence);

public sealed record ExtractedField(string? Value, decimal Confidence);

public sealed record ExtractedLine(
    string? Description, decimal? Quantity, decimal? UnitPrice, decimal? LineTotal);
