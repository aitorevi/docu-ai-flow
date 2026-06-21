using System.Text.Json;
using System.Text.RegularExpressions;
using InvoiceProcessor.Application.Invoices;

namespace InvoiceProcessor.Infrastructure.Extraction.DocumentAi;

// Maps Document AI Invoice Parser entities to our ExtractionResult.
// Entity types come from the Invoice Parser schema (invoice_id, invoice_date, …).
internal static partial class GoogleDocumentAiMapper
{
    // Document AI entity type → our field key. Dates and supplier identity are handled separately.
    private static readonly Dictionary<string, string> FieldByEntityType = new()
    {
        ["invoice_id"] = "invoice_number",
        ["net_amount"] = "net_amount",
        ["total_amount"] = "total_amount",
        ["total_tax_amount"] = "tax_amount",
        ["currency"] = "currency",
    };

    private static readonly Dictionary<string, int> SpanishMonths = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enero"] = 1, ["febrero"] = 2, ["marzo"] = 3, ["abril"] = 4, ["mayo"] = 5, ["junio"] = 6,
        ["julio"] = 7, ["agosto"] = 8, ["septiembre"] = 9, ["setiembre"] = 9, ["octubre"] = 10,
        ["noviembre"] = 11, ["diciembre"] = 12,
    };

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")] private static partial Regex IsoDate();
    [GeneratedRegex(@"(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})")] private static partial Regex NumericDate();
    [GeneratedRegex(@"(\d{1,2})\s+de\s+([A-Za-zÁÉÍÓÚáéíóú]+)\s+de\s+(\d{4})")] private static partial Regex SpanishDate();

    public static ExtractionResult ToExtractionResult(string json, string? ownTaxId = null, string? ownName = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var fields = new Dictionary<string, ExtractedField>();
        var confidences = new List<decimal>();

        if (!root.TryGetProperty("entities", out var entities))
            return new ExtractionResult(fields, [], 0m);

        string? supplierName = null, supplierTaxId = null, receiverTaxId = null, receiverName = null;
        decimal supplierNameConf = 0m, supplierTaxConf = 0m;
        decimal vatBase = 0m, vatTax = 0m; // summed across vat lines (one per IVA rate)

        foreach (var entity in entities.EnumerateArray())
        {
            var type = entity.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (type is null) continue;

            // The "vat" entity carries the IVA breakdown as typed sub-properties; some layouts
            // expose the base/cuota ONLY here, not as top-level net_amount/total_tax_amount.
            if (type == "vat")
            {
                vatBase += SumProperty(entity, "vat/amount");
                vatTax += SumProperty(entity, "vat/tax_amount");
                continue;
            }

            var value = NormalizedOrMention(entity);
            if (string.IsNullOrWhiteSpace(value)) continue;
            var conf = entity.TryGetProperty("confidence", out var c) ? (decimal)c.GetDouble() : 0m;

            switch (type)
            {
                case "supplier_name": supplierName = value; supplierNameConf = conf; break;
                case "supplier_tax_id": supplierTaxId = value; supplierTaxConf = conf; break;
                case "receiver_tax_id": receiverTaxId = value; break;
                case "receiver_name": receiverName = value; break;
                case "invoice_date" when NormalizeDate(entity) is { } issued && fields.TryAdd("issue_date", new ExtractedField(issued, conf)):
                    confidences.Add(conf); break;
                case "due_date" when NormalizeDate(entity) is { } due:
                    fields.TryAdd("due_date", new ExtractedField(due, conf)); break;
                default:
                    if (FieldByEntityType.TryGetValue(type, out var key) && fields.TryAdd(key, new ExtractedField(value, conf)))
                        confidences.Add(conf);
                    break;
            }
        }

        // Recover IVA from the vat breakdown when the top-level total_tax_amount is missing.
        if (!fields.ContainsKey("tax_amount") && vatTax > 0m)
            fields["tax_amount"] = new ExtractedField(vatTax.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.8m);

        // Supplier tax id: ignore it when it is actually the buyer's (own company or receiver).
        var cleanTaxId = NormalizeTaxId(supplierTaxId);
        if (cleanTaxId is not null && cleanTaxId != NormalizeTaxId(ownTaxId) && cleanTaxId != NormalizeTaxId(receiverTaxId))
        {
            fields["supplier_tax_id"] = new ExtractedField(cleanTaxId, supplierTaxConf);
            confidences.Add(supplierTaxConf);
        }

        // Base imponible = total − IVA. This folds in shipping (portes) and other
        // pre-tax charges that the raw net_amount entity may omit (e.g. Francisco Jover),
        // and keeps base + IVA = total coherent by construction. (Assumes no recargo de
        // equivalencia, which is 0 on these invoices.)
        if (fields.TryGetValue("total_amount", out var totalF) &&
            fields.TryGetValue("tax_amount", out var taxF) &&
            decimal.TryParse(totalF.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var total) &&
            decimal.TryParse(taxF.Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tax))
        {
            fields["net_amount"] = new ExtractedField(
                (total - tax).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Math.Min(totalF.Confidence, taxF.Confidence));
        }

        // Last resort for the base: the taxable base summed from the vat breakdown.
        if (!fields.ContainsKey("net_amount") && vatBase > 0m)
            fields["net_amount"] = new ExtractedField(vatBase.ToString(System.Globalization.CultureInfo.InvariantCulture), 0.7m);

        // Supplier name: trust the entity only if it looks like a company and isn't the buyer;
        // otherwise fall back to the first company-form line in the OCR text (fixes e.g. Star).
        var name = ResolveSupplierName(supplierName, receiverName ?? ownName,
            root.TryGetProperty("text", out var txt) ? txt.GetString() : null);
        if (name is not null)
        {
            fields["supplier_name"] = new ExtractedField(name, supplierNameConf > 0 ? supplierNameConf : 0.8m);
            confidences.Add(0.8m);
        }

        var overall = confidences.Count > 0 ? confidences.Average() : 0m;
        return new ExtractionResult(fields, [], overall);
    }

    // Document AI sometimes mis-normalizes Spanish long-form dates ("4 de marzo de 2026" →
    // "--03-02", no year). Trust normalizedValue only if it is a full ISO date; else parse the
    // mention text (numeric dd/mm/yyyy or Spanish "d de mes de yyyy").
    private static string? NormalizeDate(JsonElement entity)
    {
        if (entity.TryGetProperty("normalizedValue", out var nv) &&
            nv.TryGetProperty("text", out var nt) &&
            nt.GetString() is { } iso && IsoDate().IsMatch(iso))
            return iso;

        var mention = entity.TryGetProperty("mentionText", out var m) ? m.GetString() : null;
        return ParseDate(mention);
    }

    private static string? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var sp = SpanishDate().Match(text);
        if (sp.Success && SpanishMonths.TryGetValue(sp.Groups[2].Value, out var month))
            return $"{sp.Groups[3].Value}-{month:D2}-{int.Parse(sp.Groups[1].Value):D2}";

        var num = NumericDate().Match(text);
        if (num.Success)
        {
            var year = int.Parse(num.Groups[3].Value);
            if (year < 100) year += 2000;
            return $"{year:D4}-{int.Parse(num.Groups[2].Value):D2}-{int.Parse(num.Groups[1].Value):D2}";
        }
        return null;
    }

    // Sums a numeric sub-property (e.g. "vat/tax_amount") across an entity's properties.
    private static decimal SumProperty(JsonElement entity, string propertyType)
    {
        if (!entity.TryGetProperty("properties", out var props)) return 0m;

        var sum = 0m;
        foreach (var p in props.EnumerateArray())
        {
            if (!p.TryGetProperty("type", out var pt) || pt.GetString() != propertyType) continue;
            if (p.TryGetProperty("normalizedValue", out var nv) && nv.TryGetProperty("floatValue", out var fv))
                sum += (decimal)fv.GetDouble();
        }
        return sum;
    }

    private static string? ResolveSupplierName(string? entityName, string? ownName, string? ocrText)
    {
        var clean = CleanLeadingJunk(entityName);
        var isCompany = clean is not null && SupplierNameHeuristics.CompanyNameLine().IsMatch(clean);
        var isOwn = clean is not null && SupplierNameHeuristics.SameCompany(clean, ownName);

        if (isCompany && !isOwn) return clean;

        return ocrText is not null ? SupplierNameHeuristics.FindCompanyLine(ocrText, ownName) : clean;
    }

    // OCR sometimes prefixes the name with a stray glyph (e.g. "■Francisco Jover, S.A.").
    private static string? CleanLeadingJunk(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var trimmed = new string(name.SkipWhile(ch => !char.IsLetterOrDigit(ch)).ToArray()).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // Extracts the Spanish tax id (letter+8 digits or 8 digits+letter) from noisy OCR,
    // discarding junk prefixes/suffixes like the "F" in "FA-62728209" or an "ES" VAT prefix.
    private static string? NormalizeTaxId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var m = TaxIdCore().Match(s);
        if (m.Success) return m.Value;
        if (s.StartsWith("ES") && s.Length > 2) s = s[2..];
        return s.Length == 0 ? null : s;
    }

    [GeneratedRegex(@"[A-Z]\d{8}|\d{8}[A-Z]")] private static partial Regex TaxIdCore();

    // Prefer the normalized value (e.g. ISO date) when Document AI provides one.
    private static string? NormalizedOrMention(JsonElement entity)
    {
        if (entity.TryGetProperty("normalizedValue", out var nv) &&
            nv.TryGetProperty("text", out var nt) &&
            nt.GetString() is { Length: > 0 } normalized)
            return normalized;

        return entity.TryGetProperty("mentionText", out var m) ? m.GetString()?.Trim() : null;
    }
}
