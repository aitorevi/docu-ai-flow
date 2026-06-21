using InvoiceProcessor.Application.Export;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Runtime.CompilerServices;

namespace InvoiceProcessor.Application.Tests.Export;

public sealed class ExportQuarterToSpreadsheetServiceTests
{
    private readonly IProcessedInvoiceRepository _repository = Substitute.For<IProcessedInvoiceRepository>();
    private readonly IExportedInvoiceLog _exportedLog = Substitute.For<IExportedInvoiceLog>();
    private readonly IQuarterSpreadsheetExporter _exporter = Substitute.For<IQuarterSpreadsheetExporter>();
    private readonly IMasterSpreadsheetWriter _master = Substitute.For<IMasterSpreadsheetWriter>();

    private readonly IExportQuarterToSpreadsheetUseCase _sut;

    private static readonly Quarter TestQuarter = new(2026, 1);

    private static StoredInvoice MakeInvoice(string hash, string number, DateOnly issueDate) =>
        new(hash, number, "Repsol", "A78374725",
            issueDate, null, 100m, 21m, 121m, "EUR", "/archive/test.pdf");

    public ExportQuarterToSpreadsheetServiceTests()
    {
        _sut = new ExportQuarterToSpreadsheetService(
            _repository, _exportedLog, _exporter, _master,
            NullLogger<ExportQuarterToSpreadsheetService>.Instance);
    }

    [Fact]
    public async Task ExecuteAsync_returns_NothingNew_when_all_already_exported()
    {
        // Given
        var invoice = MakeInvoice("hash1", "F001", new DateOnly(2026, 1, 15));
        _repository.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([invoice]));
        _exportedLog.WasExportedAsync("hash1", Arg.Any<CancellationToken>())
            .Returns(true);

        // When
        var result = await _sut.ExecuteAsync(TestQuarter, CancellationToken.None);

        // Then
        Assert.True(result.NothingNew);
        Assert.Equal(0, result.Exported);
        Assert.Null(result.FilePath);
        await _exporter.DidNotReceive()
            .ExportAsync(Arg.Any<Quarter>(), Arg.Any<IReadOnlyCollection<StoredInvoice>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_exports_pending_and_marks_them()
    {
        // Given
        var inv1 = MakeInvoice("hash1", "F001", new DateOnly(2026, 1, 15));
        var inv2 = MakeInvoice("hash2", "F002", new DateOnly(2026, 2, 10));
        _repository.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([inv1, inv2]));
        _exportedLog.WasExportedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _exporter.ExportAsync(Arg.Any<Quarter>(), Arg.Any<IReadOnlyCollection<StoredInvoice>>(), Arg.Any<CancellationToken>())
            .Returns("/output/facturas_extraidas_2026Q1_20260614_120000.xlsx");

        // When
        var result = await _sut.ExecuteAsync(TestQuarter, CancellationToken.None);

        // Then
        Assert.False(result.NothingNew);
        Assert.Equal(2, result.Exported);
        Assert.NotNull(result.FilePath);

        await _exporter.Received(1)
            .ExportAsync(TestQuarter, Arg.Any<IReadOnlyCollection<StoredInvoice>>(), Arg.Any<CancellationToken>());
        await _exportedLog.Received(1)
            .MarkExportedAsync(Arg.Any<IEnumerable<string>>(), TestQuarter, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repository.Received(1)
            .MarkDeclaredAsync(Arg.Any<IEnumerable<string>>(), TestQuarter, Arg.Any<CancellationToken>());
        await _master.Received(1).RebuildAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_does_not_mark_if_export_throws()
    {
        // Given
        var invoice = MakeInvoice("hash1", "F001", new DateOnly(2026, 1, 15));
        _repository.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([invoice]));
        _exportedLog.WasExportedAsync("hash1", Arg.Any<CancellationToken>())
            .Returns(false);
        _exporter.ExportAsync(Arg.Any<Quarter>(), Arg.Any<IReadOnlyCollection<StoredInvoice>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("Disk full"));

        // When / Then
        await Assert.ThrowsAsync<IOException>(() =>
            _sut.ExecuteAsync(TestQuarter, CancellationToken.None));

        await _exportedLog.DidNotReceive()
            .MarkExportedAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<Quarter>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await _repository.DidNotReceive()
            .MarkDeclaredAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<Quarter>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<StoredInvoice> AsyncEnumerable(
        StoredInvoice[] items,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.CompletedTask;
        }
    }
}
