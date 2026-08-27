using InvoiceProcessor.Application.Invoices;

namespace InvoiceProcessor.Application.Ports.Inbound;

public interface IGetDashboardUseCase
{
    Task<DashboardSummary> ExecuteAsync(CancellationToken ct);
}
