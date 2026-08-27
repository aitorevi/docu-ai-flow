using InvoiceProcessor.Application;
using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure;
using InvoiceProcessor.Infrastructure.Extraction.DocumentAi;
using InvoiceProcessor.Infrastructure.Extraction.Ocr;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Infrastructure.Mail;
using InvoiceProcessor.Worker;
using Microsoft.Extensions.Options;
using System.Diagnostics;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MailDispatchSettings>(builder.Configuration.GetSection("MailDispatch"));
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// OCR fallback: the real Tesseract shell-out only when TemplateExtractor:OcrFallback:Enabled,
// otherwise a null object so scanned PDFs behave exactly as if OCR did not exist.
if (builder.Configuration.GetValue<bool>("TemplateExtractor:OcrFallback:Enabled"))
    builder.Services.AddSingleton<IOcrTextExtractor, TesseractOcrExtractor>();
else
    builder.Services.AddSingleton<IOcrTextExtractor, NullOcrTextExtractor>();

// Both extractors are registered as lazy singletons — neither is constructed until its type is
// first resolved — so in Template mode the Google client is never built and no credentials are
// needed. This is the whole point of the port: the choice lives here and nowhere else.
builder.Services.AddSingleton<TemplateInvoiceExtractor>();
builder.Services.AddSingleton<GoogleDocumentAiExtractor>();

var activeProvider = ExtractionOptions.ResolveProvider(builder.Configuration["Extraction:Provider"]);

if (activeProvider == ExtractionProvider.DocumentAi)
    builder.Services.AddSingleton<IInvoiceDataExtractor>(sp =>
        sp.GetRequiredService<GoogleDocumentAiExtractor>());
else
    builder.Services.AddSingleton<IInvoiceDataExtractor>(sp =>
        sp.GetRequiredService<TemplateInvoiceExtractor>());

builder.Services.AddHttpClient<ResendAdvisorMailSender>()
    .AddStandardResilienceHandler();
builder.Services.AddSingleton<IAdvisorMailSender>(sp =>
    sp.GetRequiredService<ResendAdvisorMailSender>());

builder.Services.AddHostedService<FolderWatcherService>();

var app = builder.Build();

app.Logger.LogInformation("Extractor activo: {Provider}", activeProvider);

// CLI mode "make-samples": dotnet run -- make-samples [outputDir]
if (args is ["make-samples", ..])
{
    Environment.ExitCode = await SampleInvoices.GenerateAsync(
        args.Length > 1 ? args[1] : "./data/samples");
    return;
}

// CLI mode "dump-text": dotnet run -- dump-text <pdf>
if (args is ["dump-text", var dumpPdf])
{
    Environment.ExitCode = await TemplateDiagnostics.DumpTextAsync(dumpPdf);
    return;
}

// CLI mode "template-check": dotnet run -- template-check <pdf-or-folder>
if (args is ["template-check", var checkTarget])
{
    using var checkScope = app.Services.CreateScope();
    Environment.ExitCode = await TemplateDiagnostics.CheckAsync(
        checkTarget,
        checkScope.ServiceProvider.GetRequiredService<IOptions<TemplateExtractorOptions>>().Value,
        checkScope.ServiceProvider.GetRequiredService<IInvoiceTemplateRepository>());
    return;
}

// CLI mode "master": dotnet run -- master  →  regenerates maestro_facturas.xlsx
if (args is ["master"])
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IMasterSpreadsheetWriter>()
        .RebuildAsync(CancellationToken.None);
    return;
}

// CLI mode "send": dotnet run -- send {year} {quarter} [--dry-run]
var sendArgs = args.Where(a => a != "--dry-run").ToArray();
if (sendArgs is ["send", var sy, var sq] &&
    int.TryParse(sy, out var y1) && int.TryParse(sq, out var n1))
{
    var dryRun = args.Contains("--dry-run");
    using var scope = app.Services.CreateScope();
    var useCase = scope.ServiceProvider.GetRequiredService<ISendQuarterToAdvisorUseCase>();
    var result = await useCase.ExecuteAsync(new Quarter(y1, n1), CancellationToken.None, dryRun);
    var tag = dryRun ? " [dry run — not marked as sent]" : "";
    Console.WriteLine(result.NothingNew
        ? $"Nothing new to send for {new Quarter(y1, n1)}."
        : result.Parts > 1
            ? $"Sent {result.Sent} invoices to advisor for {result.Quarter} ({result.Parts} parts).{tag}"
            : $"Sent {result.Sent} invoices to advisor for {result.Quarter}.{tag}");
    return;
}

// CLI mode "export": dotnet run -- export {year} {quarter}  →  generates quarter spreadsheet
if (args is ["export", var ey, var eq] &&
    int.TryParse(ey, out var y2) && int.TryParse(eq, out var n2))
{
    using var scope = app.Services.CreateScope();
    var useCase = scope.ServiceProvider.GetRequiredService<IExportQuarterToSpreadsheetUseCase>();
    var result = await useCase.ExecuteAsync(new Quarter(y2, n2), CancellationToken.None);
    Console.WriteLine(result.NothingNew
        ? $"Nothing new to export for {new Quarter(y2, n2)}."
        : $"Exported {result.Exported} invoices → {result.FilePath}");
    return;
}

// Watcher + web UI mode
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", async (
    IProcessedInvoiceRepository repository, IReviewInvoiceUseCase review, CancellationToken ct) =>
{
    var invoiceCount = 0;
    await foreach (var _ in repository.ListAllAsync(ct)) invoiceCount++;

    return Results.Ok(new
    {
        status = "running",
        // Which extractor is actually bound to the port. Worth surfacing: a misconfigured
        // Extraction:Provider is otherwise invisible until invoices start failing.
        extractor = activeProvider.ToString(),
        invoiceCount,
        // Drives the review badge — the only number in the UI that asks the user to act.
        pendingReview = (await review.GetPendingAsync(ct)).Count,
    });
});

app.MapReviewEndpoints();

app.MapPost("/api/export/{year:int}/{quarter:int}", async (int year, int quarter,
    IExportQuarterToSpreadsheetUseCase useCase, CancellationToken ct) =>
{
    var result = await useCase.ExecuteAsync(new Quarter(year, quarter), ct);
    return Results.Ok(new
    {
        nothingNew = result.NothingNew,
        exported   = result.Exported,
        filePath   = result.FilePath,
    });
});

app.MapPost("/api/send/{year:int}/{quarter:int}", async (int year, int quarter,
    ISendQuarterToAdvisorUseCase useCase, CancellationToken ct) =>
{
    var result = await useCase.ExecuteAsync(new Quarter(year, quarter), ct);
    return Results.Ok(new
    {
        nothingNew = result.NothingNew,
        sent       = result.Sent,
        parts      = result.Parts,
        quarter    = result.Quarter.ToString(),
    });
});

// Auto-open browser. Skipped when the output is redirected (CI, containers, scripts) and when
// the API is hosted in-process by the tests.
if (!Console.IsInputRedirected && !app.Environment.IsEnvironment("Testing"))
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        // Read the address the server actually bound to — hardcoding a port sends people to the
        // wrong URL as soon as ASPNETCORE_URLS says otherwise (the container listens on 8080).
        var url = app.Urls.FirstOrDefault()?.Replace("http://+", "http://localhost")
                          .Replace("http://0.0.0.0", "http://localhost")
                  ?? "http://localhost:5000";
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* browser open is best-effort */ }
    });
}

await app.RunAsync();

// Loads a .env file from the nearest ancestor directory that contains one.
// Lines follow KEY=value format; # comments and blank lines are ignored.
// .NET config maps Resend__ApiKey → Resend:ApiKey automatically.
static void LoadDotEnv()
{
    var dir = Directory.GetCurrentDirectory();
    string? envFile = null;
    while (dir is not null)
    {
        var candidate = Path.Combine(dir, ".env");
        if (File.Exists(candidate)) { envFile = candidate; break; }
        dir = Path.GetDirectoryName(dir);
    }
    if (envFile is null) return;

    foreach (var line in File.ReadAllLines(envFile))
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
        var idx = line.IndexOf('=');
        if (idx <= 0) continue;
        var key = line[..idx].Trim();
        var value = line[(idx + 1)..].Trim();
        Environment.SetEnvironmentVariable(key, value);
    }
}

// Named and public so WebApplicationFactory<Program> can host this exact app in tests: the
// endpoints are then exercised as they really run, not as a re-declared copy.
public partial class Program;
