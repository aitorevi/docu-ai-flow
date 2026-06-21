using InvoiceProcessor.Infrastructure.Suppliers;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Application.Tests.Invoices;

public sealed class CatalogSupplierNormalizerTests
{
    private static CatalogSupplierNormalizer BuildNormalizer()
    {
        var options = new SupplierCatalogOptions
        {
            Suppliers =
            [
                new SupplierEntry("Repsol", "A78374725", ["REPSOL S.A.", "Repsol Comercializadora"]),
                new SupplierEntry("Endesa", "A81948077", ["Endesa Energía", "ENDESA ENERGIA S.A.U."]),
                new SupplierEntry("TaxWins", "B12345678", ["OtherName"])
            ]
        };
        return new CatalogSupplierNormalizer(Options.Create(options));
    }

    [Fact]
    public void Normalize_WhenExactCifMatch_ReturnsCanonicalSupplier()
    {
        // Given
        var sut = BuildNormalizer();

        // When
        var result = sut.Normalize("whatever name", "A78374725");

        // Then
        Assert.Equal("Repsol", result.Name);
        Assert.Equal("A78374725", result.TaxId);
    }

    [Fact]
    public void Normalize_WhenCifHasSpacesAndDashes_StillMatchesByNormalizedCif()
    {
        // Given
        var sut = BuildNormalizer();

        // When
        var result = sut.Normalize(null, "A-783.747.25");

        // Then
        Assert.Equal("Repsol", result.Name);
    }

    [Fact]
    public void Normalize_WhenAliasMatch_ReturnsCanonicalSupplier()
    {
        // Given
        var sut = BuildNormalizer();

        // When
        var result = sut.Normalize("Repsol Comercializadora", null);

        // Then
        Assert.Equal("Repsol", result.Name);
    }

    [Fact]
    public void Normalize_WhenNameHasAccentsAndCorporateSuffix_ReturnsCanonicalSupplier()
    {
        // Given
        var sut = BuildNormalizer();

        // When
        var result = sut.Normalize("ENDESA ENERGIA S.A.U.", null);

        // Then
        Assert.Equal("Endesa", result.Name);
    }

    [Fact]
    public void Normalize_WhenCifConflictsWithName_CifWins()
    {
        // Given - TaxWins entry has TaxId B12345678, name "TaxWins"
        //         Repsol entry has alias "OtherName" — but CIF points to TaxWins
        var sut = BuildNormalizer();

        // When: rawName would match Repsol alias but CIF matches TaxWins
        var result = sut.Normalize("REPSOL S.A.", "B12345678");

        // Then: CIF wins
        Assert.Equal("TaxWins", result.Name);
    }

    [Fact]
    public void Normalize_WhenNotInCatalog_PreservesRawNameAndTaxId()
    {
        // Given
        var sut = BuildNormalizer();

        // When
        var result = sut.Normalize("Acme Corp", "Z99999999");

        // Then
        Assert.Equal("Acme Corp", result.Name);
        Assert.Equal("Z99999999", result.TaxId);
    }
}
