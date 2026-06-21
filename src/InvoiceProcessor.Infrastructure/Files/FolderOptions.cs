namespace InvoiceProcessor.Infrastructure.Files;

public sealed class FolderOptions
{
    public string Inbox { get; init; } = "./data/inbox";
    public string Archive { get; init; } = "./data/archive";
    public string Failed { get; init; } = "./data/failed";
    public string Output { get; init; } = "./data/output";
    public int MaxConcurrency { get; init; } = 3;
    public int PollSeconds { get; init; } = 5;
}
