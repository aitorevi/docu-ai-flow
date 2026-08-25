namespace InvoiceProcessor.Infrastructure.Files;

public sealed class FolderOptions
{
    // Where the data tree lives. Empty means "work it out" — see DataRoot.
    public string? Root { get; set; }

    // Relative by default; rewritten to absolute paths against the data root at startup, so
    // they mean the same folder however the app was launched.
    public string Inbox { get; set; } = "./data/inbox";
    public string Archive { get; set; } = "./data/archive";
    public string Failed { get; set; } = "./data/failed";
    public string Pending { get; set; } = "./data/pending";
    public string Duplicates { get; set; } = "./data/duplicates";
    public string Output { get; set; } = "./data/output";
    public int MaxConcurrency { get; init; } = 3;
    public int PollSeconds { get; init; } = 5;
}
