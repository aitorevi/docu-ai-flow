using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Domain;
using SharpMonads.Core;

namespace InvoiceProcessor.Application.Ports.Inbound;

public interface IReviewInvoiceUseCase
{
    Task<IReadOnlyList<PendingInvoice>> GetPendingAsync(CancellationToken ct);
    Task<PendingInvoice?> GetPendingByHashAsync(string contentHash, CancellationToken ct);
    Task<Result<ConfirmResult, Error>> ConfirmAsync(string contentHash, CorrectedInvoiceFields fields, CancellationToken ct);
    Task<Result<RejectResult, Error>> RejectAsync(string contentHash, CancellationToken ct);
    Task<Result<RequeueResult, Error>> RequeueAsync(string contentHash, CancellationToken ct);
}
