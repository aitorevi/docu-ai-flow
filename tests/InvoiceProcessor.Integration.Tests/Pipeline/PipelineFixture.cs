using System.Security.Cryptography;
using System.Text.Json;
using InvoiceProcessor.Application;
using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Export;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure;
using InvoiceProcessor.Infrastructure.Files;
using InvoiceProcessor.Infrastructure.Mail;
using InvoiceProcessor.Integration.Tests.Fixtures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace InvoiceProcessor.Integration.Tests.Pipeline;

/// <summary>
/// Wires up the full DI graph with temp file system, temp SQLite DB, a fake invoice
/// extractor (set per test) and a WireMock stub for Resend.
/// One instance per test method (xUnit creates a new test class instance per test).
/// </summary>
public sealed class PipelineFixture : IDisposable
{
    // Canned extraction results per scenario, returned by the fake extractor.
    public static readonly ExtractionResult ValidInvoiceQ2 = Extraction(
        0.957m, "F2026-001", "Test Supplier S.L.", "B12345678", "2026-04-01", "100.00", "21.00", "121.00");

    public static readonly ExtractionResult ValidInvoiceQ1 = Extraction(
        0.921m, "F2026-Q1-001", "Test Supplier S.L.", "B12345678", "2026-01-15", "200.00", "42.00", "242.00");

    public static readonly ExtractionResult ValidInvoiceQ2B = Extraction(
        0.942m, "F2026-002", "Test Supplier S.L.", "B12345678", "2026-04-15", "200.00", "42.00", "242.00");

    public static readonly ExtractionResult LowConfidence =
        new(new Dictionary<string, ExtractedField>(), [], 0.3m);

    private static ExtractionResult Extraction(
        decimal confidence, string invoiceNumber, string supplierName, string supplierTaxId,
        string issueDate, string net, string tax, string total) =>
        new(new Dictionary<string, ExtractedField>
        {
            ["invoice_number"]  = new(invoiceNumber, 1m),
            ["supplier_name"]   = new(supplierName, 1m),
            ["supplier_tax_id"] = new(supplierTaxId, 1m),
            ["issue_date"]      = new(issueDate, 1m),
            ["net_amount"]      = new(net, 1m),
            ["tax_amount"]      = new(tax, 1m),
            ["total_amount"]    = new(total, 1m),
            ["currency"]        = new("EUR", 1m),
        }, [], confidence);

    private readonly string _root;
    private readonly WireMockServer _server;
    private readonly FakeInvoiceDataExtractor _extractor = new();
    private readonly IServiceProvider _provider;

    public string InboxPath { get; }
    public string ArchivePath { get; }
    public string FailedPath { get; }
    public string OutputPath { get; }

    public PipelineFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), $"pipeline-{Guid.NewGuid():N}");
        InboxPath   = Path.Combine(_root, "inbox");
        ArchivePath = Path.Combine(_root, "archive");
        FailedPath  = Path.Combine(_root, "failed");
        OutputPath  = Path.Combine(_root, "output");

        foreach (var dir in new[] { InboxPath, ArchivePath, FailedPath, OutputPath })
            Directory.CreateDirectory(dir);

        _server = WireMockServer.Start();

        var dbPath = Path.Combine(_root, "invoices.db");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Folders:Inbox"]          = InboxPath,
                ["Folders:Archive"]        = ArchivePath,
                ["Folders:Failed"]         = FailedPath,
                ["Folders:Output"]         = OutputPath,
                ["Folders:MaxConcurrency"] = "3",
                ["Folders:PollSeconds"]    = "5",
                ["Database:Path"]          = dbPath,
                ["Extraction:ConfidenceThreshold"] = "0.6",
                ["Resend:ApiBaseUrl"]       = _server.Url!,
                ["Resend:ApiKey"]           = "test-key",
                ["Resend:FromName"]         = "Test Sender",
                ["Resend:FromAddress"]      = "sender@test.com",
                ["Resend:AdvisorAddress"]   = "advisor@test.com",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddApplication();
        services.AddInfrastructure(config);

        services.AddSingleton<IInvoiceDataExtractor>(_extractor);

        services.AddHttpClient<ResendAdvisorMailSender>();
        services.AddSingleton<IAdvisorMailSender>(sp =>
            sp.GetRequiredService<ResendAdvisorMailSender>());

        _provider = services.BuildServiceProvider();
    }

    // ── Stub helpers ──────────────────────────────────────────────────────────

    // Sets the extraction result the fake extractor will return for the next ProcessAsync.
    public void StubExtraction(ExtractionResult result) => _extractor.Next = result;

    public void StubResend()
    {
        _server
            .Given(Request.Create().WithPath("/emails").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"email-001"}"""));
    }

    public int ResendCallCount =>
        _server.LogEntries.Count(e => e.RequestMessage?.Path == "/emails");

    public JsonElement LastResendBody()
    {
        var entry = _server.LogEntries.Last(e => e.RequestMessage?.Path == "/emails");
        return JsonDocument.Parse(entry.RequestMessage!.Body!).RootElement;
    }

    // ── File system helpers ───────────────────────────────────────────────────

    public async Task<string> PlaceInboxAsync(string filename = "invoice.pdf", byte[]? bytes = null)
    {
        var path = Path.Combine(InboxPath, filename);
        await File.WriteAllBytesAsync(path, bytes ?? MinimalPdf.Bytes());
        return path;
    }

    public string PlaceArchive(int year, int month, string filename, byte[]? bytes = null)
    {
        var dir = Path.Combine(ArchivePath, year.ToString("D4"), month.ToString("D2"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, filename);
        File.WriteAllBytes(path, bytes ?? MinimalPdf.Bytes());
        return path;
    }

    // ── Use-case helpers ─────────────────────────────────────────────────────

    public async Task<ProcessInvoiceResult> ProcessAsync(string pdfPath, CancellationToken ct = default)
    {
        using var scope = _provider.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<IProcessInvoiceUseCase>();
        var bytes = await File.ReadAllBytesAsync(pdfPath, ct);
        var hash  = Convert.ToHexString(SHA256.HashData(bytes));
        var doc   = new IncomingDocument(
            DocumentId.New(), Path.GetFileName(pdfPath), pdfPath, hash, DateTimeOffset.UtcNow);
        return await useCase.ExecuteAsync(doc, ct);
    }

    public async Task<ExportResult> ExportAsync(int year, int quarter, CancellationToken ct = default)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IExportQuarterToSpreadsheetUseCase>()
            .ExecuteAsync(new InvoiceProcessor.Domain.Dispatch.Quarter(year, quarter), ct);
    }

    public async Task<DispatchResult> SendAsync(int year, int quarter, CancellationToken ct = default)
    {
        using var scope = _provider.CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<ISendQuarterToAdvisorUseCase>()
            .ExecuteAsync(new InvoiceProcessor.Domain.Dispatch.Quarter(year, quarter), ct);
    }

    // ── DB helpers ────────────────────────────────────────────────────────────

    public async Task<List<StoredInvoice>> GetAllInvoicesAsync()
    {
        var repo = _provider.GetRequiredService<IProcessedInvoiceRepository>();
        var list = new List<StoredInvoice>();
        await foreach (var inv in repo.ListAllAsync(CancellationToken.None))
            list.Add(inv);
        return list;
    }

    public async Task<int> GetSentCountAsync()
    {
        var log = _provider.GetRequiredService<ISentInvoiceLog>();
        // Just check a non-existent hash to force table creation; count via DB helper
        await log.WasSentAsync("__probe__", CancellationToken.None);

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_root, "invoices.db")}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sent_invoices;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task<int> GetExportedCountAsync()
    {
        var log = _provider.GetRequiredService<IExportedInvoiceLog>();
        await log.WasExportedAsync("__probe__", CancellationToken.None);

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection(
            $"Data Source={Path.Combine(_root, "invoices.db")}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM exported_invoices;";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    public async Task SaveInvoiceDirectlyAsync(StoredInvoice invoice)
    {
        var repo = _provider.GetRequiredService<IProcessedInvoiceRepository>();
        await repo.SaveAsync(invoice, CancellationToken.None);
    }

    public async Task MarkSentDirectlyAsync(string contentHash, int year, int quarter)
    {
        var log = _provider.GetRequiredService<ISentInvoiceLog>();
        await log.MarkSentAsync([contentHash], new InvoiceProcessor.Domain.Dispatch.Quarter(year, quarter),
            DateTimeOffset.UtcNow, CancellationToken.None);
    }

    public async Task MarkExportedDirectlyAsync(string contentHash, int year, int quarter)
    {
        var log = _provider.GetRequiredService<IExportedInvoiceLog>();
        await log.MarkExportedAsync([contentHash], new InvoiceProcessor.Domain.Dispatch.Quarter(year, quarter),
            DateTimeOffset.UtcNow, CancellationToken.None);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        (_provider as IDisposable)?.Dispose();
        _server.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
