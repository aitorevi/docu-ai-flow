using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Extraction.LlamaParse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace InvoiceProcessor.Integration.Tests.Extraction;

public sealed class LlamaParseExtractorContractTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly LlamaParseExtractor _sut;

    // Mirrors the real LlamaParse /result/json structure: text items + tables with rows.
    private const string SuccessResultJson = """
        {
          "pages": [
            {
              "confidence": 0.957,
              "items": [
                { "type": "text",  "value": "ACME S.L." },
                { "type": "text",  "value": "CIF: B12345678" },
                {
                  "type": "table",
                  "rows": [
                    ["Número:", "F2026-0042"],
                    ["Fecha:",  "15/01/2026"]
                  ]
                },
                {
                  "type": "table",
                  "rows": [
                    ["IMPORTE", "BASE IMPONIBLE", "IVA 21 %", "TOTAL"],
                    ["100,00",  "100,00",          "21,00",    "121,00 EUR"]
                  ]
                }
              ]
            }
          ]
        }
        """;

    public LlamaParseExtractorContractTests()
    {
        _server = WireMockServer.Start();

        _server
            .Given(Request.Create().WithPath("/api/v1/parsing/upload").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"test-job-1"}"""));

        _server
            .Given(Request.Create().WithPath("/api/v1/parsing/job/test-job-1/result/json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(SuccessResultJson));

        var options = Options.Create(new LlamaParseOptions
        {
            ParseEndpoint = $"{_server.Url}/api/v1/parsing/upload",
            ApiKey = "test-key",
        });

        var httpClient = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        _sut = new LlamaParseExtractor(httpClient, options, NullLogger<LlamaParseExtractor>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_maps_real_llamaparse_format_to_extraction_result()
    {
        // Given
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF header
        var content = new DocumentContent("invoice.pdf", "application/pdf", stream);

        // When
        var result = await _sut.ExtractAsync(content, CancellationToken.None);

        // Then — all required fields extracted from text items and table rows
        Assert.True(result.Fields.TryGetValue("invoice_number", out var numField));
        Assert.Equal("F2026-0042", numField!.Value);

        Assert.True(result.Fields.TryGetValue("issue_date", out var dateField));
        Assert.Equal("2026-01-15", dateField!.Value); // DD/MM/YYYY → YYYY-MM-DD

        Assert.True(result.Fields.TryGetValue("net_amount", out var netField));
        Assert.Equal("100.00", netField!.Value); // comma → period

        Assert.True(result.Fields.TryGetValue("tax_amount", out var taxField));
        Assert.Equal("21.00", taxField!.Value);

        Assert.True(result.Fields.TryGetValue("total_amount", out var totalField));
        Assert.Equal("121.00", totalField!.Value);

        Assert.True(result.Fields.TryGetValue("currency", out var currField));
        Assert.Equal("EUR", currField!.Value);

        Assert.True(result.OverallConfidence > 0.6m);
    }

    [Fact]
    public async Task ExtractAsync_throws_when_api_returns_server_error()
    {
        // Given: poll endpoint returns 500 (infrastructure error on LlamaParse side)
        using var errorServer = WireMockServer.Start();

        errorServer
            .Given(Request.Create().WithPath("/api/v1/parsing/upload").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"error-job-1"}"""));

        errorServer
            .Given(Request.Create().WithPath("/api/v1/parsing/job/error-job-1/result/json").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(500));

        var errorOptions = Options.Create(new LlamaParseOptions
        {
            ParseEndpoint = $"{errorServer.Url}/api/v1/parsing/upload",
            ApiKey = "test-key",
        });
        var errorHttpClient = new HttpClient { BaseAddress = new Uri(errorServer.Url!) };
        var errorSut = new LlamaParseExtractor(errorHttpClient, errorOptions, NullLogger<LlamaParseExtractor>.Instance);

        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var content = new DocumentContent("bad.pdf", "application/pdf", stream);

        // When / Then — 500 is a real infra error, not a "job pending" 404
        await Assert.ThrowsAsync<HttpRequestException>(
            () => errorSut.ExtractAsync(content, CancellationToken.None));
    }

    public void Dispose() => _server.Dispose();
}
