namespace InvoiceProcessor.Application.Dispatch;

public sealed record MailWithAttachment(string Subject, string Body, string AttachmentName, string AttachmentPath);
