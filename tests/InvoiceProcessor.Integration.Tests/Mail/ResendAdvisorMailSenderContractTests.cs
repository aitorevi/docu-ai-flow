using System.IO.Compression;
using System.Text;
using System.Text.Json;
using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Infrastructure.Mail;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace InvoiceProcessor.Integration.Tests.Mail;

public sealed class ResendAdvisorMailSenderContractTests : IDisposable
{
    private readonly WireMockServer _server;
    private readonly ResendAdvisorMailSender _sut;
    private readonly string _tmpDir;

    private const string TestApiKey       = "re_test_key";
    private const string TestFromName     = "Test Sender";
    private const string TestFromAddress  = "sender@test.com";
    private const string TestAdvisorAddress = "advisor@test.com";

    public ResendAdvisorMailSenderContractTests()
    {
        _server = WireMockServer.Start();
        _tmpDir = Path.Combine(Path.GetTempPath(), $"resend-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);

        _server
            .Given(Request.Create().WithPath("/emails").UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"id":"email-001"}"""));

        var options = Options.Create(new ResendOptions
        {
            ApiBaseUrl     = _server.Url!,
            ApiKey         = TestApiKey,
            FromName       = TestFromName,
            FromAddress    = TestFromAddress,
            AdvisorAddress = TestAdvisorAddress,
        });
        _sut = new ResendAdvisorMailSender(
            new HttpClient(),
            options,
            NullLogger<ResendAdvisorMailSender>.Instance);
    }

    public void Dispose()
    {
        _server.Dispose();
        if (Directory.Exists(_tmpDir)) Directory.Delete(_tmpDir, recursive: true);
    }

    private string CreateZipAttachment(string filename = "facturas_2026-Q2.zip")
    {
        var path = Path.Combine(_tmpDir, filename);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("invoice.pdf");
        using var stream = entry.Open();
        stream.Write(Encoding.ASCII.GetBytes("%PDF-1.4 test"));
        return path;
    }

    private JsonElement CapturedBody()
    {
        var entry = _server.LogEntries.Single(e => e.RequestMessage?.Path == "/emails");
        return JsonDocument.Parse(entry.RequestMessage!.Body!).RootElement;
    }

    [Fact]
    public async Task SendAsync_posts_to_emails_endpoint()
    {
        var mail = new MailWithAttachment("Facturas 2026-Q2", "Adjunto facturas.",
            "facturas_2026-Q2.zip", CreateZipAttachment());

        await _sut.SendAsync(mail, CancellationToken.None);

        Assert.Single(_server.LogEntries, e => e.RequestMessage?.Path == "/emails");
    }

    [Fact]
    public async Task SendAsync_includes_bearer_token_in_authorization_header()
    {
        var mail = new MailWithAttachment("Asunto", "Cuerpo.",
            "facturas.zip", CreateZipAttachment("facturas.zip"));

        await _sut.SendAsync(mail, CancellationToken.None);

        var entry = _server.LogEntries.Single(e => e.RequestMessage?.Path == "/emails");
        var authHeader = entry.RequestMessage!.Headers!["Authorization"].First();
        Assert.Equal("Bearer re_test_key", authHeader);
    }

    [Fact]
    public async Task SendAsync_formats_from_field_with_display_name()
    {
        var mail = new MailWithAttachment("S", "B", "f.zip", CreateZipAttachment("f.zip"));

        await _sut.SendAsync(mail, CancellationToken.None);

        var from = CapturedBody().GetProperty("from").GetString();
        // RFC 5322: "Display Name" <address>
        Assert.Equal("\"Test Sender\" <sender@test.com>", from);
    }

    [Fact]
    public async Task SendAsync_sends_to_advisor_address()
    {
        var mail = new MailWithAttachment("S", "B", "f.zip", CreateZipAttachment("f.zip"));

        await _sut.SendAsync(mail, CancellationToken.None);

        var to = CapturedBody().GetProperty("to")[0].GetString();
        Assert.Equal("advisor@test.com", to);
    }

    [Fact]
    public async Task SendAsync_sets_subject_and_text_body()
    {
        var mail = new MailWithAttachment("Facturas 2026-Q2", "Adjunto las facturas del trimestre.",
            "facturas_2026-Q2.zip", CreateZipAttachment());

        await _sut.SendAsync(mail, CancellationToken.None);

        var body = CapturedBody();
        Assert.Equal("Facturas 2026-Q2", body.GetProperty("subject").GetString());
        Assert.Contains("Adjunto las facturas", body.GetProperty("text").GetString());
    }

    [Fact]
    public async Task SendAsync_includes_html_with_subject_and_attachment_name()
    {
        var mail = new MailWithAttachment("Facturas 2026-Q2", "Cuerpo.",
            "facturas_2026-Q2.zip", CreateZipAttachment());

        await _sut.SendAsync(mail, CancellationToken.None);

        var html = CapturedBody().GetProperty("html").GetString()!;
        Assert.Contains("Facturas 2026-Q2", html);
        Assert.Contains("facturas_2026-Q2.zip", html);
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart());
    }

    [Fact]
    public async Task SendAsync_attaches_zip_as_valid_base64()
    {
        var zipPath = CreateZipAttachment();
        var originalBytes = await File.ReadAllBytesAsync(zipPath);
        var mail = new MailWithAttachment("S", "B", "facturas_2026-Q2.zip", zipPath);

        await _sut.SendAsync(mail, CancellationToken.None);

        var body = CapturedBody();
        var attachment = body.GetProperty("attachments")[0];
        Assert.Equal("facturas_2026-Q2.zip", attachment.GetProperty("filename").GetString());

        var content = attachment.GetProperty("content").GetString()!;
        var decoded = Convert.FromBase64String(content);
        Assert.Equal(originalBytes, decoded);
    }

    [Fact]
    public async Task SendAsync_includes_cc_when_CcAddress_is_set()
    {
        // Given
        var options = Options.Create(new ResendOptions
        {
            ApiBaseUrl     = _server.Url!,
            ApiKey         = TestApiKey,
            FromName       = TestFromName,
            FromAddress    = TestFromAddress,
            AdvisorAddress = TestAdvisorAddress,
            CcAddress      = "myself@example.com",
        });
        var sut = new ResendAdvisorMailSender(new HttpClient(), options, NullLogger<ResendAdvisorMailSender>.Instance);
        var mail = new MailWithAttachment("S", "B", "f.zip", CreateZipAttachment("f-cc.zip"));

        // When
        await sut.SendAsync(mail, CancellationToken.None);

        // Then
        var cc = CapturedBody().GetProperty("cc")[0].GetString();
        Assert.Equal("myself@example.com", cc);
    }

    [Fact]
    public async Task SendAsync_omits_cc_when_CcAddress_is_not_set()
    {
        // Given
        var mail = new MailWithAttachment("S", "B", "f.zip", CreateZipAttachment("f-no-cc.zip"));

        // When
        await _sut.SendAsync(mail, CancellationToken.None);

        // Then
        Assert.False(CapturedBody().TryGetProperty("cc", out _));
    }
}
