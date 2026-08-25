using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Dispatch;
using InvoiceProcessor.Infrastructure.Export;
using InvoiceProcessor.Infrastructure.Extraction.DocumentAi;
using InvoiceProcessor.Infrastructure.Extraction.Templates;
using InvoiceProcessor.Infrastructure.Files;
using InvoiceProcessor.Infrastructure.Idempotency;
using InvoiceProcessor.Infrastructure.Mail;
using InvoiceProcessor.Infrastructure.Persistence;
using InvoiceProcessor.Infrastructure.Suppliers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FolderOptions>(configuration.GetSection("Folders"));
        services.Configure<GoogleDocumentAiOptions>(configuration.GetSection("GoogleDocumentAi"));
        services.Configure<CompanyOptions>(configuration.GetSection("Company"));
        services.Configure<ExtractionOptions>(configuration.GetSection("Extraction"));
        services.Configure<TemplateExtractorOptions>(configuration.GetSection("TemplateExtractor"));
        services.Configure<SupplierCatalogOptions>(configuration.GetSection("SupplierCatalog"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<ResendOptions>(configuration.GetSection("Resend"));

        // Pin every relative data path to one root, so "./data/inbox" is the same folder whether
        // the app was started with `dotnet run`, from the published binary, or inside a container.
        var dataRoot = DataRoot.Resolve(
            Environment.GetEnvironmentVariable(DataRoot.EnvVariable)
                ?? configuration.GetValue<string>("Folders:Root"),
            AppContext.BaseDirectory);

        services.PostConfigure<FolderOptions>(o =>
        {
            o.Root    = dataRoot;
            o.Inbox   = Path.GetFullPath(o.Inbox, dataRoot);
            o.Archive = Path.GetFullPath(o.Archive, dataRoot);
            o.Failed  = Path.GetFullPath(o.Failed, dataRoot);
            o.Pending = Path.GetFullPath(o.Pending, dataRoot);
            o.Duplicates = Path.GetFullPath(o.Duplicates, dataRoot);
            o.Output  = Path.GetFullPath(o.Output, dataRoot);
        });
        services.PostConfigure<DatabaseOptions>(o => o.Path = Path.GetFullPath(o.Path, dataRoot));

        services.AddSingleton<IInvoiceTemplateRepository, AppsettingsTemplateRepository>();

        services.AddSingleton<IDocumentReader, FileSystemDocumentReader>();
        services.AddSingleton<IDocumentArchiver, FileSystemDocumentArchiver>();
        services.AddSingleton<ISupplierNormalizer, CatalogSupplierNormalizer>();
        services.AddSingleton<IArchivedInvoiceSource, FileSystemArchivedInvoiceSource>();
        services.AddSingleton<IInvoiceArchiveCompressor, ZipInvoiceArchiveCompressor>();

        services.AddSingleton<IProcessedDocumentLog>(sp =>
        {
            // Resolved from the options, not from raw configuration, so it follows the same data
            // root as every other folder — including when a test relocates the whole tree.
            var folders = sp.GetRequiredService<IOptions<FolderOptions>>().Value;
            var logDir = Path.GetDirectoryName(Path.GetFullPath(folders.Inbox)) ?? ".";
            Directory.CreateDirectory(logDir);
            return new JsonFileProcessedDocumentLog(Path.Combine(logDir, "processed.jsonl"));
        });

        services.AddSingleton<SqliteProcessedInvoiceRepository>();
        services.AddSingleton<IProcessedInvoiceRepository>(sp =>
        {
            var repo = sp.GetRequiredService<SqliteProcessedInvoiceRepository>();
            repo.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            return repo;
        });

        services.AddSingleton<SqlitePendingInvoiceRepository>();
        services.AddSingleton<IPendingInvoiceRepository>(sp =>
        {
            var repo = sp.GetRequiredService<SqlitePendingInvoiceRepository>();
            repo.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            return repo;
        });

        services.AddSingleton<SqliteSupplierTrustRepository>();
        services.AddSingleton<ISupplierTrustRepository>(sp =>
        {
            var repo = sp.GetRequiredService<SqliteSupplierTrustRepository>();
            repo.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            return repo;
        });

        services.AddSingleton<SqliteExportedInvoiceLog>();
        services.AddSingleton<IExportedInvoiceLog>(sp =>
        {
            var log = sp.GetRequiredService<SqliteExportedInvoiceLog>();
            log.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            return log;
        });

        services.AddSingleton<SqliteSentInvoiceLog>();
        services.AddSingleton<ISentInvoiceLog>(sp =>
        {
            var log = sp.GetRequiredService<SqliteSentInvoiceLog>();
            log.EnsureCreatedAsync(CancellationToken.None).GetAwaiter().GetResult();
            return log;
        });

        services.AddSingleton<IMasterSpreadsheetWriter, ClosedXmlMasterSpreadsheetWriter>();
        services.AddSingleton<IQuarterSpreadsheetExporter, ClosedXmlQuarterSpreadsheetExporter>();

        return services;
    }
}
