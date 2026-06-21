using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Ports.Outbound;
using System.IO.Compression;

namespace InvoiceProcessor.Infrastructure.Dispatch;

public sealed class ZipInvoiceArchiveCompressor : IInvoiceArchiveCompressor
{
    public Task<CompressedArchive> CompressAsync(
        IReadOnlyCollection<string> filePaths, string archiveName, CancellationToken ct)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"{archiveName}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            foreach (var file in filePaths)
                zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
        var sizeBytes = new FileInfo(zipPath).Length;
        return Task.FromResult(new CompressedArchive(Path.GetFileName(zipPath), zipPath, sizeBytes));
    }
}
