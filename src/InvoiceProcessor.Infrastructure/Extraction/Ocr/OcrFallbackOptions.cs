namespace InvoiceProcessor.Infrastructure.Extraction.Ocr;

// Configuration for the OCR fallback, bound from "TemplateExtractor:OcrFallback".
// Disabled by default so no environment picks up a dependency on external OCR tools
// (poppler + tesseract) unless it opts in.
public sealed class OcrFallbackOptions
{
    public bool Enabled { get; init; }
    public string Language { get; init; } = "spa";
    public int TimeoutSeconds { get; init; } = 60;
    public string? TempDir { get; init; }
}
