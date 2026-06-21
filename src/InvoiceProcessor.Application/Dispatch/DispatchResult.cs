using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Dispatch;

public sealed record DispatchResult(Quarter Quarter, int Sent, bool NothingNew, int Parts = 1, bool DryRun = false);
