using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace InvoiceProcessor.Worker;

// Fictional invoices used to demo the pipeline end to end without a single credential.
// Real invoices are never checked into this repo, so these are generated from plain text.
//
// The set is chosen to walk every branch a newcomer should see: three suppliers whose layouts
// have nothing in common, one supplier nobody has written a template for, and a scan with no
// text layer at all.
public static class SampleInvoices
{
    public sealed record Sample(string FileName, string Text, string Expectation);

    // Layout 1 — plain "label: value" lines, Spanish number and date formats.
    public const string AuroraText =
        "SUMINISTROS AURORA S.L.\n" +
        "CIF: B12345674\n" +
        "Factura Nº: A-2026/0148\n" +
        "Fecha: 12/01/2026\n" +
        "Vencimiento: 11/02/2026\n" +
        "Base imponible: 1.240,50\n" +
        "IVA 21%: 260,51\n" +
        "Total factura: 1.501,01\n";

    // Layout 2 — a column table: the values sit on the line *after* the header.
    public const string BorealText =
        "ENERGIA BOREAL S.A.\n" +
        "A87654321\n" +
        "Documento Fecha Vencimiento\n" +
        "FR-2026-0092 05/02/2026 07/03/2026\n" +
        "BASE IVA TOTAL\n" +
        "980,00 205,80 1.185,80\n";

    // Layout 3 — dot leaders, US number format and an English abbreviated month.
    public const string CronosText =
        "PAPELERIA CRONOS S.L.U.\n" +
        "NIF B55512345\n" +
        "Invoice no. ....... C/26/0311\n" +
        "Issue date ........ Feb 18, 2026\n" +
        "Net .............. 1,320.00\n" +
        "VAT (21%) ........ 277.20\n" +
        "Amount due ....... 1,597.20\n";

    // No template has been written for this supplier: the extractor must refuse to guess.
    public const string UnknownSupplierText =
        "TRANSPORTES DESCONOCIDOS S.L.\n" +
        "CIF B99999999\n" +
        "Numero de factura: TD-2026-77\n" +
        "Fecha de emision: 03/03/2026\n" +
        "Base imponible: 371,90\n" +
        "IVA 21%: 78,10\n" +
        "Importe total: 450,00\n";

    public static IReadOnlyList<Sample> All =>
    [
        new("aurora-A-2026-0148.pdf", AuroraText, "extracted and archived automatically"),
        new("boreal-FR-2026-0092.pdf", BorealText, "extracted and archived automatically"),
        new("cronos-C-26-0311.pdf", CronosText, "extracted and archived automatically"),
        new("desconocido-TD-2026-77.pdf", UnknownSupplierText, "no template matched → manual entry"),
    ];

    // dotnet run -- make-samples [outputDir]
    // Regenerates the sample PDFs. They are committed to data/samples/ so a fresh clone can just
    // copy them into the inbox, but keeping the generator means the demo is reproducible and the
    // texts stay in one place — the tests assert against these same constants.
    public static async Task<int> GenerateAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        foreach (var sample in All)
        {
            var path = Path.Combine(outputDir, sample.FileName);
            await File.WriteAllBytesAsync(path, Render(sample.Text));
            Console.WriteLine($"{sample.FileName,-32} {sample.Expectation}");
        }

        // A scan: a valid PDF with no text layer at all. PdfPig recovers nothing from it, so it
        // exercises the OCR fallback when enabled and manual entry when not.
        var scanPath = Path.Combine(outputDir, "escaneo-sin-texto.pdf");
        await File.WriteAllBytesAsync(scanPath, RenderImageOnlyPage());
        Console.WriteLine($"{"escaneo-sin-texto.pdf",-32} no text layer → OCR fallback or manual entry");

        Console.WriteLine();
        Console.WriteLine($"Wrote {All.Count + 1} sample invoices to {Path.GetFullPath(outputDir)}");
        return 0;
    }

    // Builds a single-page PDF whose text layer contains exactly the given lines.
    public static byte[] Render(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var y = 750;
        foreach (var line in text.Split('\n'))
        {
            if (line.Length > 0) page.AddText(line, 10, new PdfPoint(50, y), font);
            y -= 18;
        }

        return builder.Build();
    }

    // A page with geometry but no text — the shape of a scanned document.
    private static byte[] RenderImageOnlyPage()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.DrawRectangle(new PdfPoint(50, 600), 500, 180, 1);
        page.DrawRectangle(new PdfPoint(50, 400), 500, 150, 1);
        return builder.Build();
    }
}
