using System.Net.Http.Headers;
using System.Text.Json;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Extraction.LlamaParse;

public sealed class LlamaParseExtractor(
    HttpClient http,
    IOptions<LlamaParseOptions> opts,
    ILogger<LlamaParseExtractor> logger)
    : IInvoiceDataExtractor
{
    public async Task<ExtractionResult> ExtractAsync(DocumentContent content, CancellationToken ct)
    {
        var options = opts.Value;

        var jobId = await UploadAsync(content, options, ct);
        var resultJson = await PollForResultAsync(jobId, options, ct);

        logger.LogDebug("LlamaParse raw JSON for job {JobId}:\n{Json}", jobId, resultJson);

        return LlamaParseMapper.ToExtractionResult(resultJson);
    }

    private async Task<string> UploadAsync(DocumentContent content, LlamaParseOptions options, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(content.Stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(streamContent, "file", content.FileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, options.ParseEndpoint)
        {
            Content = form
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

        var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var jobId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("LlamaParse upload response did not contain a job id.");

        return jobId;
    }

    private async Task<string> PollForResultAsync(string jobId, LlamaParseOptions options, CancellationToken ct)
    {
        // LlamaParse uses HTTP status to signal job state:
        //   404 → job still processing, keep polling
        //   200 → job done, body is the result JSON directly (no status wrapper)
        //   other → unexpected error, let it bubble
        var resultUrl = BuildResultUrl(options.ParseEndpoint, jobId);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        for (var attempt = 0; attempt < options.MaxPollingAttempts; attempt++)
        {
            if (attempt > 0)
                await timer.WaitForNextTickAsync(ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, resultUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);

            var response = await http.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                continue; // job not ready yet

            response.EnsureSuccessStatusCode(); // any other non-2xx is a real error

            return await response.Content.ReadAsStringAsync(ct);
        }

        throw new InvalidOperationException(
            $"LlamaParse job '{jobId}' did not complete after {options.MaxPollingAttempts} polling attempts.");
    }

    private static string BuildResultUrl(string parseEndpoint, string jobId)
    {
        // ParseEndpoint: https://host/api/v1/parsing/upload
        // Result URL:    https://host/api/v1/parsing/job/{id}/result/json
        var uploadSuffix = "/upload";
        var basePath = parseEndpoint.EndsWith(uploadSuffix, StringComparison.OrdinalIgnoreCase)
            ? parseEndpoint[..^uploadSuffix.Length]
            : parseEndpoint;

        return $"{basePath}/job/{jobId}/result/json";
    }
}
