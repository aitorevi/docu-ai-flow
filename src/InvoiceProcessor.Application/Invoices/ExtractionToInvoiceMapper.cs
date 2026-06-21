using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain;
using InvoiceProcessor.Domain.Invoices;
using SharpMonads.Core;

namespace InvoiceProcessor.Application.Invoices;

public static class ExtractionToInvoiceMapper
{
    public static Result<Invoice, Error> Map(
        ExtractionResult extraction,
        ISupplierNormalizer supplierNormalizer,
        decimal confidenceThreshold = 0.6m)
    {
        if (extraction.OverallConfidence < confidenceThreshold)
            return Result<Invoice, Error>.Failure(Error.Validation(
                $"Confianza global insuficiente: {extraction.OverallConfidence:F2} < {confidenceThreshold:F2}."));

        var fields = extraction.Fields;

        if (!fields.TryGetValue("invoice_number", out var numberField) ||
            string.IsNullOrWhiteSpace(numberField.Value))
            return Result<Invoice, Error>.Failure(Error.Validation("Número de factura ausente en la extracción."));

        if (!fields.TryGetValue("issue_date", out var dateField) ||
            !DateOnly.TryParse(dateField.Value, out var issueDate))
            return Result<Invoice, Error>.Failure(Error.Validation("Fecha de factura ausente o inválida."));

        var currency = fields.TryGetValue("currency", out var currField) ? currField.Value : null;
        if (string.IsNullOrWhiteSpace(currency))
            return Result<Invoice, Error>.Failure(Error.Validation("Moneda ausente en la extracción."));

        if (!fields.TryGetValue("net_amount", out var netField) ||
            !decimal.TryParse(netField.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var netAmount))
            return Result<Invoice, Error>.Failure(Error.Validation("Base imponible ausente o inválida."));

        if (!fields.TryGetValue("tax_amount", out var taxField) ||
            !decimal.TryParse(taxField.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var taxAmount))
            return Result<Invoice, Error>.Failure(Error.Validation("IVA ausente o inválido."));

        if (!fields.TryGetValue("total_amount", out var totalField) ||
            !decimal.TryParse(totalField.Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var totalAmount))
            return Result<Invoice, Error>.Failure(Error.Validation("Total ausente o inválido."));

        DateOnly? dueDate = null;
        if (fields.TryGetValue("due_date", out var dueDateField) &&
            !string.IsNullOrWhiteSpace(dueDateField.Value) &&
            DateOnly.TryParse(dueDateField.Value, out var parsedDueDate))
            dueDate = parsedDueDate;

        var rawName = fields.TryGetValue("supplier_name", out var nameField) ? nameField.Value : null;
        var rawTaxId = fields.TryGetValue("supplier_tax_id", out var taxIdField) ? taxIdField.Value : null;
        var supplier = supplierNormalizer.Normalize(rawName, rawTaxId);

        return Money.Create(netAmount, currency).Bind(net =>
               Money.Create(taxAmount, currency).Bind(tax =>
               Money.Create(totalAmount, currency).Bind(total =>
               Invoice.Create(InvoiceId.New(), numberField.Value!, supplier, issueDate, dueDate, net, tax, total, []))));
    }
}
