using System.Text.RegularExpressions;

namespace InvoiceProcessor.Infrastructure.Extraction;

// Shared heuristic for spotting a Spanish supplier name from raw OCR text, used by the
// Document AI mapper.
internal static partial class SupplierNameHeuristics
{
    // A line ending in a Spanish company form ("…, S.A." / "S.L." / "S.L.U." / "C.B." / "S.COOP").
    // The separator before the suffix avoids false hits inside words (e.g. "Vinalesa").
    [GeneratedRegex(@"(?:^|[,\s])(S\.?A\.?[UL]?|S\.?L\.?[LU]?|C\.?B|S\.?C(?:OOP)?)\.?\s*$",
        RegexOptions.IgnoreCase)]
    public static partial Regex CompanyNameLine();

    // First company-form line in reading order, skipping the buyer's own line if given.
    public static string? FindCompanyLine(string text, string? exclude = null)
    {
        var excludeKey = Squash(exclude);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || Squash(line) == excludeKey) continue;
            if (CompanyNameLine().IsMatch(line)) return line;
        }
        return null;
    }

    // Same company ignoring punctuation/spaces/case ("Tiendas Zulima C.B." == "TIENDAS ZULIMA, C.B.").
    public static bool SameCompany(string? a, string? b)
    {
        var ka = Squash(a);
        return ka is not null && ka == Squash(b);
    }

    // Punctuation/space/case-insensitive key so "TIENDAS ZULIMA C.B." matches "TIENDAS ZULIMA, C.B.".
    private static string? Squash(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
}
