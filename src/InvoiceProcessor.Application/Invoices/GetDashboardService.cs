using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Application.Invoices;

// Builds the panel's view of the system: the headline numbers, and one row per supplier joining
// what is filed, what is waiting, and how far its template is from being trusted.
public sealed class GetDashboardService(
    IProcessedInvoiceRepository repository,
    IPendingInvoiceRepository pendingRepository,
    ISupplierTrustRepository trustRepository,
    IOptions<ExtractionOptions> extractionOptions) : IGetDashboardUseCase
{
    public async Task<DashboardSummary> ExecuteAsync(CancellationToken ct)
    {
        // Keyed by tax id; documents with no supplier yet (manual entry) share the null key so
        // they show as a single "unidentified" row rather than one row each.
        var rows = new Dictionary<string, Row>();
        var archivedCount = 0;
        var total = 0m;
        var currency = "EUR";

        await foreach (var invoice in repository.ListAllAsync(ct))
        {
            archivedCount++;
            total += invoice.TotalAmount;
            currency = invoice.Currency;
            Get(rows, invoice.SupplierName, invoice.SupplierTaxId).Archived++;
        }

        var pendingCount = 0;
        await foreach (var pending in pendingRepository.ListAllAsync(ct))
        {
            pendingCount++;
            Get(rows, pending.SupplierName, pending.SupplierTaxId).Pending++;
        }

        foreach (var trust in await trustRepository.ListAllAsync(ct))
        {
            // A trust record for a supplier with nothing on file is possible (everything it sent
            // was later deleted); it still belongs on the list, so create the row if missing.
            var row = Get(rows, string.Empty, trust.TaxId);
            row.Unmodified = trust.ConsecutiveUnmodifiedCount;
            row.IsTrusted = trust.IsTrusted;
        }

        var suppliers = rows.Values
            // Lead with whoever is costing review time; a supplier that needs nothing sinks.
            .OrderByDescending(r => r.Pending)
            .ThenByDescending(r => r.Archived)
            .ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(r => new SupplierSummary(
                Name: string.IsNullOrWhiteSpace(r.Name) ? "Sin identificar" : r.Name,
                TaxId: r.TaxId,
                ArchivedCount: r.Archived,
                PendingCount: r.Pending,
                UnmodifiedCount: r.Unmodified,
                IsTrusted: r.IsTrusted))
            .ToArray();

        return new DashboardSummary(
            PendingReview: pendingCount,
            ArchivedInvoices: archivedCount,
            TotalAmount: total,
            Currency: currency,
            TrustThreshold: extractionOptions.Value.SupplierTrustThreshold,
            Suppliers: suppliers);
    }

    private static Row Get(Dictionary<string, Row> rows, string name, string? taxId)
    {
        var key = taxId ?? string.Empty;
        if (!rows.TryGetValue(key, out var row))
            rows[key] = row = new Row { Name = name, TaxId = taxId };

        // The first non-empty name wins: a trust record carries no name, and a manual-entry
        // pending invoice carries an empty one.
        if (string.IsNullOrWhiteSpace(row.Name) && !string.IsNullOrWhiteSpace(name))
            row.Name = name;

        return row;
    }

    private sealed class Row
    {
        public string Name = string.Empty;
        public string? TaxId;
        public int Archived;
        public int Pending;
        public int Unmodified;
        public bool IsTrusted;
    }
}
