using InvoiceProcessor.Application.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IInvoiceArchiveCompressor
{
    Task<CompressedArchive> CompressAsync(IReadOnlyCollection<string> filePaths, string archiveName, CancellationToken ct);
}
