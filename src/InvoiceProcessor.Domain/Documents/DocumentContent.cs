namespace InvoiceProcessor.Domain.Documents;

public sealed record DocumentContent(string FileName, string MediaType, Stream Stream) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
