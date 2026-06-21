using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Extensions.Logging;

namespace InvoiceProcessor.Application.Export;

public sealed class ExportQuarterToSpreadsheetService(
    IProcessedInvoiceRepository repository,
    IExportedInvoiceLog exportedLog,
    IQuarterSpreadsheetExporter exporter,
    IMasterSpreadsheetWriter master,
    ILogger<ExportQuarterToSpreadsheetService> logger) : IExportQuarterToSpreadsheetUseCase
{
    public async Task<ExportResult> ExecuteAsync(Quarter quarter, CancellationToken ct)
    {
        var (start, end) = quarter.ExcelSourceRange();

        var pending = new List<Invoices.StoredInvoice>();
        await foreach (var inv in repository.ListByDateRangeAsync(start, end, ct))
            if (!await exportedLog.WasExportedAsync(inv.ContentHash, ct))
                pending.Add(inv);

        if (pending.Count == 0)
        {
            logger.LogInformation("Trimestre {Q}: nada nuevo que exportar.", quarter);
            return new ExportResult(quarter, 0, null, NothingNew: true);
        }

        var path = await exporter.ExportAsync(quarter, pending, ct);
        var hashes = pending.Select(p => p.ContentHash).ToList();
        await exportedLog.MarkExportedAsync(hashes, quarter, DateTimeOffset.UtcNow, ct);

        await repository.MarkDeclaredAsync(hashes, quarter, ct);
        await master.RebuildAsync(ct);

        logger.LogInformation("Trimestre {Q}: {N} facturas exportadas → {Path}.", quarter, pending.Count, path);
        return new ExportResult(quarter, pending.Count, path, NothingNew: false);
    }
}
