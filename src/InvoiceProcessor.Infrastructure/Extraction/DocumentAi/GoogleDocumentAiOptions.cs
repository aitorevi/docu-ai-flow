namespace InvoiceProcessor.Infrastructure.Extraction.DocumentAi;

public sealed class GoogleDocumentAiOptions
{
    public string ProjectId { get; init; } = string.Empty;
    public string Location { get; init; } = "eu";
    public string ProcessorId { get; init; } = string.Empty;
}
