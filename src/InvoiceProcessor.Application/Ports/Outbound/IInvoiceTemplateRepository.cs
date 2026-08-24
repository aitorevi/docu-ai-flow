using InvoiceProcessor.Application.Invoices;

namespace InvoiceProcessor.Application.Ports.Outbound;

public interface IInvoiceTemplateRepository
{
    IReadOnlyList<InvoiceTemplate> GetAll();
}
