using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Options;
using SharpMonads.Core;

namespace InvoiceProcessor.Application.Invoices;

// The human half of the loop. Everything the extractor was not confident enough to file on its
// own ends up here, and what the human does with it feeds back into whether that supplier gets
// to skip review next time.
public sealed class ReviewInvoiceService(
    IPendingInvoiceRepository pendingRepository,
    ISupplierTrustRepository trustRepository,
    IProcessedInvoiceRepository repository,
    IDocumentArchiver archiver,
    IProcessedDocumentLog processedLog,
    IMasterSpreadsheetWriter masterWriter,
    IOptions<ExtractionOptions> extractionOptions) : IReviewInvoiceUseCase
{
    private readonly int _threshold = extractionOptions.Value.SupplierTrustThreshold;

    public async Task<IReadOnlyList<PendingInvoice>> GetPendingAsync(CancellationToken ct)
    {
        var list = new List<PendingInvoice>();
        await foreach (var pending in pendingRepository.ListAllAsync(ct))
            list.Add(pending);
        return list;
    }

    public Task<PendingInvoice?> GetPendingByHashAsync(string contentHash, CancellationToken ct) =>
        pendingRepository.FindByContentHashAsync(contentHash, ct);

    public async Task<Result<ConfirmResult, Error>> ConfirmAsync(
        string contentHash, CorrectedInvoiceFields fields, CancellationToken ct)
    {
        var pending = await pendingRepository.FindByContentHashAsync(contentHash, ct);
        if (pending is null)
            return Result<ConfirmResult, Error>.Failure(Error.Validation("Factura pendiente no encontrada."));

        var built = BuildInvoice(fields);
        return await built.Match(
            onSuccess: async invoice =>
            {
                // Did the human touch anything? That single bit is what the trust score is made of.
                var wasModified = fields.DiffersFrom(pending);

                // Archive and persist first; only then advance the supplier's trust. In this order
                // a failed file move or save can never leave the counter incremented for an
                // invoice that was not actually filed, which would double-count on retry.
                var archivedPath = await archiver.ArchiveConfirmedAsync(pending.PendingPath, invoice, ct);
                var stored = StoredInvoice.From(invoice, contentHash, archivedPath);
                await repository.SaveAsync(stored, ct);
                await processedLog.MarkProcessedAsync(contentHash, invoice.Id, ct);

                var trust = await RegisterConfirmationAsync(invoice.Supplier.TaxId, wasModified, ct);

                await pendingRepository.DeleteAsync(contentHash, ct);
                await masterWriter.RebuildAsync(ct);

                return Result<ConfirmResult, Error>.Success(new ConfirmResult(stored, wasModified, trust));
            },
            onFailure: error => Task.FromResult(Result<ConfirmResult, Error>.Failure(error)));
    }

    public async Task<Result<RejectResult, Error>> RejectAsync(string contentHash, CancellationToken ct)
    {
        var pending = await pendingRepository.FindByContentHashAsync(contentHash, ct);
        if (pending is null)
            return Result<RejectResult, Error>.Failure(Error.Validation("Factura pendiente no encontrada."));

        var moved = await archiver.RejectPendingAsync(pending.PendingPath, ct);
        await pendingRepository.DeleteAsync(contentHash, ct);
        // Forget the hash — it was marked processed on entering pending/ — so the same PDF can be
        // submitted again and reviewed if the rejection was a mistake.
        await processedLog.RemoveAsync(contentHash, ct);
        return Result<RejectResult, Error>.Success(new RejectResult(contentHash, PdfWasMissing: !moved));
    }

    public async Task<Result<RequeueResult, Error>> RequeueAsync(string contentHash, CancellationToken ct)
    {
        var pending = await pendingRepository.FindByContentHashAsync(contentHash, ct);
        if (pending is null)
            return Result<RequeueResult, Error>.Failure(Error.Validation("Factura pendiente no encontrada."));

        var moved = await archiver.RequeuePendingAsync(pending.PendingPath, pending.OriginalFileName, ct);
        await pendingRepository.DeleteAsync(contentHash, ct);
        // Forget the hash, or the requeued PDF is skipped as a duplicate the moment it lands back
        // in the inbox and the whole point of reprocessing it is lost.
        await processedLog.RemoveAsync(contentHash, ct);
        return Result<RequeueResult, Error>.Success(new RequeueResult(contentHash, PdfWasMissing: !moved));
    }

    private async Task<SupplierTrust> RegisterConfirmationAsync(string? taxId, bool wasModified, CancellationToken ct)
    {
        // Trust is keyed by tax id; without one there is nothing stable to attach a score to.
        if (string.IsNullOrWhiteSpace(taxId))
            return SupplierTrust.New(string.Empty);

        var trust = await trustRepository.GetAsync(taxId, ct) ?? SupplierTrust.New(taxId);
        trust = wasModified
            ? trust.RegisterModifiedConfirmation()
            : trust.RegisterUnmodifiedConfirmation(_threshold);
        await trustRepository.SaveAsync(trust, ct);
        return trust;
    }

    private static Result<Invoice, Error> BuildInvoice(CorrectedInvoiceFields f)
    {
        var supplier = new Supplier(f.SupplierName, f.SupplierTaxId, null);
        return Money.Create(f.NetAmount, f.Currency).Bind(net =>
               Money.Create(f.TaxAmount, f.Currency).Bind(tax =>
               Money.Create(f.TotalAmount, f.Currency).Bind(total =>
               Invoice.Create(InvoiceId.New(), f.InvoiceNumber, supplier, f.IssueDate, f.DueDate, net, tax, total, []))));
    }
}
