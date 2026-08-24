using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace InvoiceProcessor.Infrastructure.Extraction.Templates;

// Local invoice extractor that works entirely on-device: it reads the PDF's text layer
// with PdfPig, identifies the supplier via template identification anchors (longest→shortest),
// then extracts each field using the corresponding anchor + regex pattern.
//
// No network, no credentials, no per-page cost. Where Document AI generalises across layouts
// it has never seen, this one only knows the suppliers it has been taught — and says so
// instead of guessing: when the document cannot be matched (no text, scarce text, no template,
// required fields missing) it sets RequiresManualEntry = true and a human takes over.
public sealed class TemplateInvoiceExtractor(
    IOptions<TemplateExtractorOptions> opts,
    IInvoiceTemplateRepository templateRepository,
    IOcrTextExtractor ocrExtractor) : IInvoiceDataExtractor
{
    private readonly int _minTextLength = opts.Value.MinTextLength;

    public async Task<ExtractionResult> ExtractAsync(DocumentContent content, CancellationToken ct)
    {
        // Buffer the stream once: PdfPig reads it to the end, and the OCR fallback needs the
        // same bytes again (and the stream is not guaranteed seekable).
        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await content.Stream.CopyToAsync(ms, ct);
            bytes = ms.ToArray();
        }

        var text = ExtractText(bytes);
        var usedOcr = false;

        // No usable text layer → try OCR (scans). Any OCR failure degrades to manual entry.
        if (text.Length < _minTextLength)
        {
            string ocrText;
            try { ocrText = await ocrExtractor.ExtractTextAsync(bytes, ct); }
            catch { return ManualEntry(); }

            if (ocrText.Length < _minTextLength)
                return ManualEntry();

            text = ocrText;
            usedOcr = true;
        }

        var template = FindTemplate(text);
        if (template is null)
            return ManualEntry();

        // Flag OCR-sourced extractions so the reviewer knows to double-check the digits.
        return ExtractWithTemplate(text, template) with { SourcedFromOcr = usedOcr };
    }

    // Uses PdfPig to extract text from the PDF, reconstructing lines by grouping words
    // that share a similar Y coordinate. This produces newline-separated text that regular
    // expressions can search line-by-line, avoiding false matches that occur when PdfPig's
    // page.Text concatenates adjacent lines without separators.
    // Public: also used by the "template-check" / "dump-text" CLI diagnostics so they preview
    // the exact same text the real extractor sees.
    public static string ExtractText(byte[] pdfBytes)
    {
        try
        {
            // PdfPig may throw on corrupt or purely image-based PDFs;
            // treat any failure as "no text".
            using var doc = PdfDocument.Open(pdfBytes);
            var sb = new System.Text.StringBuilder();
            foreach (var page in doc.GetPages())
            {
                var words = page.GetWords().ToList();
                if (words.Count == 0) continue;

                // Group words into lines by Y position (nearest-integer bucket)
                var lines = words
                    .GroupBy(w => (int)Math.Round(w.BoundingBox.Bottom))
                    .OrderByDescending(g => g.Key)   // PDF Y grows upward
                    .Select(g => string.Join(" ", g
                        .OrderBy(w => w.BoundingBox.Left)
                        .Select(w => w.Text)));

                // Always join with a bare "\n", never Environment.NewLine: templates are
                // authored and tested on LF, and their patterns anchor on a literal "\n".
                // AppendLine would emit "\r\n" on Windows, silently breaking any pattern
                // that requires a line break right after the captured value.
                foreach (var line in lines)
                    sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    // Tries each template's identification anchors (already sorted longest→shortest by the
    // repository) against the PDF text. Returns the first matching template, or null.
    private InvoiceTemplate? FindTemplate(string text)
    {
        foreach (var template in templateRepository.GetAll())
        {
            foreach (var anchor in template.IdentificationAnchors)
            {
                if (text.Contains(anchor, StringComparison.OrdinalIgnoreCase))
                    return template;
            }
        }
        return null;
    }

    // Monetary fields whose label repeats per page on multi-page invoices; anchored on the last occurrence.
    private static readonly HashSet<string> AmountFieldKeys =
        ["net_amount", "tax_amount", "total_amount"];

    // Extracts all configured fields from the PDF text using the template's anchors + patterns.
    // Supplier name and tax ID are injected directly from the template with confidence 1.0.
    private static ExtractionResult ExtractWithTemplate(string text, InvoiceTemplate template)
    {
        var fields = new Dictionary<string, ExtractedField>
        {
            // Supplier fields come from the template definition itself — they are canonical
            // and require no regex extraction, so confidence is always 1.0.
            ["supplier_name"]   = new(template.SupplierName, 1m),
            ["supplier_tax_id"] = new(template.SupplierTaxId, 1m),
        };

        int matched = 0, total = template.Fields.Count;

        foreach (var (fieldKey, fieldTemplate) in template.Fields)
        {
            // Amount labels repeat as a per-page header on multi-page invoices while the totals sit
            // on the last page, so anchor amounts on the LAST occurrence (a no-op when it appears once).
            var raw = TemplateFieldParser.FindAnchorAndCapture(
                text, fieldTemplate.Anchors, fieldTemplate.Pattern,
                matchLastAnchor: AmountFieldKeys.Contains(fieldKey));

            if (raw is null)
            {
                fields[fieldKey] = new(null, 0m);
                continue;
            }

            // Normalise the value: parse numbers to invariant decimal strings,
            // and parse dates to ISO format.
            var normalised = NormaliseFieldValue(fieldKey, raw);
            fields[fieldKey] = new(normalised, normalised is not null ? 1m : 0m);
            if (normalised is not null) matched++;
        }

        // Overall confidence = fraction of fields successfully extracted (0.0 – 1.0).
        var overallConfidence = total == 0 ? 1m : (decimal)matched / total;

        // Safety net: if any of the three mandatory fields could not be extracted, mark the
        // result as requiring manual entry. A template that identifies the supplier but cannot
        // parse its fields is better treated as "not understood" than as a hard error — the
        // invoice is real, it is the template that is behind.
        bool missingMandatory =
            !fields.TryGetValue("invoice_number", out var invNum) || invNum.Value is null ||
            !fields.TryGetValue("issue_date",     out var issDate) || issDate.Value is null ||
            !fields.TryGetValue("net_amount",     out var net)     || net.Value is null;

        if (missingMandatory)
            return ManualEntry();

        return new ExtractionResult(fields, [], overallConfidence, RequiresManualEntry: false);
    }

    private static string? NormaliseFieldValue(string fieldKey, string raw) => fieldKey switch
    {
        "net_amount" or "tax_amount" or "total_amount" =>
            TemplateFieldParser.ParseSpanishNumber(raw)?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
        "issue_date" or "due_date" =>
            TemplateFieldParser.ParseDate(raw),
        // Invoice numbers are normalised by stripping all whitespace: suppliers print them with
        // stray spaces (e.g. "SF 2603247") that carry no meaning, so "SF 2603247" → "SF2603247".
        "invoice_number" =>
            System.Text.RegularExpressions.Regex.Replace(raw, @"\s+", string.Empty),
        _ => raw,
    };

    private static ExtractionResult ManualEntry() =>
        new(new Dictionary<string, ExtractedField>(), [], 0m, RequiresManualEntry: true);
}
