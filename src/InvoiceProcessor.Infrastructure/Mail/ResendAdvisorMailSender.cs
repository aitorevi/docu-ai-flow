using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Ports.Outbound;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace InvoiceProcessor.Infrastructure.Mail;

public sealed class ResendAdvisorMailSender(
    HttpClient httpClient,
    IOptions<ResendOptions> opts,
    ILogger<ResendAdvisorMailSender> logger) : IAdvisorMailSender
{
    private readonly Uri _emailsUri = new($"{opts.Value.ApiBaseUrl}/emails");

    public async Task SendAsync(MailWithAttachment mail, CancellationToken ct)
    {
        var options = opts.Value;
        logger.LogDebug("Sending from: {From} → to: {To}", options.FromAddress, options.AdvisorAddress);
        var attachmentBytes = await File.ReadAllBytesAsync(mail.AttachmentPath, ct);
        var attachmentBase64 = Convert.ToBase64String(attachmentBytes);

        var from = string.IsNullOrWhiteSpace(options.FromName)
            ? options.FromAddress
            : $"\"{options.FromName.Replace("\"", "\\\"")}\" <{options.FromAddress}>";

        var payload = new
        {
            from,
            to = new[] { options.AdvisorAddress },
            cc = string.IsNullOrWhiteSpace(options.CcAddress) ? null : new[] { options.CcAddress },
            subject = mail.Subject,
            text = mail.Body,
            html = BuildHtml(mail, options),
            attachments = new[]
            {
                new
                {
                    filename = mail.AttachmentName,
                    content = attachmentBase64
                }
            }
        };

        var json = JsonSerializer.Serialize(payload,
            new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
        using var request = new HttpRequestMessage(HttpMethod.Post, _emailsUri);
        request.Headers.Add("Authorization", $"Bearer {options.ApiKey}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Resend {(int)response.StatusCode}: {body}", null, response.StatusCode);
        }
    }

    private static string BuildHtml(MailWithAttachment mail, ResendOptions options)
    {
        var senderName = string.IsNullOrWhiteSpace(options.FromName) ? options.FromAddress : options.FromName;
        var year = DateTimeOffset.UtcNow.Year;

        return $"""
            <!DOCTYPE html>
            <html lang="es">
            <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f4f4f5;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f4f5;padding:32px 0;">
                <tr><td align="center">
                  <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,.08);">

                    <!-- Header -->
                    <tr>
                      <td style="background:#1e40af;padding:28px 40px;">
                        <p style="margin:0;color:#ffffff;font-size:20px;font-weight:600;">{mail.Subject}</p>
                      </td>
                    </tr>

                    <!-- Body -->
                    <tr>
                      <td style="padding:36px 40px 24px;">
                        <p style="margin:0 0 16px;color:#374151;font-size:15px;line-height:1.6;">{mail.Body}</p>
                        <table cellpadding="0" cellspacing="0" style="width:100%;">
                          <tr>
                            <td align="center">
                              <table cellpadding="0" cellspacing="0">
                                <tr>
                                  <td style="background:#1e40af;border-radius:6px;padding:14px 28px;">
                                    <p style="margin:0;color:#ffffff;font-size:14px;font-weight:600;letter-spacing:.01em;">
                                      ⬇&#xFE0E;&nbsp; {mail.AttachmentName}
                                    </p>
                                  </td>
                                </tr>
                              </table>
                              <p style="margin:10px 0 0;color:#9ca3af;font-size:11px;">El archivo ZIP está adjunto a este correo</p>
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                      <td style="padding:0 40px 32px;">
                        <p style="margin:24px 0 0;border-top:1px solid #f3f4f6;padding-top:20px;color:#9ca3af;font-size:12px;">
                          Enviado automáticamente por {senderName}
                        </p>
                      </td>
                    </tr>

                  </table>
                  <p style="color:#9ca3af;font-size:11px;margin:16px 0 0;">&copy; {year} {senderName}</p>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }
}
