namespace InvoiceProcessor.Infrastructure.Extraction.LlamaParse;

public sealed class LlamaParseOptions
{
    public string ParseEndpoint { get; init; } = "https://api.cloud.llamaindex.ai/api/v1/parsing/upload";
    public string ApiKey { get; init; } = string.Empty;
    public int MaxPollingAttempts { get; init; } = 40;
}
