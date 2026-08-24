using System.Text.RegularExpressions;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Extraction.Templates;

// Reads invoice templates from the "TemplateExtractor:Templates" configuration section.
// All anchor lists are ordered longest→shortest at construction time so the extractor
// does not need to sort on every extraction call.
public sealed class AppsettingsTemplateRepository : IInvoiceTemplateRepository
{
    private readonly IReadOnlyList<InvoiceTemplate> _templates;

    public AppsettingsTemplateRepository(IOptions<TemplateExtractorOptions> opts)
    {
        _templates = opts.Value.Templates
            .Select(e => new InvoiceTemplate(
                SupplierId: e.SupplierId,
                SupplierName: e.SupplierName,
                SupplierTaxId: e.SupplierTaxId,
                IdentificationAnchors: e.IdentificationAnchors
                    .OrderByDescending(a => a.Length)
                    .ToArray(),
                Fields: e.Fields.ToDictionary(
                    kv => kv.Key,
                    kv => new FieldTemplate(
                        Anchors: kv.Value.Anchors
                            .OrderByDescending(a => a.Length)
                            .ToArray(),
                        Pattern: AmountFieldKeys.Contains(kv.Key)
                            ? AllowNegativeAmounts(kv.Value.Pattern)
                            : kv.Value.Pattern))))
            .ToArray();
    }

    public IReadOnlyList<InvoiceTemplate> GetAll() => _templates;

    // Field keys whose value is a monetary amount and may therefore be negative on a credit note.
    private static readonly HashSet<string> AmountFieldKeys =
        ["net_amount", "tax_amount", "total_amount"];

    // Any regex character class: [ ... ].
    private static readonly Regex CharacterClass = new(@"\[[^\]]*\]", RegexOptions.Compiled);

    // Makes an amount pattern tolerant of a leading minus sign so credit notes (negative amounts)
    // are captured, without every template author having to remember it. Prefixes "-?" before
    // every NUMERIC character class (e.g. [\d.,]+, [\d.]+, [\d,]+) — including columns the pattern
    // only skips over, since a credit note prints every column negative. Leaves the "[\s\S]"
    // any-char idiom and non-numeric classes untouched, and \S+ already accepts the sign.
    // Idempotent.
    internal static string AllowNegativeAmounts(string pattern) =>
        CharacterClass.Replace(pattern, m =>
        {
            var body = m.Value[1..^1]; // strip the surrounding [ ]

            // Numeric class: only \d, \s, '.' or ',' atoms, and at least one \d. This excludes
            // [\s\S] (contains \S) and classes like [^)] or [.\s] (no \d).
            var isNumeric = body.Contains(@"\d") && Regex.IsMatch(body, @"^(?:\\d|\\s|[.,])+$");
            if (!isNumeric) return m.Value;

            // Idempotent: skip if this class is already preceded by "-?".
            var alreadySigned = m.Index >= 2 && pattern.AsSpan(m.Index - 2, 2) is "-?";
            return alreadySigned ? m.Value : "-?" + m.Value;
        });
}
