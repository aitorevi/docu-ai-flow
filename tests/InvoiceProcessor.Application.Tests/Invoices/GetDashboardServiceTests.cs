using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace InvoiceProcessor.Application.Tests.Invoices;

public sealed class GetDashboardServiceTests
{
    private readonly IProcessedInvoiceRepository _repository = Substitute.For<IProcessedInvoiceRepository>();
    private readonly IPendingInvoiceRepository _pending = Substitute.For<IPendingInvoiceRepository>();
    private readonly ISupplierTrustRepository _trust = Substitute.For<ISupplierTrustRepository>();

    private readonly IGetDashboardUseCase _sut;

    public GetDashboardServiceTests()
    {
        _sut = new GetDashboardService(
            _repository, _pending, _trust,
            Options.Create(new ExtractionOptions { SupplierTrustThreshold = 3 }));
    }

    private static StoredInvoice AnInvoice(string supplier, string? taxId, decimal total) => new(
        Guid.NewGuid().ToString(), "F-1", supplier, taxId,
        new DateOnly(2026, 1, 15), null, total, 0m, total, "EUR", "/archive/x.pdf");

    private static PendingInvoice APending(string supplier, string? taxId) => new(
        Guid.NewGuid().ToString(), "/pending/x.pdf", "x.pdf", "F-2", supplier, taxId,
        new DateOnly(2026, 1, 15), null, 10m, 0m, 10m, "EUR",
        new Dictionary<string, CapturedField>(), DateTimeOffset.UtcNow);

    private void GivenStored(params StoredInvoice[] invoices) =>
        _repository.ListAllAsync(Arg.Any<CancellationToken>()).Returns(invoices.ToAsyncEnumerable());

    private void GivenPending(params PendingInvoice[] pending) =>
        _pending.ListAllAsync(Arg.Any<CancellationToken>()).Returns(pending.ToAsyncEnumerable());

    private void GivenTrust(params SupplierTrust[] trust) =>
        _trust.ListAllAsync(Arg.Any<CancellationToken>()).Returns(trust);

    [Fact]
    public async Task ExecuteAsync_OnAnEmptySystem_ReportsZeroesAndNoSuppliers()
    {
        GivenStored();
        GivenPending();
        GivenTrust();

        var result = await _sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(0, result.PendingReview);
        Assert.Equal(0, result.ArchivedInvoices);
        Assert.Equal(0m, result.TotalAmount);
        Assert.Equal(3, result.TrustThreshold);
        Assert.Empty(result.Suppliers);
    }

    [Fact]
    public async Task ExecuteAsync_SumsTheHeadlineNumbers()
    {
        GivenStored(AnInvoice("Aurora", "B1", 100m), AnInvoice("Aurora", "B1", 21.50m));
        GivenPending(APending("Boreal", "A2"));
        GivenTrust();

        var result = await _sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(2, result.ArchivedInvoices);
        Assert.Equal(121.50m, result.TotalAmount);
        Assert.Equal(1, result.PendingReview);
    }

    [Fact]
    public async Task ExecuteAsync_MergesArchivedPendingAndTrustIntoOneRowPerSupplier()
    {
        // Aurora has both filed and waiting invoices and a trust record; Boreal is only waiting.
        GivenStored(AnInvoice("Aurora", "B1", 100m), AnInvoice("Aurora", "B1", 50m));
        GivenPending(APending("Aurora", "B1"), APending("Boreal", "A2"));
        GivenTrust(new SupplierTrust("B1", 2, IsTrusted: false));

        var result = await _sut.ExecuteAsync(CancellationToken.None);

        var aurora = result.Suppliers.Single(s => s.TaxId == "B1");
        Assert.Equal("Aurora", aurora.Name);
        Assert.Equal(2, aurora.ArchivedCount);
        Assert.Equal(1, aurora.PendingCount);
        Assert.Equal(2, aurora.UnmodifiedCount);
        Assert.False(aurora.IsTrusted);

        var boreal = result.Suppliers.Single(s => s.TaxId == "A2");
        Assert.Equal(0, boreal.ArchivedCount);
        Assert.Equal(1, boreal.PendingCount);
        Assert.Equal(0, boreal.UnmodifiedCount);
    }

    [Fact]
    public async Task ExecuteAsync_OrdersSuppliersByWhatStillNeedsAttention()
    {
        // Whoever is costing the most review time goes first; a trusted supplier that needs
        // nothing sinks to the bottom. A dashboard should lead with the work, not the alphabet.
        GivenStored(AnInvoice("Tranquila", "T1", 10m));
        GivenPending(
            APending("Ruidosa", "R1"), APending("Ruidosa", "R1"), APending("Ruidosa", "R1"),
            APending("Media", "M1"));
        GivenTrust(new SupplierTrust("T1", 3, IsTrusted: true));

        var result = await _sut.ExecuteAsync(CancellationToken.None);

        Assert.Equal(["R1", "M1", "T1"], result.Suppliers.Select(s => s.TaxId));
    }

    [Fact]
    public async Task ExecuteAsync_KeepsSuppliersWithoutATaxIdApart()
    {
        // Manual-entry documents have no supplier yet. They must not all collapse into one row.
        GivenStored();
        GivenPending(APending(string.Empty, null), APending(string.Empty, null));
        GivenTrust();

        var result = await _sut.ExecuteAsync(CancellationToken.None);

        var unidentified = Assert.Single(result.Suppliers);
        Assert.Null(unidentified.TaxId);
        Assert.Equal(2, unidentified.PendingCount);
    }
}
