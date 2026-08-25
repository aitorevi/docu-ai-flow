namespace InvoiceProcessor.Infrastructure.Files;

// Single source of truth for where the data folders live, independent of the current working
// directory. Without this, "./data/inbox" means a different folder depending on whether you
// ran `dotnet run --project ...`, launched the built binary, or double-clicked a script — and
// invoices quietly pile up in a tree nobody is watching.
//
// Resolution order:
//   1. an explicit configured root (env DOCU_AI_FLOW_DATA, or config Folders:Root)
//   2. the nearest ancestor directory containing a .git marker (the repo root, in development)
//   3. the start directory as a last resort (a published or containerised build)
public static class DataRoot
{
    public const string EnvVariable = "DOCU_AI_FLOW_DATA";

    public static string Resolve(string? configuredRoot, string startDir)
    {
        if (!string.IsNullOrWhiteSpace(configuredRoot))
            return Path.GetFullPath(configuredRoot);

        for (var dir = startDir; dir is not null; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(Path.Combine(dir, ".git")))
                return dir;

        return startDir;
    }
}
