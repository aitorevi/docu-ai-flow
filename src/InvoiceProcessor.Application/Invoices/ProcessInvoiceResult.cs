using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Application.Invoices;

public sealed record ProcessInvoiceResult(
    bool Success,
    InvoiceId? InvoiceId,
    string? FailureReason);
