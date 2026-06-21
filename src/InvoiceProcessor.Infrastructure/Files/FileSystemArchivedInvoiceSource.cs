using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Ports.Outbound;
using Microsoft.Extensions.Options;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace InvoiceProcessor.Infrastructure.Files;

public sealed class FileSystemArchivedInvoiceSource(IOptions<FolderOptions> folders)
    : IArchivedInvoiceSource
{
    public async IAsyncEnumerable<ArchivedInvoice> ListByDateRangeAsync(
        DateOnly start, DateOnly end, [EnumeratorCancellation] CancellationToken ct)
    {
        for (var m = new DateOnly(start.Year, start.Month, 1); m <= end; m = m.AddMonths(1))
        {
            var dir = Path.Combine(folders.Value.Archive, m.Year.ToString("D4"), m.Month.ToString("D2"));
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.pdf", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file, ct)));
                yield return new ArchivedInvoice(file, hash, InvoiceDate: m);
            }
        }
    }
}
