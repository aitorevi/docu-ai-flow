using InvoiceProcessor.Application.Ports.Outbound;

namespace InvoiceProcessor.Infrastructure.Extraction.Ocr;

// Null-object implementation used when the OCR fallback is disabled. Always returns an
// empty string, so the caller's "too little text → manual entry" guard fires as before
// and no branch anywhere has to ask whether OCR is switched on.
public sealed class NullOcrTextExtractor : IOcrTextExtractor
{
    public Task<string> ExtractTextAsync(ReadOnlyMemory<byte> pdfBytes, CancellationToken ct) =>
        Task.FromResult(string.Empty);
}
