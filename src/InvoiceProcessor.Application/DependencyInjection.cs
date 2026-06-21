using InvoiceProcessor.Application.Dispatch;
using InvoiceProcessor.Application.Export;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using Microsoft.Extensions.DependencyInjection;

namespace InvoiceProcessor.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<MailDispatchSettings>();
        services.AddScoped<IProcessInvoiceUseCase, ProcessInvoiceService>();
        services.AddScoped<IExportQuarterToSpreadsheetUseCase, ExportQuarterToSpreadsheetService>();
        services.AddScoped<ISendQuarterToAdvisorUseCase, SendQuarterToAdvisorService>();
        return services;
    }
}
