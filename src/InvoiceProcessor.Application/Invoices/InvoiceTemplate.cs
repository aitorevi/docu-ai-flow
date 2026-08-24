namespace InvoiceProcessor.Application.Invoices;

// Template that identifies a supplier's invoices and extracts fields from them using anchors
// and regular expressions over the PDF's plain text. Lives in the Application layer so the
// port (IInvoiceTemplateRepository) is testable without Infrastructure dependencies.
public sealed record InvoiceTemplate(
    string SupplierId,
    string SupplierName,
    string SupplierTaxId,
    IReadOnlyList<string> IdentificationAnchors,
    IReadOnlyDictionary<string, FieldTemplate> Fields);

// A single extractable field: one or more textual anchors (ordered longest→shortest by the
// repository at load time) plus a regex whose first capture group is the value.
public sealed record FieldTemplate(
    IReadOnlyList<string> Anchors,
    string Pattern);
