using InvoiceProcessor.Infrastructure.Files;

namespace InvoiceProcessor.Integration.Tests.Files;

// "./data/inbox" must mean the same folder no matter how the app was launched. These tests pin
// the resolution order, because getting it wrong is silent: invoices pile up in a tree nobody
// is watching and nothing ever errors.
public sealed class DataRootTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "dataroot-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true);
    }

    [Fact]
    public void Resolve_WithConfiguredRoot_UsesItAndIgnoresEverythingElse()
    {
        var repo = Path.Combine(_tmp, "repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var configured = Path.Combine(_tmp, "elsewhere");

        Assert.Equal(Path.GetFullPath(configured), DataRoot.Resolve(configured, repo));
    }

    [Fact]
    public void Resolve_WithoutConfiguredRoot_WalksUpToTheRepoRoot()
    {
        var repo = Path.Combine(_tmp, "repo");
        var deep = Path.Combine(repo, "src", "Worker", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(deep);

        Assert.Equal(repo, DataRoot.Resolve(null, deep));
    }

    [Fact]
    public void Resolve_WithNoMarkerAnywhere_FallsBackToTheStartDirectory()
    {
        // A published or containerised build: no .git above it.
        var appDir = Path.Combine(_tmp, "app");
        Directory.CreateDirectory(appDir);

        Assert.Equal(appDir, DataRoot.Resolve(null, appDir));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_TreatsABlankConfiguredRootAsAbsent(string blank)
    {
        var repo = Path.Combine(_tmp, "repo");
        var deep = Path.Combine(repo, "src", "Worker");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(deep);

        Assert.Equal(repo, DataRoot.Resolve(blank, deep));
    }
}
