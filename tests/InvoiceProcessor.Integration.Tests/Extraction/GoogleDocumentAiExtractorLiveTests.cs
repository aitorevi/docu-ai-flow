using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Extraction.DocumentAi;
using InvoiceProcessor.Infrastructure.Suppliers;
using InvoiceProcessor.Integration.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Integration.Tests.Extraction;

// Smoke test that calls the real Google Document AI API.
// Skipped automatically when GOOGLE_APPLICATION_CREDENTIALS is absent.
// Run with: dotnet test --filter "Category=LiveGcp"
public sealed class GoogleDocumentAiExtractorLiveTests
{
    [Fact]
    [Trait("Category", "LiveGcp")]
    public async Task ExtractAsync_processes_real_pdf_and_returns_fields()
    {
        var credentials = Environment.GetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS");
        var projectId = Environment.GetEnvironmentVariable("GOOGLE_PROJECT_ID");
        var location = Environment.GetEnvironmentVariable("GOOGLE_LOCATION");
        var processorId = Environment.GetEnvironmentVariable("GOOGLE_PROCESSOR_ID");

        if (string.IsNullOrWhiteSpace(credentials) ||
            string.IsNullOrWhiteSpace(projectId) ||
            string.IsNullOrWhiteSpace(location) ||
            string.IsNullOrWhiteSpace(processorId))
        {
            // Skip: live GCP credentials not configured in this environment.
            return;
        }

        var opts = Options.Create(new GoogleDocumentAiOptions
        {
            ProjectId = projectId,
            Location = location,
            ProcessorId = processorId,
        });
        var company = Options.Create(new CompanyOptions());
        var logger = NullLogger<GoogleDocumentAiExtractor>.Instance;

        var extractor = new GoogleDocumentAiExtractor(opts, company, logger);

        // Use the minimal PDF fixture shipped with the integration tests.
        var pdfBytes = MinimalPdf.Bytes();
        await using var content = new DocumentContent("test.pdf", "application/pdf", new MemoryStream(pdfBytes));

        var result = await extractor.ExtractAsync(content, CancellationToken.None);

        Assert.NotEmpty(result.Fields);
    }
}
