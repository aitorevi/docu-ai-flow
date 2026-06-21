namespace InvoiceProcessor.Infrastructure.Mail;

public sealed class ResendOptions
{
    public string ApiBaseUrl { get; init; } = "https://api.resend.com";
    public string ApiKey { get; init; } = string.Empty;
    public string FromName { get; init; } = string.Empty;
    public string FromAddress { get; init; } = string.Empty;
    public string AdvisorAddress { get; init; } = string.Empty;
    public string? CcAddress { get; init; }
    public int MaxAttachmentMb { get; init; } = 38;
}
