namespace InvoiceProcessor.Domain;

public sealed record Error(string Code, string Message)
{
    public static Error Validation(string message) => new("validation", message);
}
