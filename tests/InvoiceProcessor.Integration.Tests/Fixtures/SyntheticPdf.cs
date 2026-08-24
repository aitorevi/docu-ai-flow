using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace InvoiceProcessor.Integration.Tests.Fixtures;

// Builds synthetic PDFs for use in tests. Real invoice PDFs are never checked into the repo,
// so PdfPig's writer produces real (but minimal) PDF files in memory instead.
public static class SyntheticPdf
{
    // Creates a PDF containing the specified text on a single page, using a standard
    // font so PdfPig's reader can extract the content back out.
    public static byte[] WithText(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var y = 750;
        foreach (var line in text.Split('\n'))
        {
            page.AddText(line, 10, new UglyToad.PdfPig.Core.PdfPoint(50, y), font);
            y -= 15;
        }

        return builder.Build();
    }

    // Creates a minimal PDF with no text content, simulating a scanned document
    // (image-only, no OCR text layer).
    public static byte[] WithNoText() => MinimalPdf.Bytes();
}
