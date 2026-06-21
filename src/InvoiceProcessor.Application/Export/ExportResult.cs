using InvoiceProcessor.Domain.Dispatch;

namespace InvoiceProcessor.Application.Export;

public sealed record ExportResult(Quarter Quarter, int Exported, string? FilePath, bool NothingNew);
