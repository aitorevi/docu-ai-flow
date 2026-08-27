using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace InvoiceProcessor.Worker;

// Turns the plain text of a sample invoice into a PDF that looks like an invoice.
//
// The critical invariant: **what is drawn is exactly what the extractor reads back**. The
// extractor groups words by their Y coordinate and joins them with single spaces, so a row laid
// out as columns comes back as one space-separated line — the same line the templates were
// written against. Markers (`#`, `---`, `\t`, `|`) are layout only and are never drawn, so they
// never reach the extracted text either.
//
// That is what lets these files be pretty and still be the thing the tests assert on.
internal static class SampleInvoiceRenderer
{
    private const double PageWidth = 595;   // A4, points
    private const double Left = 56;
    private const double Right = PageWidth - 56;
    private const double Top = 780;

    private const double BodySize = 10;
    private const double HeadingSize = 15;
    private const double LabelSize = 9;
    private const double MutedSize = 8.5;

    public static byte[] Render(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var regular = builder.AddStandard14Font(Standard14Font.Helvetica);
        var bold = builder.AddStandard14Font(Standard14Font.HelveticaBold);

        var y = Top;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd();

            if (line.Length == 0) { y -= 10; continue; }

            // A rule: pure decoration, draws no glyphs, so the extractor sees nothing here and
            // the lines either side of it stay adjacent — which some templates depend on.
            if (line == "---")
            {
                page.SetStrokeColor(210, 214, 220);
                page.DrawLine(new PdfPoint(Left, y + 4), new PdfPoint(Right, y + 4), 0.75m);
                page.ResetColor();
                y -= 18;
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                page.AddText(line[2..], (decimal)HeadingSize, new PdfPoint(Left, y), bold);
                y -= 24;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                page.AddText(line[3..], (decimal)LabelSize, new PdfPoint(Left, y), bold);
                y -= 20;
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                page.SetTextAndFillColor(120, 125, 135);
                page.AddText(line[2..], (decimal)MutedSize, new PdfPoint(Left, y), regular);
                page.ResetColor();
                y -= 15;
                continue;
            }

            // "label\tvalue" — label at the left margin, value flush right. Both sit on the same
            // baseline, so the extractor reads back "label value".
            if (line.Contains('\t'))
            {
                var parts = line.Split('\t', 2);
                var label = parts[0].Trim();
                var value = parts[1].Trim();
                page.AddText(label, (decimal)BodySize, new PdfPoint(Left, y), regular);
                var width = MeasureWidth(page, value, BodySize, regular);
                page.AddText(value, (decimal)BodySize, new PdfPoint(Right - width, y), bold);
                y -= 17;
                continue;
            }

            // "a | b | c" — evenly spaced columns on one baseline, read back as "a b c".
            if (line.Contains('|'))
            {
                var cells = line.Split('|').Select(c => c.Trim()).ToArray();
                var step = (Right - Left) / cells.Length;
                for (var i = 0; i < cells.Length; i++)
                {
                    if (cells[i].Length == 0) continue;
                    page.AddText(cells[i], (decimal)BodySize, new PdfPoint(Left + i * step, y), regular);
                }
                y -= 17;
                continue;
            }

            page.AddText(line, (decimal)BodySize, new PdfPoint(Left, y), regular);
            y -= 17;
        }

        return builder.Build();
    }

    // A page with geometry but no text at all — the shape of a scanned document.
    public static byte[] RenderScan()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.SetStrokeColor(205, 209, 215);
        page.DrawRectangle(new PdfPoint(Left, 560), 483, 200, 0.75m);
        page.DrawRectangle(new PdfPoint(Left, 380), 483, 150, 0.75m);
        page.DrawLine(new PdfPoint(Left, 330), new PdfPoint(Right, 330), 0.75m);
        page.ResetColor();
        return builder.Build();
    }

    private static double MeasureWidth(
        PdfPageBuilder page, string text, double size, PdfDocumentBuilder.AddedFont font)
    {
        var letters = page.MeasureText(text, (decimal)size, new PdfPoint(0, 0), font);
        return letters.Count == 0 ? 0 : letters[^1].GlyphRectangle.Right;
    }
}
