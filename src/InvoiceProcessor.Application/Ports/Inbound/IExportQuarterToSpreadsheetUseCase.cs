using InvoiceProcessor.Application.Export;
using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Inbound;

public interface IExportQuarterToSpreadsheetUseCase
{
    Task<ExportResult> ExecuteAsync(Quarter quarter, CancellationToken ct);
}
