using System.Globalization;
using System.Text;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Suppliers;

public sealed class CatalogSupplierNormalizer(IOptions<SupplierCatalogOptions> opts) : ISupplierNormalizer
{
    public Supplier Normalize(string? rawName, string? rawTaxId)
    {
        var catalog = opts.Value.Suppliers;

        // 1) Match por CIF/NIF: el identificador estable entre facturas.
        if (!string.IsNullOrWhiteSpace(rawTaxId))
        {
            var byTax = catalog.FirstOrDefault(s => SameTaxId(s.TaxId, rawTaxId));
            if (byTax is not null) return new Supplier(byTax.CanonicalName, byTax.TaxId, null);
        }

        // 2) Respaldo: nombre normalizado contra el nombre canónico o sus alias.
        var key = Canonicalize(rawName);
        if (!string.IsNullOrEmpty(key))
        {
            var byName = catalog.FirstOrDefault(s =>
                Canonicalize(s.CanonicalName) == key || s.Aliases.Any(a => Canonicalize(a) == key));
            if (byName is not null) return new Supplier(byName.CanonicalName, byName.TaxId ?? rawTaxId, null);
        }

        // 3) Desconocido: conserva lo extraído (limpio) para añadirlo luego al catálogo.
        return new Supplier(string.IsNullOrWhiteSpace(rawName) ? "Desconocido" : rawName.Trim(), rawTaxId, null);
    }

    // CIF/NIF sin espacios, guiones ni puntos, en mayúsculas.
    private static bool SameTaxId(string? a, string? b)
    {
        static string N(string? s) => new(s?.Where(char.IsLetterOrDigit).ToArray() ?? []);
        var na = N(a); var nb = N(b);
        return na.Length > 0 && na.Equals(nb, StringComparison.OrdinalIgnoreCase);
    }

    // Mayúsculas, sin acentos, sin sufijos societarios, espacios colapsados.
    private static string Canonicalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var stripped = string.Concat(name.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

        var upper = stripped.ToUpperInvariant();
        foreach (var suffix in new[] { "S.A.U.", "S.L.U.", "S.A.", "S.L.", "SAU", "SLU", "SA", "SL" })
            upper = upper.Replace(suffix, " ");

        return string.Join(' ', upper.Split(new[] { ',', ' ', '.', '-' }, StringSplitOptions.RemoveEmptyEntries));
    }
}
