using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Integration.Tests.Fixtures;
using InvoiceProcessor.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceProcessor.Integration.Tests.Api;

// The review screen talks to these endpoints and nothing else, so they are tested against the
// real host: routing, JSON shape, status codes and the PDF response headers the viewer relies on.
public sealed class PendingEndpointsTests : IClassFixture<IsolatedApiFactory>
{
    private readonly IsolatedApiFactory _factory;
    private readonly HttpClient _client;

    public PendingEndpointsTests(IsolatedApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private static ExtractionResult AnInvoiceFrom(string supplier, string taxId, string number) =>
        new(new Dictionary<string, ExtractedField>
        {
            ["invoice_number"]  = new(number, 1m),
            ["supplier_name"]   = new(supplier, 1m),
            ["supplier_tax_id"] = new(taxId, 0.42m),   // deliberately low: the UI flags this field
            ["issue_date"]      = new("2026-01-15", 1m),
            ["net_amount"]      = new("100.00", 1m),
            ["tax_amount"]      = new("21.00", 1m),
            ["total_amount"]    = new("121.00", 1m),
        }, [], 0.95m);

    // Drops a PDF through the real processing use case so the queue is populated the way it is
    // in production, not by writing rows straight into SQLite.
    private async Task<string> GivenAPendingInvoiceAsync(ExtractionResult extraction, string fileName)
    {
        _factory.Extractor.Next = extraction;

        var inbox = Path.Combine(_factory.Root, "inbox");
        Directory.CreateDirectory(inbox);
        var path = Path.Combine(inbox, fileName);
        var bytes = SampleInvoices.Render($"pdf for {fileName}");
        await File.WriteAllBytesAsync(path, bytes);

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IProcessInvoiceUseCase>().ExecuteAsync(
            new IncomingDocument(DocumentId.New(), fileName, path, hash, DateTimeOffset.UtcNow),
            CancellationToken.None);
        return hash;
    }

    [Fact]
    public async Task GetPending_ListsWhatIsWaitingForReview()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Suministros Aurora", "B10000001", "LIST-001"), "list.pdf");

        var response = await _client.GetAsync("/api/pending");
        response.EnsureSuccessStatusCode();

        var queue = await response.Content.ReadFromJsonAsync<JsonElement>();
        var entry = queue.EnumerateArray().Single(e => e.GetProperty("contentHash").GetString() == hash);
        Assert.Equal("LIST-001", entry.GetProperty("invoiceNumber").GetString());
        Assert.Equal("Suministros Aurora", entry.GetProperty("supplierName").GetString());
        Assert.False(entry.GetProperty("requiresManualEntry").GetBoolean());
        Assert.True(entry.GetProperty("pendingCountForSupplier").GetInt32() >= 1);
    }

    [Fact]
    public async Task GetPendingByHash_ReturnsPerFieldConfidenceAndTrustProgress()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Energía Boreal", "B10000002", "DET-001"), "detail.pdf");

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/pending/{hash}");

        Assert.Equal("DET-001", detail.GetProperty("invoiceNumber").GetString());
        // The reviewer needs to know which field to look at first.
        var confidence = detail.GetProperty("confidence");
        Assert.Equal(0.42m, confidence.GetProperty("supplierTaxId").GetProperty("confidence").GetDecimal());
        Assert.Equal(1m, confidence.GetProperty("invoiceNumber").GetProperty("confidence").GetDecimal());
        // …and why they are still reviewing this supplier at all.
        Assert.Equal(0, detail.GetProperty("trust").GetProperty("unmodifiedCount").GetInt32());
        Assert.Equal(3, detail.GetProperty("trust").GetProperty("threshold").GetInt32());
    }

    [Fact]
    public async Task GetPendingByHash_WhenUnknown_Returns404()
    {
        var response = await _client.GetAsync("/api/pending/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetPendingPdf_IsServedInlineAndSupportsRangeRequests()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Papelería Cronos", "B10000003", "PDF-001"), "viewer.pdf");

        var response = await _client.GetAsync($"/api/pending/{hash}/pdf");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        // No Content-Disposition ⇒ the browser renders it in the split view instead of downloading.
        Assert.Null(response.Content.Headers.ContentDisposition);
        // Accept-Ranges ⇒ the PDF viewer can load the document progressively.
        Assert.Contains("bytes", response.Headers.AcceptRanges);
    }

    [Fact]
    public async Task PutPending_ConfirmingUnchangedFields_AdvancesTrust()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Suministros Aurora", "B10000004", "CONF-001"), "confirm.pdf");
        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/pending/{hash}");

        var response = await _client.PutAsJsonAsync($"/api/pending/{hash}", new
        {
            invoiceNumber = detail.GetProperty("invoiceNumber").GetString(),
            supplierName  = detail.GetProperty("supplierName").GetString(),
            supplierTaxId = detail.GetProperty("supplierTaxId").GetString(),
            issueDate     = "2026-01-15",
            dueDate       = (string?)null,
            netAmount     = 100.00m,
            taxAmount     = 21.00m,
            totalAmount   = 121.00m,
            currency      = "EUR",
        });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("wasModified").GetBoolean());
        Assert.Equal(1, body.GetProperty("unmodifiedCount").GetInt32());
        Assert.False(body.GetProperty("isTrusted").GetBoolean());

        // And it is gone from the queue.
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/pending/{hash}")).StatusCode);
    }

    [Fact]
    public async Task PutPending_WithAnUnparseableDate_Returns400WithAMessage()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Suministros Aurora", "B10000005", "BAD-DATE"), "baddate.pdf");

        var response = await _client.PutAsJsonAsync($"/api/pending/{hash}", new
        {
            invoiceNumber = "BAD-DATE",
            supplierName  = "Suministros Aurora",
            supplierTaxId = "B10000005",
            issueDate     = "no-es-una-fecha",
            dueDate       = (string?)null,
            netAmount     = 100.00m,
            taxAmount     = 21.00m,
            totalAmount   = 121.00m,
            currency      = "EUR",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("Fecha", body.GetProperty("error").GetString());
        // The invoice is still in the queue — a bad submission must not lose it.
        (await _client.GetAsync($"/api/pending/{hash}")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeletePending_RejectsTheInvoice()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Suministros Aurora", "B10000006", "REJ-001"), "reject.pdf");

        var response = await _client.DeleteAsync($"/api/pending/{hash}");
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("pdfWasMissing").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/pending/{hash}")).StatusCode);
    }

    [Fact]
    public async Task PostRequeue_SendsThePdfBackToTheInbox()
    {
        var hash = await GivenAPendingInvoiceAsync(
            AnInvoiceFrom("Suministros Aurora", "B10000007", "REQ-001"), "requeue.pdf");

        var response = await _client.PostAsync($"/api/pending/{hash}/requeue", null);
        response.EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/pending/{hash}")).StatusCode);
        Assert.True(File.Exists(Path.Combine(_factory.Root, "inbox", "requeue.pdf")));
    }

    [Fact]
    public async Task DeletePending_WhenUnknown_Returns404()
    {
        var response = await _client.DeleteAsync("/api/pending/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
