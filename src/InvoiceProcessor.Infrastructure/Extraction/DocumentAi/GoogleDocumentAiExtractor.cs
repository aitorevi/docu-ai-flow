using Google.Cloud.DocumentAI.V1;
using Google.Protobuf;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Suppliers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Extraction.DocumentAi;

// Alternative extractor backed by Google Cloud Document AI's Invoice Parser, which
// returns invoice fields as typed entities instead of raw text + tables.
// Credentials come from the GOOGLE_APPLICATION_CREDENTIALS env var (ADC).
public sealed class GoogleDocumentAiExtractor : IInvoiceDataExtractor
{
    // Rough Invoice Parser price per page, for cost-awareness logging only.
    private const decimal EstimatedCostPerCall = 0.10m;

    private readonly GoogleDocumentAiOptions _opts;
    private readonly CompanyOptions _company;
    private readonly DocumentProcessorServiceClient _client;
    private readonly ILogger<GoogleDocumentAiExtractor> _logger;
    private int _sessionCalls;

    public GoogleDocumentAiExtractor(
        IOptions<GoogleDocumentAiOptions> opts, IOptions<CompanyOptions> company,
        ILogger<GoogleDocumentAiExtractor> logger)
    {
        _opts = opts.Value;
        _company = company.Value;
        _logger = logger;
        // Document AI is regional: the endpoint must match the processor's location.
        _client = new DocumentProcessorServiceClientBuilder
        {
            Endpoint = $"{_opts.Location}-documentai.googleapis.com",
        }.Build();
    }

    public async Task<ExtractionResult> ExtractAsync(DocumentContent content, CancellationToken ct)
    {
        var json = await FetchRawJsonAsync(content, ct);
        return GoogleDocumentAiMapper.ToExtractionResult(json, _company.TaxId, _company.Name);
    }

    // Processes the PDF and returns the Document proto serialized as JSON, unmapped.
    // Used both for extraction and to capture real responses into fixtures.
    public async Task<string> FetchRawJsonAsync(DocumentContent content, CancellationToken ct)
    {
        var calls = Interlocked.Increment(ref _sessionCalls);
        _logger.LogWarning(
            "Document AI (servicio de pago) procesando {File} — {Calls} documento(s) esta sesión (~{Cost:0.00} $ aprox).",
            content.FileName, calls, calls * EstimatedCostPerCall);

        using var ms = new MemoryStream();
        await content.Stream.CopyToAsync(ms, ct);

        var name = ProcessorName.FromProjectLocationProcessor(_opts.ProjectId, _opts.Location, _opts.ProcessorId);
        var request = new ProcessRequest
        {
            Name = name.ToString(),
            RawDocument = new RawDocument
            {
                Content = ByteString.CopyFrom(ms.ToArray()),
                MimeType = "application/pdf",
            },
        };

        var response = await _client.ProcessDocumentAsync(request, ct);
        return JsonFormatter.Default.Format(response.Document);
    }
}
