using System.Globalization;
using System.Text.RegularExpressions;

namespace InvoiceProcessor.Infrastructure.Extraction.Templates;

// Outcome of a net + tax = total coherence cross-check.
public enum CoherenceKind { Coherent, Incoherent, Uncrossable }

// Holds the result of CheckAmountCoherence: the parsed amounts and the computed diff.
// When Kind == Uncrossable, Net/Tax/Total/Diff are zero (at least one value was missing or
// unparseable).
public record CoherenceResult(CoherenceKind Kind, decimal Net, decimal Tax, decimal Total, decimal Diff);

// Pure logic for parsing invoice field values found in PDF text.
// All methods are static so they can be unit-tested without any I/O dependency.
public static class TemplateFieldParser
{
    private static readonly string[] SpanishMonths =
    [
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    ];

    // Abbreviated Spanish month names (3-letter, no period). Index 0 = enero (1).
    private static readonly string[] SpanishMonthsAbbrev =
    [
        "ene", "feb", "mar", "abr", "may", "jun",
        "jul", "ago", "sep", "oct", "nov", "dic"
    ];

    // Abbreviated English month names, for foreign suppliers invoicing in English.
    private static readonly string[] EnglishMonthsAbbrev =
    [
        "jan", "feb", "mar", "apr", "may", "jun",
        "jul", "aug", "sep", "oct", "nov", "dec"
    ];

    // Parses a decimal number in either Spanish or English/US format.
    // Spanish: thousands-separator "." and decimal-separator "," — e.g. "1.210,00" → 1210.00m.
    // English: thousands-separator "," and decimal-separator "." — e.g. "1,132.11" → 1132.11m.
    // Format is auto-detected from the last separator character in the string.
    // Returns null for null, empty or unrecognizable input.
    public static decimal? ParseSpanishNumber(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // Some PDFs render amounts with the digits spaced apart character-by-character
        // ("3 6 , 8 9 0"). Strip all internal whitespace so the number parses; spaces never
        // carry meaning inside a numeric amount.
        // Also normalise the soft hyphen (U+00AD) and the unicode minus (U+2212), which some
        // issuers use as the minus sign on credit notes, to a plain '-'.
        var trimmed = Regex.Replace(input, @"\s", string.Empty)
            .Replace('­', '-')
            .Replace('−', '-');

        // Detect format by the last separator character
        var lastComma = trimmed.LastIndexOf(',');
        var lastDot   = trimmed.LastIndexOf('.');

        string normalized;
        if (lastComma < 0 && trimmed.Count(c => c == '.') >= 2)
        {
            // Dot used as BOTH thousands and decimal separator, no comma at all
            // (e.g. "1.170.00" meaning 1170.00). Treat the last dot as the decimal point when
            // it is followed by two digits (cents), otherwise treat every dot as a thousands
            // separator.
            var tail = trimmed[(lastDot + 1)..];
            normalized = tail.Length == 2
                ? trimmed[..lastDot].Replace(".", string.Empty) + "." + tail
                : trimmed.Replace(".", string.Empty);
        }
        else if (lastComma > lastDot)
        {
            // Spanish format: "1.210,00" — comma is the decimal separator
            normalized = trimmed.Replace(".", string.Empty).Replace(",", ".");
        }
        else if (lastDot > lastComma)
        {
            // English/US format: "1,132.11" — dot is the decimal separator
            normalized = trimmed.Replace(",", string.Empty);
        }
        else
        {
            // No separator at all (e.g. "1000") — treat as integer
            normalized = trimmed;
        }

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    // Parses a date string and returns it as ISO 8601 (yyyy-MM-dd), or null if unrecognizable.
    // Suppliers do not agree on a format, so every accepted shape below comes from a real invoice.
    public static string? ParseDate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        // Collapse spaces around the slash — some issuers print dates as "30 / 9 / 2025".
        // Only the slash is normalised so textual month formats ("3 de marzo de 2025",
        // "17 dic. 2025") are left untouched.
        var trimmed = Regex.Replace(input.Trim(), @"\s*/\s*", "/");

        // Numeric formats, most specific first. Two-digit-year variants ("yy") are only accepted
        // when they resolve to 2000+, so a stray "31/12/99" is rejected instead of being filed
        // under 1999; four-digit-year variants are taken at face value.
        (string Format, bool RequiresYear2000)[] numericFormats =
        [
            ("yyyy-MM-dd", false),   // already ISO
            ("dd/MM/yyyy", false), ("dd/MM/yy", true),
            ("d/M/yyyy",   false), ("d/M/yy",   true),
            ("dd.MM.yyyy", false), ("dd.MM.yy", true),
            ("dd-MM-yyyy", false), ("dd-MM-yy", true),
        ];

        foreach (var (format, requiresYear2000) in numericFormats)
        {
            if (DateOnly.TryParseExact(trimmed, format, null, DateTimeStyles.None, out var parsed)
                && (!requiresYear2000 || parsed.Year >= 2000))
                return parsed.ToString("yyyy-MM-dd");
        }

        // "N de MONTH de YYYY"
        var spanishMatch = Regex.Match(trimmed,
            @"^(\d{1,2})\s+de\s+(\w+)\s+de\s+(\d{4})$",
            RegexOptions.IgnoreCase);
        if (spanishMatch.Success)
        {
            var day = int.Parse(spanishMatch.Groups[1].Value);
            var monthName = spanishMatch.Groups[2].Value.ToLowerInvariant();
            var year = int.Parse(spanishMatch.Groups[3].Value);

            var monthIndex = Array.IndexOf(SpanishMonths, monthName);
            if (monthIndex >= 0)
                return new DateOnly(year, monthIndex + 1, day).ToString("yyyy-MM-dd");
        }

        // "Mon DD, YYYY" (English abbreviated month) — foreign suppliers invoicing in English.
        var englishMatch = Regex.Match(trimmed,
            @"^(\w{3})\s+(\d{1,2}),\s+(\d{4})$",
            RegexOptions.IgnoreCase);
        if (englishMatch.Success)
        {
            var abbrev = englishMatch.Groups[1].Value.ToLowerInvariant();
            var day    = int.Parse(englishMatch.Groups[2].Value);
            var year   = int.Parse(englishMatch.Groups[3].Value);

            var monthIndex = Array.IndexOf(EnglishMonthsAbbrev, abbrev);
            if (monthIndex >= 0)
                return new DateOnly(year, monthIndex + 1, day).ToString("yyyy-MM-dd");
        }

        // "DD mon YYYY" / "DD mon. YYYY" (abbreviated Spanish month, optional trailing period).
        // \w{3,4} covers standard 3-letter abbreviations and the 4-letter "sept" some issuers use.
        var abbrevMatch = Regex.Match(trimmed,
            @"^(\d{1,2})\s+(\w{3,4})\.?\s+(\d{4})$",
            RegexOptions.IgnoreCase);
        if (abbrevMatch.Success)
        {
            var day   = int.Parse(abbrevMatch.Groups[1].Value);
            var abbrev = abbrevMatch.Groups[2].Value.ToLowerInvariant();
            var year  = int.Parse(abbrevMatch.Groups[3].Value);

            // Normalize "sept" → "sep" so it maps to September.
            if (abbrev == "sept") abbrev = "sep";

            var monthIndex = Array.IndexOf(SpanishMonthsAbbrev, abbrev);
            if (monthIndex >= 0)
                return new DateOnly(year, monthIndex + 1, day).ToString("yyyy-MM-dd");
        }

        return null;
    }

    // Searches for an anchor (tried longest→shortest) in the text. When found, runs the regex
    // starting from the end of the anchor and returns capture group 1. Returns null if nothing
    // matches. matchLastAnchor:true anchors on the LAST occurrence instead of the first — used for
    // monetary amounts, whose label repeats as a per-page header on multi-page invoices while the
    // totals sit on the last page (and is a no-op when the anchor appears only once).
    public static string? FindAnchorAndCapture(
        string text, IEnumerable<string> anchors, string pattern, bool matchLastAnchor = false)
    {
        // Sort longest → shortest here so callers don't have to worry about order.
        var sorted = anchors
            .OrderByDescending(a => a.Length)
            .ThenBy(a => a, StringComparer.OrdinalIgnoreCase);

        foreach (var anchor in sorted)
        {
            var anchorPos = matchLastAnchor
                ? text.LastIndexOf(anchor, StringComparison.OrdinalIgnoreCase)
                : text.IndexOf(anchor, StringComparison.OrdinalIgnoreCase);
            if (anchorPos < 0) continue;

            var searchFrom = anchorPos + anchor.Length;
            var remainder = text[searchFrom..];

            var match = Regex.Match(remainder, pattern);
            if (match.Success && match.Groups.Count > 1)
                return match.Groups[1].Value.Trim();
        }

        return null;
    }

    // Checks whether net_amount + tax_amount ≈ total_amount (within 0.05 tolerance).
    // Accepts raw strings as captured by the template extractor.
    // Returns Uncrossable when any amount is null or cannot be parsed as a number.
    public static CoherenceResult CheckAmountCoherence(string? net, string? tax, string? total)
    {
        var parsedNet   = ParseSpanishNumber(net);
        var parsedTax   = ParseSpanishNumber(tax);
        var parsedTotal = ParseSpanishNumber(total);

        if (parsedNet is null || parsedTax is null || parsedTotal is null)
            return new CoherenceResult(CoherenceKind.Uncrossable, 0m, 0m, 0m, 0m);

        var diff = Math.Abs(parsedNet.Value + parsedTax.Value - parsedTotal.Value);
        var kind = diff <= 0.05m ? CoherenceKind.Coherent : CoherenceKind.Incoherent;
        return new CoherenceResult(kind, parsedNet.Value, parsedTax.Value, parsedTotal.Value, diff);
    }
}
