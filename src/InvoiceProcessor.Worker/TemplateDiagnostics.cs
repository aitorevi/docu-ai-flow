using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Extraction.Templates;

namespace InvoiceProcessor.Worker;

// Diagnostics for authoring and tuning invoice templates without starting the Worker.
//
// Writing a template is an iterative game: dump the text a PDF really contains, pick anchors,
// write patterns, then check them against a pile of invoices. These two commands are that loop.
public static class TemplateDiagnostics
{
    private static readonly string[] RequiredFields = ["invoice_number", "issue_date", "net_amount"];

    // dotnet run -- dump-text <pdf>
    // Prints the exact text the extractor sees, so anchors can be chosen from reality rather
    // than from what the PDF looks like in a viewer.
    public static async Task<int> DumpTextAsync(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            await Console.Error.WriteLineAsync($"File not found: {pdfPath}");
            return 1;
        }

        Console.WriteLine(TemplateInvoiceExtractor.ExtractText(await File.ReadAllBytesAsync(pdfPath)));
        return 0;
    }

    // dotnet run -- template-check <pdf-or-folder>
    // Reports, per PDF, which template matched and which fields it captured, and finishes with a
    // success ratio. Run it over a folder of archived invoices to see what a template change costs.
    public static async Task<int> CheckAsync(
        string target, TemplateExtractorOptions options, IInvoiceTemplateRepository templates)
    {
        var pdfs = File.Exists(target)
            ? [target]
            : Directory.Exists(target)
                ? Directory.EnumerateFiles(target, "*.pdf", SearchOption.AllDirectories).OrderBy(f => f).ToArray()
                : [];

        if (pdfs.Length == 0)
        {
            await Console.Error.WriteLineAsync($"No PDF files found in: {target}");
            return 1;
        }

        int total = 0, coherent = 0, incoherent = 0;

        foreach (var pdf in pdfs)
        {
            total++;
            var name = Path.GetFileName(pdf);

            string text;
            try
            {
                text = TemplateInvoiceExtractor.ExtractText(await File.ReadAllBytesAsync(pdf));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]  {name} — {ex.Message}");
                continue;
            }

            if (text.Length < options.MinTextLength)
            {
                Console.WriteLine($"[SCAN]   {name} — no text layer ({text.Length} chars)");
                continue;
            }

            var matched = FindTemplate(text, templates);
            if (matched is null)
            {
                Console.WriteLine($"[NONE]   {name} — no template matched");
                continue;
            }

            // A field counts as missing when it fails to capture OR captures something that does
            // not normalise (a date whose format ParseDate rejects, say) — mirroring the real
            // extractor's mandatory-field gate so template-check never reports a false [OK].
            var captured = new Dictionary<string, string?>();
            var missing = new List<string>();
            foreach (var (key, field) in matched.Fields)
            {
                var raw = TemplateFieldParser.FindAnchorAndCapture(text, field.Anchors, field.Pattern);
                captured[key] = raw;
                if (raw is null || !Normalises(key, raw)) missing.Add(key);
            }

            var missingRequired = missing.Where(RequiredFields.Contains).ToArray();
            if (missingRequired.Length > 0)
            {
                Console.WriteLine($"[MISS]   {name} — {matched.SupplierName}");
                Console.WriteLine($"         required missing: {string.Join(", ", missingRequired)}");
            }
            else
            {
                // Correctness gate: a template can capture every required field and still capture
                // the wrong numbers. Cross-checking net + tax = total is what makes [OK] mean
                // "the values are sane", not merely "the values are present".
                var coherence = TemplateFieldParser.CheckAmountCoherence(
                    captured.GetValueOrDefault("net_amount"),
                    captured.GetValueOrDefault("tax_amount"),
                    captured.GetValueOrDefault("total_amount"));

                if (coherence.Kind == CoherenceKind.Incoherent)
                {
                    incoherent++;
                    Console.WriteLine($"[BAD]    {name} — {matched.SupplierName}");
                    Console.WriteLine($"         net+tax≠total: {coherence.Net:0.00} + {coherence.Tax:0.00} ≠ {coherence.Total:0.00} (diff {coherence.Diff:0.00})");
                }
                else
                {
                    coherent++;
                    var note = coherence.Kind == CoherenceKind.Uncrossable
                        ? "  (no cross-check: missing tax/total)" : "";
                    Console.WriteLine($"[OK]     {name} — {matched.SupplierName}{note}");
                    if (missing.Count > 0)
                        Console.WriteLine($"         optional missing: {string.Join(", ", missing)}");
                }
            }

            // Always print the captured values so correctness can be verified at a glance.
            foreach (var (key, value) in captured)
                Console.WriteLine($"         {(value is null ? "  NULL  " : "        ")}{key}: {value ?? "(not captured)"}");
        }

        Console.WriteLine();
        Console.WriteLine($"Result: {coherent}/{total} coherent ({100.0 * coherent / total:F0}%)  ·  {incoherent} incoherent [BAD]");
        return incoherent > 0 ? 1 : 0;
    }

    private static InvoiceTemplate? FindTemplate(string text, IInvoiceTemplateRepository templates) =>
        templates.GetAll().FirstOrDefault(t =>
            t.IdentificationAnchors.Any(a => text.Contains(a, StringComparison.OrdinalIgnoreCase)));

    // Mirrors TemplateInvoiceExtractor.NormaliseFieldValue: does the captured raw value survive
    // normalisation? Dates must parse, amounts must parse, others just have to be non-empty.
    private static bool Normalises(string key, string raw) => key switch
    {
        "net_amount" or "tax_amount" or "total_amount" =>
            TemplateFieldParser.ParseSpanishNumber(raw) is not null,
        "issue_date" or "due_date" =>
            TemplateFieldParser.ParseDate(raw) is not null,
        _ => !string.IsNullOrWhiteSpace(raw),
    };
}
