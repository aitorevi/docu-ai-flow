using InvoiceProcessor.Infrastructure.Extraction.Ocr;

namespace InvoiceProcessor.Infrastructure.Extraction.Templates;

// Bound from the "TemplateExtractor" section of appsettings.json.
public sealed class TemplateExtractorOptions
{
    // Minimum number of characters a PDF's extracted text must contain before it is
    // considered "has text". Below this threshold the document is treated as a scan
    // and sent to manual entry (unless the OCR fallback recovers text).
    public int MinTextLength { get; init; } = 50;

    public TemplateEntryOptions[] Templates { get; init; } = [];

    // OCR fallback for scanned PDFs with no text layer. Disabled by default.
    public OcrFallbackOptions OcrFallback { get; init; } = new();
}

public sealed class TemplateEntryOptions
{
    public string SupplierId { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string SupplierTaxId { get; init; } = string.Empty;

    // Text strings that uniquely identify this supplier's invoices (e.g. their legal name,
    // tax id, or a characteristic header). At least one must appear in the PDF text.
    public string[] IdentificationAnchors { get; init; } = [];

    // Map of field key → field template. Recognised keys: invoice_number, issue_date,
    // due_date, net_amount, tax_amount, total_amount.
    public Dictionary<string, FieldEntryOptions> Fields { get; init; } = [];
}

public sealed class FieldEntryOptions
{
    // One or more anchor strings; the repository orders them longest→shortest at load time.
    public string[] Anchors { get; init; } = [];

    // Regex whose first capture group is the field value.
    public string Pattern { get; init; } = string.Empty;
}
