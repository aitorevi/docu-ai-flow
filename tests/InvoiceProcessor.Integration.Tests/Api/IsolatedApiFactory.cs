using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Files;
using InvoiceProcessor.Infrastructure.Persistence;
using InvoiceProcessor.Integration.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InvoiceProcessor.Integration.Tests.Api;

// Hosts the real Worker API in-process against a throwaway data root, with a fake extractor so no
// PDF parsing or cloud call is involved. The endpoints under test are the ones that actually ship.
public sealed class IsolatedApiFactory : WebApplicationFactory<Program>
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"api-{Guid.NewGuid():N}");

    public FakeInvoiceDataExtractor Extractor { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Keeps Program.cs's auto-open-browser hook from firing for an in-process host.
        builder.UseEnvironment("Testing");
        Directory.CreateDirectory(Root);

        builder.ConfigureServices(services =>
        {
            // PostConfigure, not configuration: it wins over Program's own registrations no matter
            // when WebApplicationFactory applies configuration relative to Build(), so every byte
            // this host writes stays under Root.
            services.PostConfigure<DatabaseOptions>(o => o.Path = Path.Combine(Root, "invoices.db"));
            services.PostConfigure<FolderOptions>(o =>
            {
                o.Root       = Root;
                o.Inbox      = Path.Combine(Root, "inbox");
                o.Archive    = Path.Combine(Root, "archive");
                o.Failed     = Path.Combine(Root, "failed");
                o.Output     = Path.Combine(Root, "output");
                o.Pending    = Path.Combine(Root, "pending");
                o.Duplicates = Path.Combine(Root, "duplicates");
            });
            // A small threshold so a test can walk a supplier to trusted without 20 round trips.
            services.PostConfigure<ExtractionOptions>(o =>
            {
                o.Provider = ExtractionProvider.Template;
                o.SupplierTrustThreshold = 3;
            });

            services.RemoveAll<IInvoiceDataExtractor>();
            services.AddSingleton<IInvoiceDataExtractor>(Extractor);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
