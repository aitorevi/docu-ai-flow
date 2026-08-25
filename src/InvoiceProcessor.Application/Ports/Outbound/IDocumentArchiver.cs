using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IDocumentArchiver
{
    Task<string> ArchiveProcessedAsync(IncomingDocument document, Invoice invoice, CancellationToken ct);
    Task<string> ArchiveFailedAsync(IncomingDocument document, CancellationToken ct);

    // Already seen before → duplicates/. Moving it out of the inbox matters: otherwise the
    // watcher keeps re-polling the same file forever.
    Task<string> ArchiveDuplicateAsync(IncomingDocument document, CancellationToken ct);

    // Awaiting human review → pending/. The PDF is held there, not archived, until someone
    // confirms it. Prefixed with the supplier so the folder is readable at a glance.
    Task<string> ArchivePendingAsync(IncomingDocument document, string supplierName, CancellationToken ct);

    // Confirmed by a human → moves the held PDF from pending/ to its final place in archive/.
    Task<string> ArchiveConfirmedAsync(string pendingPath, Invoice invoice, CancellationToken ct);

    // Rejected by a human → pending/ to failed/. False when the PDF is no longer on disk.
    Task<bool> RejectPendingAsync(string pendingPath, CancellationToken ct);

    // Sent back for reprocessing → pending/ to inbox/, under its ORIGINAL name so the supplier
    // prefix is not stacked again on every round trip. False when the PDF is no longer on disk.
    Task<bool> RequeuePendingAsync(string pendingPath, string originalFileName, CancellationToken ct);
}
