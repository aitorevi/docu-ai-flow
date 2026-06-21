using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Application.Invoices;

public sealed class ProcessInvoiceService(
    IDocumentReader reader,
    IInvoiceDataExtractor extractor,
    ISupplierNormalizer supplierNormalizer,
    IProcessedInvoiceRepository repository,
    IDocumentArchiver archiver,
    IProcessedDocumentLog log,
    ILogger<ProcessInvoiceService> logger,
    IOptions<ExtractionOptions> extractionOptions) : IProcessInvoiceUseCase
{
    public async Task<ProcessInvoiceResult> ExecuteAsync(IncomingDocument document, CancellationToken ct)
    {
        if (await log.WasProcessedAsync(document.ContentHash, ct))
        {
            logger.LogInformation("Documento {File} ya procesado (hash duplicado). Se omite.", document.FileName);
            return new ProcessInvoiceResult(true, null, "Duplicado");
        }

        await using var content = await reader.OpenAsync(document, ct);
        var extraction = await extractor.ExtractAsync(content, ct);

        var mapped = ExtractionToInvoiceMapper.Map(extraction, supplierNormalizer,
            extractionOptions.Value.ConfidenceThreshold);

        return await mapped.Match(
            onSuccess: async invoice =>
            {
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
