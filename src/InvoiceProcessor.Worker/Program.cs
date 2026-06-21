using InvoiceProcessor.Application;
using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using InvoiceProcessor.Infrastructure;
using InvoiceProcessor.Infrastructure.Extraction.DocumentAi;
using InvoiceProcessor.Infrastructure.Extraction.LlamaParse;
using InvoiceProcessor.Infrastructure.Mail;
using InvoiceProcessor.Worker;
using System.Diagnostics;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MailDispatchSettings>(builder.Configuration.GetSection("MailDispatch"));
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

// LlamaParseExtractor kept as a proof-of-result reference; no longer bound to IInvoiceDataExtractor.
builder.Services.AddHttpClient<LlamaParseExtractor>()
    .AddStandardResilienceHandler();

builder.Services.AddSingleton<GoogleDocumentAiExtractor>();
builder.Services.AddSingleton<IInvoiceDataExtractor>(sp =>
    sp.GetRequiredService<GoogleDocumentAiExtractor>());

builder.Services.AddHttpClient<ResendAdvisorMailSender>()
    .AddStandardResilienceHandler();
builder.Services.AddSingleton<IAdvisorMailSender>(sp =>
    sp.GetRequiredService<ResendAdvisorMailSender>());

builder.Services.AddHostedService<FolderWatcherService>();

var app = builder.Build();

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

app.MapGet("/api/health", () => Results.Ok(new { status = "running" }));

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

// Auto-open browser (skip in CI / test environments)
if (!Console.IsInputRedirected)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1500);
        try { Process.Start(new ProcessStartInfo("http://localhost:5000") { UseShellExecute = true }); }
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
