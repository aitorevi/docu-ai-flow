using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Invoices;

// Outcome of confirming a pending invoice: the archived invoice, whether the human modified any
// field, and the supplier's updated trust after the confirmation was registered.
public sealed record ConfirmResult(StoredInvoice Invoice, bool WasModified, SupplierTrust Trust);

// Outcome of rejecting a pending invoice. PdfWasMissing is true when the pending PDF no longer
// existed on disk (the pending row is still removed).
public sealed record RejectResult(string ContentHash, bool PdfWasMissing);

// Outcome of requeuing a pending invoice (moved back to inbox/ for reprocessing). PdfWasMissing is
// true when the pending PDF no longer existed on disk (the pending row and hash are still cleared).
public sealed record RequeueResult(string ContentHash, bool PdfWasMissing);
