namespace InvoiceProcessor.Application.Invoices;

// What the panel leads with. Deliberately small: four headline numbers and one row per
// supplier, which is all the dashboard needs to answer "what is the system doing, and which
// suppliers still cost me time".
public sealed record DashboardSummary(
    int PendingReview,
    int ArchivedInvoices,
    decimal TotalAmount,
    string Currency,
    int TrustThreshold,
    IReadOnlyList<SupplierSummary> Suppliers);

// A supplier's standing: how much of it is already filed, how much still waits on a human, and
// how close its template is to being trusted enough to skip review.
public sealed record SupplierSummary(
    string Name,
    string? TaxId,
    int ArchivedCount,
    int PendingCount,
    int UnmodifiedCount,
    bool IsTrusted);
