using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Infrastructure.Dispatch;
using InvoiceProcessor.Infrastructure.Export;
using InvoiceProcessor.Infrastructure.Extraction.DocumentAi;
using InvoiceProcessor.Infrastructure.Extraction.LlamaParse;
using InvoiceProcessor.Infrastructure.Files;
using InvoiceProcessor.Infrastructure.Idempotency;
using InvoiceProcessor.Infrastructure.Mail;
using InvoiceProcessor.Infrastructure.Persistence;
using InvoiceProcessor.Infrastructure.Suppliers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceProcessor.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FolderOptions>(configuration.GetSection("Folders"));
        services.Configure<LlamaParseOptions>(configuration.GetSection("LlamaParse"));
        services.Configure<GoogleDocumentAiOptions>(configuration.GetSection("GoogleDocumentAi"));
        services.Configure<CompanyOptions>(configuration.GetSection("Company"));
        services.Configure<ExtractionOptions>(configuration.GetSection("Extraction"));
        services.Configure<SupplierCatalogOptions>(configuration.GetSection("SupplierCatalog"));
        services.Configure<DatabaseOptions>(configuration.GetSection("Database"));
        services.Configure<ResendOptions>(configuration.GetSection("Resend"));

        services.AddSingleton<IDocumentReader, FileSystemDocumentReader>();
        services.AddSingleton<IDocumentArchiver, FileSystemDocumentArchiver>();
        services.AddSingleton<ISupplierNormalizer, CatalogSupplierNormalizer>();
        services.AddSingleton<IArchivedInvoiceSource, FileSystemArchivedInvoiceSource>();
        services.AddSingleton<IInvoiceArchiveCompressor, ZipInvoiceArchiveCompressor>();

        services.AddSingleton<IProcessedDocumentLog>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var dataPath = config.GetValue<string>("Folders:Inbox") ?? "./data";
            var logDir = Path.GetDirectoryName(Path.GetFullPath(dataPath)) ?? ".";
            var logPath = Path.Combine(logDir, "processed.jsonl");
            return new JsonFileProcessedDocumentLog(logPath);
        });

        services.AddSingleton<SqliteProcessedInvoiceRepository>();
        services.AddSingleton<IProcessedInvoiceRepository>(sp =>
        {
            var repo = sp.GetRequiredService<SqliteProcessedInvoiceRepository>();
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
