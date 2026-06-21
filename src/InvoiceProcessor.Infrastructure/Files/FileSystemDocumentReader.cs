using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;

namespace InvoiceProcessor.Infrastructure.Files;

public sealed class FileSystemDocumentReader : IDocumentReader
{
    public Task<DocumentContent> OpenAsync(IncomingDocument doc, CancellationToken ct)
    {
        Stream stream = File.OpenRead(doc.Location);
        return Task.FromResult(new DocumentContent(doc.FileName, "application/pdf", stream));
    }
}
