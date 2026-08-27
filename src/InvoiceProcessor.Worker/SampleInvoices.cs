using System.Globalization;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace InvoiceProcessor.Worker;

// Fictional invoices used to demo the pipeline end to end without a single credential.
// Real invoices are never checked into this repo, so these are generated from plain text.
//
// The set walks every branch a newcomer should see: three supplier layouts with nothing in
// common, a supplier nobody has written a template for, and a scan with no text layer.
//
// Aurora deliberately appears three times. With one invoice per supplier the headline idea of
// the project — a supplier earning the right to skip review — is unreachable in a demo: you
// confirm once, read "1 de 3", and never see it happen. Three lets you watch it happen.
public static class SampleInvoices
{
    // Extracts = the shipped templates should read this one cleanly. Asserted by
    // ShippedDemoTemplatesTests, so a broken sample fails the build rather than the demo.
    public sealed record Sample(string FileName, string Text, bool Extracts, string Expectation);

    // ── Layout 1 — plain "label: value" lines, Spanish number and date formats ──
    public static string Aurora(string number, string issue, string due, decimal net, decimal tax) =>
        "SUMINISTROS AURORA S.L.\n" +
        "CIF: B12345674\n" +
        $"Factura Nº: {number}\n" +
        $"Fecha: {issue}\n" +
        $"Vencimiento: {due}\n" +
        $"Base imponible: {Es(net)}\n" +
        $"IVA 21%: {Es(tax)}\n" +
        $"Total factura: {Es(net + tax)}\n";

    // ── Layout 2 — a column table: the values sit on the line *after* the header ──
    public static string Boreal(string number, string issue, string due, decimal net, decimal tax) =>
        "ENERGIA BOREAL S.A.\n" +
        "A87654321\n" +
        "Documento Fecha Vencimiento\n" +
        $"{number} {issue} {due}\n" +
        "BASE IVA TOTAL\n" +
        $"{Es(net)} {Es(tax)} {Es(net + tax)}\n";

    // ── Layout 3 — dot leaders, US number format, English abbreviated month ──
    public static string Cronos(string number, string issue, decimal net, decimal tax) =>
        "PAPELERIA CRONOS S.L.U.\n" +
        "NIF B55512345\n" +
        $"Invoice no. ....... {number}\n" +
        $"Issue date ........ {issue}\n" +
        $"Net .............. {Us(net)}\n" +
        $"VAT (21%) ........ {Us(tax)}\n" +
        $"Amount due ....... {Us(net + tax)}\n";

    // The canonical example of each layout — the shipped-template tests assert against these.
    public const string AuroraTextNumber = "A-2026/0148";
    public static string AuroraText => Aurora(AuroraTextNumber, "12/01/2026", "11/02/2026", 1240.50m, 260.51m);
    public static string BorealText => Boreal("FR-2026-0092", "05/02/2026", "07/03/2026", 980.00m, 205.80m);
    public static string CronosText => Cronos("C/26/0311", "Feb 18, 2026", 1320.00m, 277.20m);

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
        // Three from the same supplier, so confirming all three walks Aurora from
        // "en revisión" to "automático" at the default demo threshold.
        new("aurora-A-2026-0148.pdf", AuroraText, true, "se extrae y espera revisión"),
        new("aurora-A-2026-0151.pdf", Aurora("A-2026/0151", "26/01/2026", "25/02/2026", 480.00m, 100.80m),
            true, "se extrae y espera revisión"),
        new("aurora-A-2026-0163.pdf", Aurora("A-2026/0163", "09/02/2026", "11/03/2026", 2015.75m, 423.31m),
            true, "se extrae y espera revisión"),

        new("boreal-FR-2026-0092.pdf", BorealText, true, "se extrae y espera revisión"),
        new("boreal-FR-2026-0104.pdf", Boreal("FR-2026-0104", "05/03/2026", "04/04/2026", 1120.00m, 235.20m),
            true, "se extrae y espera revisión"),

        new("cronos-C-26-0311.pdf", CronosText, true, "se extrae y espera revisión"),

        new("desconocido-TD-2026-77.pdf", UnknownSupplierText, false, "sin plantilla → alta manual"),
    ];

    // dotnet run -- make-samples [outputDir]
    // Regenerates the sample PDFs. They are committed to data/samples/ so a fresh clone can just
    // copy them into the inbox, but keeping the generator means the demo is reproducible and the
    // texts stay in one place — the tests assert against these same builders.
    public static async Task<int> GenerateAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        foreach (var sample in All)
        {
            await File.WriteAllBytesAsync(Path.Combine(outputDir, sample.FileName), Render(sample.Text));
            Console.WriteLine($"{sample.FileName,-32} {sample.Expectation}");
        }

        // A scan: a valid PDF with no text layer at all. PdfPig recovers nothing from it, so it
        // exercises the OCR fallback when enabled and manual entry when not.
        await File.WriteAllBytesAsync(Path.Combine(outputDir, "escaneo-sin-texto.pdf"), RenderImageOnlyPage());
        Console.WriteLine($"{"escaneo-sin-texto.pdf",-32} sin capa de texto → OCR o alta manual");

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

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static string Es(decimal value) => value.ToString("N2", Spanish);
    private static string Us(decimal value) => value.ToString("N2", English);
}
