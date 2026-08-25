using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Invoices;

// The extraction backends bound to IInvoiceDataExtractor. Which one is active is a
// composition-root decision driven by configuration, never by the use cases.
public enum ExtractionProvider { Template, DocumentAi }

public sealed class ExtractionOptions
{
    public decimal ConfidenceThreshold { get; init; } = 0.6m;

    // Consecutive confirmations with no human correction a supplier needs before its invoices
    // skip review and are filed automatically. See SupplierTrust.
    public int SupplierTrustThreshold { get; set; } = SupplierTrust.DefaultThreshold;

    // Which extraction backend is active. Default = Template (local, via PdfPig, no external
    // credentials needed) so a fresh clone runs offline and for free. Switch to DocumentAi to
    // use Google's paid cloud API.
    public ExtractionProvider Provider { get; set; } = ExtractionProvider.Template;

    // Parses a raw config string into ExtractionProvider. Returns Template if the value is
    // null, empty, or unrecognized — preserving the safe, credential-free default. A typo in
    // configuration must never leave the app demanding cloud credentials it cannot have.
    public static ExtractionProvider ResolveProvider(string? value) =>
        Enum.TryParse<ExtractionProvider>(value, ignoreCase: true, out var p)
            ? p : ExtractionProvider.Template;
}
