using InvoiceProcessor.Domain.Invoices;

namespace InvoiceProcessor.Domain.Tests.Invoices;

public sealed class SupplierTrustTests
{
    private const string TaxId = "A46466116";

    [Fact]
    public void New_StartsUntrustedWithZeroCount()
    {
        // Given / When
        var trust = SupplierTrust.New(TaxId);

        // Then
        Assert.Equal(TaxId, trust.TaxId);
        Assert.Equal(0, trust.ConsecutiveUnmodifiedCount);
        Assert.False(trust.IsTrusted);
    }

    [Fact]
    public void RegisterUnmodifiedConfirmation_BelowThreshold_IncrementsButStaysUntrusted()
    {
        // Given
        var trust = SupplierTrust.New(TaxId);

        // When
        var updated = trust.RegisterUnmodifiedConfirmation(threshold: 20);

        // Then
        Assert.Equal(1, updated.ConsecutiveUnmodifiedCount);
        Assert.False(updated.IsTrusted);
    }

    [Fact]
    public void RegisterUnmodifiedConfirmation_ReachingThreshold_BecomesTrusted()
    {
        // Given
        var trust = SupplierTrust.New(TaxId);

        // When: 20 consecutive unmodified confirmations
        for (var i = 0; i < 20; i++)
            trust = trust.RegisterUnmodifiedConfirmation(threshold: 20);

        // Then
        Assert.Equal(20, trust.ConsecutiveUnmodifiedCount);
        Assert.True(trust.IsTrusted);
    }

    [Fact]
    public void RegisterUnmodifiedConfirmation_OneBeforeThreshold_IsNotYetTrusted()
    {
        // Given
        var trust = SupplierTrust.New(TaxId);

        // When: 19 confirmations
        for (var i = 0; i < 19; i++)
            trust = trust.RegisterUnmodifiedConfirmation(threshold: 20);

        // Then
        Assert.Equal(19, trust.ConsecutiveUnmodifiedCount);
        Assert.False(trust.IsTrusted);
    }

    [Fact]
    public void RegisterModifiedConfirmation_ResetsCountAndTrust()
    {
        // Given: a supplier already trusted with a full count
        var trust = SupplierTrust.New(TaxId);
        for (var i = 0; i < 20; i++)
            trust = trust.RegisterUnmodifiedConfirmation(threshold: 20);
        Assert.True(trust.IsTrusted);

        // When: a human correction happens
        var reset = trust.RegisterModifiedConfirmation();

        // Then
        Assert.Equal(0, reset.ConsecutiveUnmodifiedCount);
        Assert.False(reset.IsTrusted);
        Assert.Equal(TaxId, reset.TaxId);
    }
}
