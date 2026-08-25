using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Application.Invoices;

// Decides what happens to each PDF that lands in the inbox. The interesting decision is not
// "did extraction work" but "is this good enough to file without a human looking at it" — and
// the answer depends on how well the supplier's template has performed so far.
public sealed class ProcessInvoiceService(
    IDocumentReader reader,
    IInvoiceDataExtractor extractor,
    ISupplierNormalizer supplierNormalizer,
    IProcessedInvoiceRepository repository,
    IDocumentArchiver archiver,
    IProcessedDocumentLog log,
    ISupplierTrustRepository trustRepository,
    IPendingInvoiceRepository pendingRepository,
    ILogger<ProcessInvoiceService> logger,
    IOptions<ExtractionOptions> extractionOptions) : IProcessInvoiceUseCase
{
    public async Task<ProcessInvoiceResult> ExecuteAsync(IncomingDocument document, CancellationToken ct)
    {
        if (await log.WasProcessedAsync(document.ContentHash, ct))
        {
            // Move it out of the inbox, or the watcher keeps re-polling the same file forever.
            var duplicatePath = await archiver.ArchiveDuplicateAsync(document, ct);
            logger.LogInformation("Documento {File} ya procesado (hash duplicado) → {Path}.",
                document.FileName, duplicatePath);
            return new ProcessInvoiceResult(true, null, "Duplicado");
        }

        // Close the source stream before archiving: on Windows a file with an open handle
        // cannot be moved, which would fail the File.Move later on.
        ExtractionResult extraction;
        await using (var content = await reader.OpenAsync(document, ct))
        {
            extraction = await extractor.ExtractAsync(content, ct);
        }

        // Nothing could be understood — a scan with no text layer, no template for this supplier,
        // or a mandatory field the pattern could not parse. Mapping an empty result would only
        // produce a misleading "missing field" error, so hold the PDF for a human instead.
        if (extraction.RequiresManualEntry)
        {
            var pendingPath = await archiver.ArchivePendingAsync(document, string.Empty, ct);
            await pendingRepository.SaveAsync(PendingInvoice.CreateEmpty(document, pendingPath), ct);
            await log.MarkProcessedAsync(document.ContentHash, InvoiceId.New(), ct);
            logger.LogInformation("Documento {File} → pending/ (alta manual requerida).", document.FileName);
            return new ProcessInvoiceResult(true, null, "Alta manual requerida");
        }

        var mapped = ExtractionToInvoiceMapper.Map(extraction, supplierNormalizer,
            extractionOptions.Value.ConfidenceThreshold);

        return await mapped.Match(
            onSuccess: async invoice =>
            {
                // Natural-key duplicate guard: a re-issued or re-sent copy of an invoice already
                // stored — same number, same supplier tax id, different bytes, so the hash log
                // missed it — would otherwise be declared twice and inflate the quarter's VAT.
                // Only when the tax id is known: (number, null) is not a reliable identity.
                var supplierTaxId = invoice.Supplier.TaxId;
                if (!string.IsNullOrWhiteSpace(supplierTaxId) &&
                    await repository.ExistsByNaturalKeyAsync(invoice.InvoiceNumber, supplierTaxId, ct))
                {
                    var duplicatePath = await archiver.ArchiveDuplicateAsync(document, ct);
                    await log.MarkProcessedAsync(document.ContentHash, invoice.Id, ct);
                    logger.LogWarning("Posible duplicado {Number} de {Name} (mismo nº+CIF ya registrado) → {Path}.",
                        invoice.InvoiceNumber, invoice.Supplier.Name, duplicatePath);
                    return new ProcessInvoiceResult(true, null, "Posible duplicado");
                }

                // Trust gate: a supplier's invoices are filed automatically only once its template
                // has earned trust — N consecutive confirmations with no human correction. Until
                // then every invoice waits in pending/ for someone to look at it.
                var trust = string.IsNullOrWhiteSpace(supplierTaxId)
                    ? null
                    : await trustRepository.GetAsync(supplierTaxId, ct);

                if (trust?.IsTrusted != true)
                {
                    var pendingPath = await archiver.ArchivePendingAsync(document, invoice.Supplier.Name, ct);
                    await pendingRepository.SaveAsync(
                        PendingInvoice.From(invoice, extraction, document, pendingPath), ct);
                    await log.MarkProcessedAsync(document.ContentHash, invoice.Id, ct);
                    logger.LogInformation("Factura {Number} de {Name} → pending/ (revisión humana).",
                        invoice.InvoiceNumber, invoice.Supplier.Name);
                    return new ProcessInvoiceResult(true, invoice.Id, "Pendiente de revisión");
                }

                var archivedPath = await archiver.ArchiveProcessedAsync(document, invoice, ct);
                await repository.SaveAsync(StoredInvoice.From(invoice, document.ContentHash, archivedPath), ct);
                await log.MarkProcessedAsync(document.ContentHash, invoice.Id, ct);

                logger.LogInformation("Factura {Number} procesada desde {File}.", invoice.InvoiceNumber, document.FileName);
                return new ProcessInvoiceResult(true, invoice.Id, null);
            },
            onFailure: async error =>
            {
                logger.LogWarning("Factura inválida [{Code}]: {Msg}. Se mueve a failed/.", error.Code, error.Message);
                await archiver.ArchiveFailedAsync(document, ct);
                return new ProcessInvoiceResult(false, null, error.Message);
            });
    }
}
