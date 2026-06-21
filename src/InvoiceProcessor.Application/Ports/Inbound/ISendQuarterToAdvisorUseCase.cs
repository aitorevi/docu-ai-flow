using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Ports.Inbound;

public interface ISendQuarterToAdvisorUseCase
{
    Task<DispatchResult> ExecuteAsync(Quarter quarter, CancellationToken ct, bool dryRun = false);
}
