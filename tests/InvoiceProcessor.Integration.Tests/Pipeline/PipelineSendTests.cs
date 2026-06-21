using System.Security.Cryptography;
using InvoiceProcessor.Integration.Tests.Fixtures;

namespace InvoiceProcessor.Integration.Tests.Pipeline;

public sealed class PipelineSendTests : IDisposable
{
    private readonly PipelineFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    private static string HashOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    [Fact]
    public async Task SendQuarter_CreatesZipAndSendsEmail()
    {
        _fx.StubResend();
        _fx.PlaceArchive(2026, 4, "supplier-F2026-001.pdf");
        _fx.PlaceArchive(2026, 5, "supplier-F2026-002.pdf");

        var result = await _fx.SendAsync(2026, 2);

        Assert.False(result.NothingNew);
        Assert.Equal(2, result.Sent);
        Assert.Equal(1, _fx.ResendCallCount);

        var body = _fx.LastResendBody();
        var attachmentName = body.GetProperty("attachments")[0].GetProperty("filename").GetString();
        Assert.StartsWith("facturas_2026-Q2", attachmentName);
        Assert.EndsWith(".zip", attachmentName);
    }

    [Fact]
    public async Task SendQuarter_EmailHasCorrectRecipient()
    {
        _fx.StubResend();
        _fx.PlaceArchive(2026, 4, "inv.pdf");

        await _fx.SendAsync(2026, 2);

        var body = _fx.LastResendBody();
        var to = body.GetProperty("to")[0].GetString();
        Assert.Equal("advisor@test.com", to);
    }

    [Fact]
    public async Task SendQuarter_MarksInvoicesAsSent()
    {
        _fx.StubResend();
        _fx.PlaceArchive(2026, 4, "inv-a.pdf", MinimalPdf.Bytes());
        _fx.PlaceArchive(2026, 4, "inv-b.pdf", [.. MinimalPdf.Bytes(), (byte)0xAA]);

        await _fx.SendAsync(2026, 2);

        var count = await _fx.GetSentCountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task SendQuarter_NothingNew_WhenAlreadySent()
    {
        _fx.StubResend();
        _fx.PlaceArchive(2026, 4, "inv.pdf");

        var first = await _fx.SendAsync(2026, 2);
        Assert.False(first.NothingNew);

        var second = await _fx.SendAsync(2026, 2);
        Assert.True(second.NothingNew);

        // Resend was only called once — no duplicate email
        Assert.Equal(1, _fx.ResendCallCount);
    }

    [Fact]
    public async Task SendQuarter_OnlyPendingInvoices_AreIncluded()
    {
        _fx.StubResend();

        var bytesA = MinimalPdf.Bytes();
        byte[] bytesB = [.. MinimalPdf.Bytes(), (byte)0x01];
        byte[] bytesC = [.. MinimalPdf.Bytes(), (byte)0x02];

        _fx.PlaceArchive(2026, 4, "inv-a.pdf", bytesA);
        _fx.PlaceArchive(2026, 4, "inv-b.pdf", bytesB);
        _fx.PlaceArchive(2026, 4, "inv-c.pdf", bytesC);

        // Pre-mark A and B as already sent
        await _fx.MarkSentDirectlyAsync(HashOf(bytesA), 2026, 2);
        await _fx.MarkSentDirectlyAsync(HashOf(bytesB), 2026, 2);

        var result = await _fx.SendAsync(2026, 2);

        Assert.False(result.NothingNew);
        Assert.Equal(1, result.Sent); // only C was pending
        Assert.Equal(1, _fx.ResendCallCount);

        // sent_invoices should now have 3 rows (2 pre-marked + 1 newly sent)
        var count = await _fx.GetSentCountAsync();
        Assert.Equal(3, count);
    }
}
