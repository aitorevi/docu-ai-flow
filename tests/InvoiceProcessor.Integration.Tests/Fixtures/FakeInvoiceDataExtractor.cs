using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;

namespace InvoiceProcessor.Integration.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="IInvoiceDataExtractor"/> for pipeline tests: returns a canned
/// <see cref="ExtractionResult"/> set per scenario, so the pipeline is exercised end to end
/// without any real extraction service.
/// </summary>
public sealed class FakeInvoiceDataExtractor : IInvoiceDataExtractor
{
    public ExtractionResult Next { get; set; } =
        new(new Dictionary<string, ExtractedField>(), [], 0m);

    public Task<ExtractionResult> ExtractAsync(DocumentContent content, CancellationToken ct) =>
        Task.FromResult(Next);
}
