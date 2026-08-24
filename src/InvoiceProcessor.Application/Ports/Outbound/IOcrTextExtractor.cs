namespace InvoiceProcessor.Application.Ports.Outbound;

// Outbound port for OCR text extraction.
// Implementations shell out to external OCR tools; the null-object returns an empty
// string so the caller never needs to check whether OCR is enabled.
public interface IOcrTextExtractor
{
    Task<string> ExtractTextAsync(ReadOnlyMemory<byte> pdfBytes, CancellationToken ct);
}
