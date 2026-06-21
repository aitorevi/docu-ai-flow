using InvoiceProcessor.Application.Dispatch;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IAdvisorMailSender
{
    Task SendAsync(MailWithAttachment mail, CancellationToken ct);
}
