using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Infrastructure.Files;
using InvoiceProcessor.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace InvoiceProcessor.Integration.Tests.Watcher;

public sealed class FolderWatcherServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _inbox;

    public FolderWatcherServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"watcher-tests-{Guid.NewGuid():N}");
        _inbox = Path.Combine(_root, "inbox");
        Directory.CreateDirectory(_inbox);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public async Task WhenPdfDroppedInInbox_CallsUseCaseWithinFiveSeconds()
    {
        // Given
        var useCase = Substitute.For<IProcessInvoiceUseCase>();
        useCase.ExecuteAsync(Arg.Any<IncomingDocument>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessInvoiceResult(true, null, null));

        var services = new ServiceCollection();
        services.AddScoped<IProcessInvoiceUseCase>(_ => useCase);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var options = new FolderOptions
        {
            Inbox = _inbox,
            Archive = Path.Combine(_root, "archive"),
            Failed = Path.Combine(_root, "failed"),
            Output = Path.Combine(_root, "output"),
            MaxConcurrency = 1,
            PollSeconds = 2
        };

        var masterWriter = Substitute.For<IMasterSpreadsheetWriter>();

        var sut = new FolderWatcherService(
            scopeFactory,
            masterWriter,
            Options.Create(options),
            NullLogger<FolderWatcherService>.Instance);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // When: start the watcher then drop a PDF
        var watcherTask = sut.StartAsync(cts.Token);
        await Task.Delay(200, cts.Token); // brief pause for watcher to initialize

        var pdfPath = Path.Combine(_inbox, "test-invoice.pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46], cts.Token); // %PDF

        // Then: use case is called within 5 seconds
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var calls = useCase.ReceivedCalls().ToList();
            if (calls.Count > 0) break;
            await Task.Delay(100);
        }

        await cts.CancelAsync();
        try { await watcherTask; } catch (OperationCanceledException) { }

        await useCase.Received().ExecuteAsync(
            Arg.Is<IncomingDocument>(d => d.FileName == "test-invoice.pdf"),
            Arg.Any<CancellationToken>());

        await masterWriter.Received().RebuildAsync(Arg.Any<CancellationToken>());
    }
}
