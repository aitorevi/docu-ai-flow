using System.Globalization;

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

    // Each supplier gets a different look on purpose: three layouts with nothing in common is
    // the whole point of a template-based extractor. The markers (#, ---, tab, |) are layout
    // only — see SampleInvoiceRenderer for why they never reach the extracted text.

    // ── Layout 1 — a classic Spanish invoice: label on the left, amount flush right ──
    public static string Aurora(string number, string issue, string due, decimal net, decimal tax) =>
        "# SUMINISTROS AURORA S.L.\n" +
        "> Poligono Las Salinas, nave 7 - 46940 Manises (Valencia)\n" +
        "CIF: B12345674\n" +
        "---\n" +
        "## FACTURA\n" +
        $"Factura N\u00ba:\t{number}\n" +
        $"Fecha:\t{issue}\n" +
        $"Vencimiento:\t{due}\n" +
        "---\n" +
        "## DETALLE\n" +
        $"Material de oficina y consumibles\t{Es(net)}\n" +
        "---\n" +
        $"Base imponible:\t{Es(net)}\n" +
        $"IVA 21%:\t{Es(tax)}\n" +
        $"Total factura:\t{Es(net + tax)}\n";

    // ── Layout 2 — a utility bill: column tables, values on the line *after* the header ──
    public static string Boreal(string number, string issue, string due, decimal net, decimal tax) =>
        "# ENERGIA BOREAL S.A.\n" +
        "> Suministro electrico - Atencion al cliente 900 123 456\n" +
        "A87654321\n" +
        "---\n" +
        "Documento | Fecha | Vencimiento\n" +
        $"{number} | {issue} | {due}\n" +
        "---\n" +
        "BASE | IVA | TOTAL\n" +
        $"{Es(net)} | {Es(tax)} | {Es(net + tax)}\n";

    // ── Layout 3 — a minimal English invoice with dot leaders and US number formats ──
    public static string Cronos(string number, string issue, decimal net, decimal tax) =>
        "# PAPELERIA CRONOS S.L.U.\n" +
        "> Stationery and print - VAT registered in Spain\n" +
        "NIF B55512345\n" +
        "---\n" +
        $"Invoice no. ....... {number}\n" +
        $"Issue date ........ {issue}\n" +
        "---\n" +
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
        "# TRANSPORTES DESCONOCIDOS S.L.\n" +
        "> Transporte y paqueteria\n" +
        "CIF B99999999\n" +
        "---\n" +
        "Numero de factura:\tTD-2026-77\n" +
        "Fecha de emision:\t03/03/2026\n" +
        "---\n" +
        "Base imponible:\t371,90\n" +
        "IVA 21%:\t78,10\n" +
        "Importe total:\t450,00\n";

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
        await File.WriteAllBytesAsync(Path.Combine(outputDir, "escaneo-sin-texto.pdf"), SampleInvoiceRenderer.RenderScan());
        Console.WriteLine($"{"escaneo-sin-texto.pdf",-32} sin capa de texto → OCR o alta manual");

        Console.WriteLine();
        Console.WriteLine($"Wrote {All.Count + 1} sample invoices to {Path.GetFullPath(outputDir)}");
        return 0;
    }

    // Lays the text out as an invoice. The extractor reads back exactly these lines — see
    // SampleInvoiceRenderer for the invariant that makes that true.
    public static byte[] Render(string text) => SampleInvoiceRenderer.Render(text);

    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-ES");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    private static string Es(decimal value) => value.ToString("N2", Spanish);
    private static string Us(decimal value) => value.ToString("N2", English);
}
