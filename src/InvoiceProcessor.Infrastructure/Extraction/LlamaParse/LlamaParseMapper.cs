using System.Text.Json;
using System.Text.RegularExpressions;
using InvoiceProcessor.Application.Invoices;

namespace InvoiceProcessor.Infrastructure.Extraction.LlamaParse;

internal static class LlamaParseMapper
{
    private static readonly Regex TaxIdPattern =
        new(@"(?:CIF|NIF)[:\s]+([A-Z]\d{8}|\d{8}[A-Z])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DateDmY =
        new(@"^(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})$", RegexOptions.Compiled);

    private static readonly Regex AmountWithCurrency =
        new(@"^([\d.,\s]+)\s*([A-Z]{3})$", RegexOptions.Compiled);

    public static ExtractionResult ToExtractionResult(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("pages", out var pages))
            return new ExtractionResult(new Dictionary<string, ExtractedField>(), [], 0m);

        var fields = new Dictionary<string, ExtractedField>();
        var pageConfidences = new List<decimal>();

        foreach (var page in pages.EnumerateArray())
        {
            if (page.TryGetProperty("confidence", out var conf))
                pageConfidences.Add(conf.GetDecimal());

            if (page.TryGetProperty("items", out var items))
                ExtractFromItems(items, fields);
        }

        var overallConfidence = pageConfidences.Count > 0 ? pageConfidences.Average() : 0m;
        return new ExtractionResult(fields, [], overallConfidence);
    }

    private static void ExtractFromItems(JsonElement items, Dictionary<string, ExtractedField> fields)
    {
        var textValues = new List<string>();

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out var typeEl))
                continue;

            switch (typeEl.GetString())
            {
                case "text":
                    var v = item.TryGetProperty("value", out var valEl) ? valEl.GetString()?.Trim() : null;
                    if (!string.IsNullOrEmpty(v)) textValues.Add(v);
                    break;

                case "table":
                    if (item.TryGetProperty("rows", out var rows))
                        ExtractFromRows(rows, fields);
                    break;
            }
        }

        ExtractFromTexts(textValues, fields);
    }

    private static void ExtractFromTexts(List<string> texts, Dictionary<string, ExtractedField> fields)
    {
        foreach (var text in texts)
        {
            var taxMatch = TaxIdPattern.Match(text);
            if (taxMatch.Success)
                fields.TryAdd("supplier_tax_id", new ExtractedField(taxMatch.Groups[1].Value.ToUpperInvariant(), 0.9m));

            if (!fields.ContainsKey("supplier_name") && IsLikelySupplierName(text))
                fields["supplier_name"] = new ExtractedField(text, 0.75m);
        }
    }

    // Heuristic: first short text item that doesn't look like an address, URL or phone.
    private static bool IsLikelySupplierName(string text) =>
        text.Length is > 2 and < 80
        && !text.Contains('@')
        && !text.Contains("www.", StringComparison.OrdinalIgnoreCase)
        && !text.StartsWith("Teléfono", StringComparison.OrdinalIgnoreCase)
        && !text.StartsWith("email:", StringComparison.OrdinalIgnoreCase)
        && !text.StartsWith("C/", StringComparison.OrdinalIgnoreCase)
        && !text.StartsWith("Av", StringComparison.OrdinalIgnoreCase)
        && !char.IsDigit(text[0]);

    private static void ExtractFromRows(JsonElement rows, Dictionary<string, ExtractedField> fields)
    {
        var matrix = rows.EnumerateArray()
            .Select(r => r.EnumerateArray().Select(c => c.GetString()?.Trim() ?? "").ToArray())
            .ToList();

        if (matrix.Count == 0) return;

        if (matrix[0].Length == 2)
        {
            // Two-column key-value table (e.g. "Número: | PV680159")
            foreach (var row in matrix)
                MapKeyValueRow(row[0].TrimEnd(':').Trim(), row[1], fields);
        }
        else if (matrix.Count >= 2)
        {
            // Header row + data rows (e.g. Resumen de Costos)
            var headers = matrix[0];
            for (var i = 1; i < matrix.Count; i++)
                MapHeaderDataRow(headers, matrix[i], fields);
        }
    }

    private static void MapKeyValueRow(string key, string value, Dictionary<string, ExtractedField> fields)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        switch (key.ToLowerInvariant())
        {
            case "número" or "numero" or "nº" or "n.º" or "número de factura" or "factura" or "ref":
                fields.TryAdd("invoice_number", new ExtractedField(value, 0.9m));
                break;
            case "fecha" or "fecha de emisión" or "fecha emisión" or "fecha factura" or "date":
                fields.TryAdd("issue_date", new ExtractedField(NormalizeDate(value), 0.9m));
                break;
            case "vencimiento" or "fecha de vencimiento" or "fecha vto" or "vto":
                fields.TryAdd("due_date", new ExtractedField(NormalizeDate(value), 0.8m));
                break;
            case "cif" or "nif" or "cif/nif":
                fields.TryAdd("supplier_tax_id", new ExtractedField(value.ToUpperInvariant(), 0.9m));
                break;
        }
    }

    private static void MapHeaderDataRow(string[] headers, string[] data, Dictionary<string, ExtractedField> fields)
    {
        for (var i = 0; i < headers.Length && i < data.Length; i++)
        {
            var header = headers[i].ToUpperInvariant();
            var value = data[i];
            if (string.IsNullOrWhiteSpace(value)) continue;

            if (header is "BASE IMPONIBLE" or "BASE" or "IMPONIBLE" or "IMPORTE BASE")
                fields.TryAdd("net_amount", new ExtractedField(NormalizeAmount(value), 0.85m));
            else if (header.StartsWith("IVA") || header is "TAX" or "VAT" or "CUOTA IVA")
                fields.TryAdd("tax_amount", new ExtractedField(NormalizeAmount(value), 0.85m));
            else if (header is "TOTAL" or "TOTAL FACTURA" or "TOTAL A PAGAR" or "IMPORTE TOTAL")
            {
                var (amount, currency) = SplitAmountCurrency(value);
                fields.TryAdd("total_amount", new ExtractedField(NormalizeAmount(amount), 0.9m));
                if (!string.IsNullOrEmpty(currency))
                    fields.TryAdd("currency", new ExtractedField(currency, 0.95m));
            }
        }
    }

    // DD/MM/YYYY or DD-MM-YYYY → YYYY-MM-DD for DateOnly.TryParse compatibility.
    private static string NormalizeDate(string date)
    {
        var m = DateDmY.Match(date);
        return m.Success
            ? $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}"
            : date;
    }

    // "82,85 EUR" → "82.85", "1.234,56" → "1234.56", "68,47" → "68.47"
    private static string NormalizeAmount(string raw)
    {
        var m = AmountWithCurrency.Match(raw);
        var digits = m.Success ? m.Groups[1].Value.Trim() : raw.Trim();

        if (digits.Contains('.') && digits.Contains(','))
            return digits.Replace(".", "").Replace(',', '.');

        return digits.Contains(',') ? digits.Replace(',', '.') : digits;
    }

    private static (string amount, string currency) SplitAmountCurrency(string value)
    {
        var m = AmountWithCurrency.Match(value);
        return m.Success ? (m.Groups[1].Value.Trim(), m.Groups[2].Value) : (value, "");
    }
}
