namespace InvoiceProcessor.Domain.Invoices;

// Trust scoring per supplier (keyed by canonical TaxId). A supplier becomes trusted
// only after `threshold` consecutive confirmations with no human correction; any
// correction resets the count and revokes trust.
public sealed record SupplierTrust(string TaxId, int ConsecutiveUnmodifiedCount, bool IsTrusted)
{
    public const int DefaultThreshold = 20;

    public static SupplierTrust New(string taxId) => new(taxId, 0, false);

    public SupplierTrust RegisterUnmodifiedConfirmation(int threshold = DefaultThreshold)
    {
        var count = ConsecutiveUnmodifiedCount + 1;
        return this with { ConsecutiveUnmodifiedCount = count, IsTrusted = count >= threshold };
    }

    public SupplierTrust RegisterModifiedConfirmation() =>
        this with { ConsecutiveUnmodifiedCount = 0, IsTrusted = false };
}
