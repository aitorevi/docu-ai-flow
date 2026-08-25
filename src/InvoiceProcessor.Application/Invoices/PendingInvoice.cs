using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Invoices;

// A single extracted field value plus the confidence the extractor assigned to it, kept so the
// review UI can highlight the fields a human should look at first.
public sealed record CapturedField(string? Value, decimal Confidence);

// An invoice extracted from a supplier that has not earned trust yet, waiting for human
// validation. It holds the captured field values — the diff baseline used to decide whether a confirmation counts as
// "unmodified" — plus the per-field confidence snapshot and the PDF's location in data/pending/.
public sealed record PendingInvoice(
    string ContentHash,
    string PendingPath,
    string OriginalFileName,
    string InvoiceNumber,
    string SupplierName,
    string? SupplierTaxId,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency,
    IReadOnlyDictionary<string, CapturedField> Confidence,
    DateTimeOffset DetectedAt,
    bool RequiresManualEntry = false,
    bool SourcedFromOcr = false)
{
    // Creates an empty pending invoice for documents that cannot be auto-extracted (scans,
    // unknown templates, scarce text). All invoice fields are blank/zero; a human fills them
    // in the /review UI.
    public static PendingInvoice CreateEmpty(IncomingDocument document, string pendingPath)
    {
        var emptyConfidence = new Dictionary<string, CapturedField>
        {
            ["invoiceNumber"] = new(null, 0m),
            ["issueDate"]     = new(null, 0m),
            ["dueDate"]       = new(null, 0m),
            ["supplierName"]  = new(null, 0m),
            ["supplierTaxId"] = new(null, 0m),
            ["netAmount"]     = new(null, 0m),
            ["taxAmount"]     = new(null, 0m),
            ["totalAmount"]   = new(null, 0m),
            ["currency"]      = new(null, 0m),
        };
        return new PendingInvoice(
            ContentHash: document.ContentHash,
            PendingPath: pendingPath,
            OriginalFileName: document.FileName,
            InvoiceNumber: string.Empty,
            SupplierName: string.Empty,
            SupplierTaxId: null,
            IssueDate: DateOnly.MinValue,
            DueDate: null,
            NetAmount: 0m,
            TaxAmount: 0m,
            TotalAmount: 0m,
            Currency: "EUR",
            Confidence: emptyConfidence,
            DetectedAt: document.DetectedAt,
            RequiresManualEntry: true);
    }

    public static PendingInvoice From(
        Invoice invoice, ExtractionResult extraction, IncomingDocument document, string pendingPath) => new(
        ContentHash: document.ContentHash,
        PendingPath: pendingPath,
        OriginalFileName: document.FileName,
        InvoiceNumber: invoice.InvoiceNumber,
        SupplierName: invoice.Supplier.Name,
        SupplierTaxId: invoice.Supplier.TaxId,
        IssueDate: invoice.IssueDate,
        DueDate: invoice.DueDate,
        NetAmount: invoice.NetAmount.Amount,
        TaxAmount: invoice.TaxAmount.Amount,
        TotalAmount: invoice.TotalAmount.Amount,
        Currency: invoice.TotalAmount.Currency,
        Confidence: BuildConfidence(extraction),
        DetectedAt: document.DetectedAt,
        SourcedFromOcr: extraction.SourcedFromOcr);

    // Maps extraction field keys onto the UI field keys so the front can show a confidence per
    // editable field. Missing extraction fields default to zero confidence.
    private static Dictionary<string, CapturedField> BuildConfidence(ExtractionResult extraction)
    {
        (string Ui, string Extraction)[] map =
        [
            ("invoiceNumber", "invoice_number"),
            ("issueDate", "issue_date"),
            ("dueDate", "due_date"),
            ("supplierName", "supplier_name"),
            ("supplierTaxId", "supplier_tax_id"),
            ("netAmount", "net_amount"),
            ("taxAmount", "tax_amount"),
            ("totalAmount", "total_amount"),
            ("currency", "currency"),
        ];

        var result = new Dictionary<string, CapturedField>();
        foreach (var (ui, key) in map)
            result[ui] = extraction.Fields.TryGetValue(key, out var field)
                ? new CapturedField(field.Value, field.Confidence)
                : new CapturedField(null, 0m);
        return result;
    }
}

// The fields a human submits when confirming a pending invoice. Compared against the captured
// PendingInvoice to decide whether the confirmation was "modified" (any diff resets trust).
public sealed record CorrectedInvoiceFields(
    string InvoiceNumber,
    string SupplierName,
    string? SupplierTaxId,
    DateOnly IssueDate,
    DateOnly? DueDate,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency)
{
    // True when any field differs from the captured pending values (filling an empty field
    // counts as a modification). Strings are compared trimmed and case-insensitively.
    public bool DiffersFrom(PendingInvoice pending) =>
        !SameText(InvoiceNumber, pending.InvoiceNumber) ||
        !SameText(SupplierName, pending.SupplierName) ||
        !SameText(SupplierTaxId, pending.SupplierTaxId) ||
        IssueDate != pending.IssueDate ||
        DueDate != pending.DueDate ||
        NetAmount != pending.NetAmount ||
        TaxAmount != pending.TaxAmount ||
        TotalAmount != pending.TotalAmount ||
        !SameText(Currency, pending.Currency);

    private static bool SameText(string? a, string? b) =>
        string.Equals((a ?? string.Empty).Trim(), (b ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);
}
