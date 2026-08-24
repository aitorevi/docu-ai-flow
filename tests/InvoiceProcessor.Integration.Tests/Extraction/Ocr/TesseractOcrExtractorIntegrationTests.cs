using InvoiceProcessor.Infrastructure.Extraction.Ocr;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Integration.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Extraction.Ocr;

// Exercises the real pdftoppm + tesseract shell-out. Excluded from CI (poppler/tesseract
// are not installed there): run with `dotnet test --filter Category=RequiresTesseract`.
[Trait("Category", "RequiresTesseract")]
public sealed class TesseractOcrExtractorIntegrationTests
{
    [Fact]
    public async Task ExtractTextAsync_RecoversTextFromRasterisedPdf()
    {
        var opts = new TemplateExtractorOptions
        {
            OcrFallback = new OcrFallbackOptions { Language = "eng", TimeoutSeconds = 60 },
        };
        var extractor = new TesseractOcrExtractor(
            Options.Create(opts), NullLogger<TesseractOcrExtractor>.Instance);

        // A native-text PDF is fine here: pdftoppm rasterises it to pixels and tesseract
        // must read the text back, which proves the shell-out chain end-to-end.
        var pdf = SyntheticPdf.WithText("INVOICE NUMBER 12345");

        var text = await extractor.ExtractTextAsync(pdf, CancellationToken.None);

        Assert.Contains("12345", text);
    }
}
