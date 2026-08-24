namespace InvoiceProcessor.Application.Invoices;

public sealed record ExtractionResult(
    IReadOnlyDictionary<string, ExtractedField> Fields,
    IReadOnlyList<ExtractedLine> Lines,
    decimal OverallConfidence,
    // True when the document could not be understood at all (no text, no matching template,
    // required fields missing). The caller parks the invoice for a human instead of guessing.
    bool RequiresManualEntry = false,
    // True when the text came from the OCR fallback (a scan). OCR can misread digits, so
    // these invoices deserve closer human checking.
    bool SourcedFromOcr = false);

public sealed record ExtractedField(string? Value, decimal Confidence);

public sealed record ExtractedLine(
    string? Description, decimal? Quantity, decimal? UnitPrice, decimal? LineTotal);
