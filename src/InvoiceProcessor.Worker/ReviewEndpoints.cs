using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Worker;

// HTTP surface for the review queue: list what is waiting, open one invoice next to its PDF,
// and then confirm, reject or send it back for reprocessing.
public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this WebApplication app)
    {
        // The queue. Deliberately lightweight — the review screen loads the full invoice only
        // when one is opened.
        app.MapGet("/api/pending", async (
            IReviewInvoiceUseCase review, IPendingInvoiceRepository pendingRepo, CancellationToken ct) =>
        {
            var pending = await review.GetPendingAsync(ct);
            var result = new List<object>();
            foreach (var p in pending)
                result.Add(new
                {
                    contentHash             = p.ContentHash,
                    supplierName            = p.SupplierName,
                    supplierTaxId           = p.SupplierTaxId,
                    invoiceNumber           = p.InvoiceNumber,
                    issueDate               = p.IssueDate,
                    totalAmount             = p.TotalAmount,
                    currency                = p.Currency,
                    requiresManualEntry     = p.RequiresManualEntry,
                    sourcedFromOcr          = p.SourcedFromOcr,
                    pendingCountForSupplier = await pendingRepo.CountByTaxIdAsync(p.SupplierTaxId, ct),
                });
            return Results.Ok(result);
        });

        // One invoice, with its per-field confidence and the supplier's progress towards trust —
        // the reviewer should be able to see both what to check and why they are still checking.
        app.MapGet("/api/pending/{hash}", async (string hash,
            IReviewInvoiceUseCase review, ISupplierTrustRepository trustRepo,
            IOptions<ExtractionOptions> extraction, CancellationToken ct) =>
        {
            var p = await review.GetPendingByHashAsync(hash, ct);
            if (p is null) return Results.NotFound(new { error = "Factura pendiente no encontrada." });

            var trust = p.SupplierTaxId is null ? null : await trustRepo.GetAsync(p.SupplierTaxId, ct);
            return Results.Ok(new
            {
                contentHash         = p.ContentHash,
                originalFileName    = p.OriginalFileName,
                invoiceNumber       = p.InvoiceNumber,
                supplierName        = p.SupplierName,
                supplierTaxId       = p.SupplierTaxId,
                issueDate           = p.IssueDate,
                dueDate             = p.DueDate,
                netAmount           = p.NetAmount,
                taxAmount           = p.TaxAmount,
                totalAmount         = p.TotalAmount,
                currency            = p.Currency,
                requiresManualEntry = p.RequiresManualEntry,
                sourcedFromOcr      = p.SourcedFromOcr,
                confidence          = p.Confidence,
                trust = new
                {
                    unmodifiedCount = trust?.ConsecutiveUnmodifiedCount ?? 0,
                    threshold       = extraction.Value.SupplierTrustThreshold,
                },
            });
        });

        app.MapGet("/api/pending/{hash}/pdf", async (string hash,
            IReviewInvoiceUseCase review, CancellationToken ct) =>
        {
            var p = await review.GetPendingByHashAsync(hash, ct);
            if (p is null || !File.Exists(p.PendingPath))
                return Results.NotFound(new { error = "PDF pendiente no encontrado." });

            var stream = File.OpenRead(p.PendingPath);
            // No fileDownloadName ⇒ no "Content-Disposition: attachment" ⇒ the browser renders it
            // inline in the review split view instead of downloading it. enableRangeProcessing ⇒
            // Accept-Ranges + 206, which is how PDF viewers load a document progressively.
            return Results.File(stream, "application/pdf", enableRangeProcessing: true);
        });

        // Confirm: the reviewer says these are the right numbers. Whether they changed anything
        // is what decides the supplier's trust.
        app.MapPut("/api/pending/{hash}", async (string hash,
            [FromBody] CorrectInvoiceRequest req, IReviewInvoiceUseCase review, CancellationToken ct) =>
        {
            if (!DateOnly.TryParse(req.IssueDate, out var issueDate))
                return Results.BadRequest(new { error = "Fecha de emisión inválida." });

            DateOnly? dueDate = null;
            if (!string.IsNullOrWhiteSpace(req.DueDate))
            {
                if (!DateOnly.TryParse(req.DueDate, out var parsedDue))
                    return Results.BadRequest(new { error = "Fecha de vencimiento inválida." });
                dueDate = parsedDue;
            }

            var fields = new CorrectedInvoiceFields(
                req.InvoiceNumber, req.SupplierName, req.SupplierTaxId, issueDate, dueDate,
                req.NetAmount, req.TaxAmount, req.TotalAmount, req.Currency);

            var result = await review.ConfirmAsync(hash, fields, ct);
            return result.Match(
                onSuccess: r => Results.Ok(new
                {
                    contentHash     = r.Invoice.ContentHash,
                    wasModified     = r.WasModified,
                    unmodifiedCount = r.Trust.ConsecutiveUnmodifiedCount,
                    isTrusted       = r.Trust.IsTrusted,
                }),
                onFailure: err => Results.BadRequest(new { error = err.Message }));
        });

        // Reject: this is not an invoice we want. Discards it to failed/.
        app.MapDelete("/api/pending/{hash}", async (string hash,
            IReviewInvoiceUseCase review, CancellationToken ct) =>
        {
            var result = await review.RejectAsync(hash, ct);
            return result.Match(
                onSuccess: r => Results.Ok(new { contentHash = r.ContentHash, pdfWasMissing = r.PdfWasMissing }),
                onFailure: err => Results.NotFound(new { error = err.Message }));
        });

        // Requeue: the invoice is fine, the template was not. Sends the PDF back to the inbox so
        // it can be extracted again once the template is fixed, instead of discarding it.
        app.MapPost("/api/pending/{hash}/requeue", async (string hash,
            IReviewInvoiceUseCase review, CancellationToken ct) =>
        {
            var result = await review.RequeueAsync(hash, ct);
            return result.Match(
                onSuccess: r => Results.Ok(new { contentHash = r.ContentHash, pdfWasMissing = r.PdfWasMissing }),
                onFailure: err => Results.NotFound(new { error = err.Message }));
        });
    }
}

// Dates arrive as strings so an unparseable value is a 400 with a message, not a model-binding
// failure the reviewer cannot act on.
public sealed record CorrectInvoiceRequest(
    string InvoiceNumber,
    string SupplierName,
    string? SupplierTaxId,
    string IssueDate,
    string? DueDate,
    decimal NetAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    string Currency);
