using System.Security.Cryptography;
using System.Threading.Channels;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Worker;

public sealed class FolderWatcherService(
    IServiceScopeFactory scopeFactory,
    IMasterSpreadsheetWriter masterWriter,
    IOptions<FolderOptions> folders,
    ILogger<FolderWatcherService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IMasterSpreadsheetWriter _masterWriter = masterWriter;
    private readonly FolderOptions _folders = folders.Value;
    private readonly ILogger<FolderWatcherService> _logger = logger;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var inbox = Path.GetFullPath(_folders.Inbox);
        Directory.CreateDirectory(inbox);
        _logger.LogInformation("Watching inbox: {Inbox}", inbox);

        // Reprocesa lo que ya hubiera en la carpeta al arrancar
        await EnqueueExistingAsync(inbox, stoppingToken);

        // Detector 1: watcher en tiempo real (baja latencia). Solo encola.
        using var watcher = new FileSystemWatcher(inbox) { EnableRaisingEvents = true };
        watcher.Created += async (_, e) =>
        {
            if (IsPdf(e.FullPath))
                await _queue.Writer.WriteAsync(e.FullPath, stoppingToken);
        };

        // Detector 2: polling de respaldo multiplataforma (sólido en macOS).
        var poll = PollLoopAsync(inbox, stoppingToken);

        // Límite de concurrencia: protege la API de IA
        var gate = new SemaphoreSlim(_folders.MaxConcurrency);

        await foreach (var path in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await WaitUntilStableAsync(path, stoppingToken);
            await gate.WaitAsync(stoppingToken);

            _ = ProcessAsync(path, gate, stoppingToken); // fire-and-forget controlado por el semáforo
        }

        await poll;
    }

    private async Task RebuildMasterAsync(CancellationToken ct)
    {
        try { await _masterWriter.RebuildAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to rebuild master spreadsheet."); }
    }

    // Red de seguridad: escanea la carpeta cada N segundos y encola lo que vea.
    private async Task PollLoopAsync(string inbox, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_folders.PollSeconds));
        while (await timer.WaitForNextTickAsync(ct))
            await EnqueueExistingAsync(inbox, ct);
    }

    private async Task EnqueueExistingAsync(string inbox, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(inbox).Where(IsPdf))
            await _queue.Writer.WriteAsync(file, ct);
    }

    // Filtro de extensión idéntico en Windows, macOS y Linux.
    private static bool IsPdf(string path) =>
        Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    private async Task ProcessAsync(string path, SemaphoreSlim gate, CancellationToken ct)
    {
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogDebug("File {File} no longer in inbox (already moved), skipping.", Path.GetFileName(path));
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var useCase = scope.ServiceProvider.GetRequiredService<IProcessInvoiceUseCase>();

            _logger.LogInformation("Processing {File}...", Path.GetFileName(path));
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var doc = new IncomingDocument(
                DocumentId.New(), Path.GetFileName(path), path, hash, DateTimeOffset.UtcNow);

            var result = await useCase.ExecuteAsync(doc, ct);
            _logger.LogInformation("Done {File}: Success={Success} {Reason}",
                Path.GetFileName(path), result.Success, result.FailureReason ?? "");
            await RebuildMasterAsync(ct);
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing file {Path}", path);
        }
        finally { gate.Release(); }
    }

    private static Task WaitUntilStableAsync(string path, CancellationToken ct) =>
        InvoiceProcessor.Infrastructure.Files.FileStabilityWaiter.WaitUntilStableAsync(path, ct);
}
