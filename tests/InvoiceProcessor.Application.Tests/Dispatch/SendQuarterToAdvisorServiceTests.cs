using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using System.Runtime.CompilerServices;

namespace InvoiceProcessor.Application.Tests.Dispatch;

public sealed class SendQuarterToAdvisorServiceTests
{
    private readonly IArchivedInvoiceSource _source = Substitute.For<IArchivedInvoiceSource>();
    private readonly ISentInvoiceLog _sentLog = Substitute.For<ISentInvoiceLog>();
    private readonly IInvoiceArchiveCompressor _compressor = Substitute.For<IInvoiceArchiveCompressor>();
    private readonly IAdvisorMailSender _mailSender = Substitute.For<IAdvisorMailSender>();

    private readonly ISendQuarterToAdvisorUseCase _sut;

    private static readonly Quarter TestQuarter = new(2026, 1);

    public SendQuarterToAdvisorServiceTests()
    {
        _sut = new SendQuarterToAdvisorService(_source, _sentLog, _compressor, _mailSender);
    }

    [Fact]
    public async Task ExecuteAsync_returns_NothingNew_when_all_already_sent()
    {
        // Given
        var invoice = new ArchivedInvoice("/archive/2026/01/inv1.pdf", "hash1");
        _source.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([invoice]));
        _sentLog.WasSentAsync("hash1", Arg.Any<CancellationToken>())
            .Returns(true);

        // When
        var result = await _sut.ExecuteAsync(TestQuarter, CancellationToken.None);

        // Then
        Assert.True(result.NothingNew);
        Assert.Equal(0, result.Sent);
        await _compressor.DidNotReceive()
            .CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_sends_pending_and_marks_them()
    {
        // Given
        var inv1 = new ArchivedInvoice("/archive/2026/01/inv1.pdf", "hash1");
        var inv2 = new ArchivedInvoice("/archive/2026/02/inv2.pdf", "hash2");
        _source.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([inv1, inv2]));
        _sentLog.WasSentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _compressor.CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompressedArchive("facturas_2026-Q1.zip", "/tmp/facturas_2026-Q1.zip"));

        // When
        var result = await _sut.ExecuteAsync(TestQuarter, CancellationToken.None);

        // Then
        Assert.False(result.NothingNew);
        Assert.Equal(2, result.Sent);

        await _compressor.Received(1)
            .CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _mailSender.Received(1)
            .SendAsync(Arg.Any<MailWithAttachment>(), Arg.Any<CancellationToken>());
        await _sentLog.Received(1)
            .MarkSentAsync(Arg.Any<IEnumerable<string>>(), TestQuarter, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_does_not_mark_if_send_throws()
    {
        // Given
        var invoice = new ArchivedInvoice("/archive/2026/01/inv1.pdf", "hash1");
        _source.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([invoice]));
        _sentLog.WasSentAsync("hash1", Arg.Any<CancellationToken>())
            .Returns(false);
        _compressor.CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompressedArchive("facturas_2026-Q1.zip", "/tmp/facturas_2026-Q1.zip"));
        _mailSender.SendAsync(Arg.Any<MailWithAttachment>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("API error"));

        // When / Then
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            _sut.ExecuteAsync(TestQuarter, CancellationToken.None));

        await _sentLog.DidNotReceive()
            .MarkSentAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<Quarter>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_sends_single_email_when_archive_is_within_limit()
    {
        // Given — 10 MB limit, 5 MB archive
        var settings = Options.Create(new MailDispatchSettings { MaxAttachmentMb = 10 });
        var sut = new SendQuarterToAdvisorService(_source, _sentLog, _compressor, _mailSender, settings);

        var inv = new ArchivedInvoice("/archive/2026/01/inv1.pdf", "hash1", new DateOnly(2026, 1, 15));
        _source.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([inv]));
        _sentLog.WasSentAsync("hash1", Arg.Any<CancellationToken>()).Returns(false);
        _compressor.CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompressedArchive("facturas_2026-Q1.zip", "/tmp/facturas_2026-Q1.zip", FileSizeBytes: 5 * 1024 * 1024));

        // When
        var result = await sut.ExecuteAsync(TestQuarter, CancellationToken.None);

        // Then
        Assert.Equal(1, result.Parts);
        await _mailSender.Received(1).SendAsync(Arg.Any<MailWithAttachment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_splits_into_monthly_parts_when_archive_exceeds_limit()
    {
        // Given — 10 MB limit, 50 MB combined archive → splits into 2 monthly parts
        var settings = Options.Create(new MailDispatchSettings { MaxAttachmentMb = 10 });
        var sut = new SendQuarterToAdvisorService(_source, _sentLog, _compressor, _mailSender, settings);

        var invJan = new ArchivedInvoice("/archive/2026/01/inv1.pdf", "hash1", new DateOnly(2026, 1, 15));
        var invFeb = new ArchivedInvoice("/archive/2026/02/inv2.pdf", "hash2", new DateOnly(2026, 2, 20));
        _source.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([invJan, invFeb]));
        _sentLog.WasSentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        _compressor.CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                new CompressedArchive("all.zip", "/tmp/all.zip", FileSizeBytes: 50 * 1024 * 1024),
                new CompressedArchive("jan.zip", "/tmp/jan.zip", FileSizeBytes:  5 * 1024 * 1024),
                new CompressedArchive("feb.zip", "/tmp/feb.zip", FileSizeBytes:  5 * 1024 * 1024));

        // When
        var result = await sut.ExecuteAsync(TestQuarter, CancellationToken.None);

        // Then
        Assert.Equal(2, result.Parts);
        Assert.Equal(2, result.Sent);
        await _mailSender.Received(2).SendAsync(Arg.Any<MailWithAttachment>(), Arg.Any<CancellationToken>());
        await _sentLog.Received(1)
            .MarkSentAsync(Arg.Any<IEnumerable<string>>(), TestQuarter, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_with_dryRun_sends_email_but_does_not_mark_as_sent()
    {
        // Given
        var inv = new ArchivedInvoice("/archive/2026/01/inv1.pdf", "hash1");
        _source.ListByDateRangeAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(AsyncEnumerable([inv]));
        _sentLog.WasSentAsync("hash1", Arg.Any<CancellationToken>()).Returns(false);
        _compressor.CompressAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new CompressedArchive("facturas_2026-Q1.zip", "/tmp/facturas_2026-Q1.zip"));

        // When
        var result = await _sut.ExecuteAsync(TestQuarter, CancellationToken.None, dryRun: true);

        // Then
        Assert.False(result.NothingNew);
        Assert.Equal(1, result.Sent);
        Assert.True(result.DryRun);
        await _mailSender.Received(1).SendAsync(Arg.Any<MailWithAttachment>(), Arg.Any<CancellationToken>());
        await _sentLog.DidNotReceive()
            .MarkSentAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<Quarter>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private static async IAsyncEnumerable<ArchivedInvoice> AsyncEnumerable(
        ArchivedInvoice[] items,
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
