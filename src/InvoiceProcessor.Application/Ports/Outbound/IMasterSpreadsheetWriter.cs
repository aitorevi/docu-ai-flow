namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IMasterSpreadsheetWriter
{
    Task RebuildAsync(CancellationToken ct);
}
