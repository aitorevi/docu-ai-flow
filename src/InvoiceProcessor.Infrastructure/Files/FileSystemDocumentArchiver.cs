using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Documents;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Files;

public sealed class FileSystemDocumentArchiver(IOptions<FolderOptions> folders) : IDocumentArchiver
{
    private readonly string _archive = Path.GetFullPath(folders.Value.Archive);
    private readonly string _failed  = Path.GetFullPath(folders.Value.Failed);

    // Procesado → {Archive}/2026/01/Repsol/Repsol-F2026-0042.pdf (año y mes = fecha de EMISIÓN)
    public Task<string> ArchiveProcessedAsync(IncomingDocument doc, Invoice invoice, CancellationToken ct)
    {
        var supplier = CanonicalizeSupplierName(invoice.Supplier.Name);

        var dir = Path.Combine(
            _archive,
            invoice.IssueDate.Year.ToString("D4"),
            invoice.IssueDate.Month.ToString("D2"),
            supplier);
        Directory.CreateDirectory(dir);

        var number = Sanitize(invoice.InvoiceNumber, fallback: "sin-numero").ToLowerInvariant();
        var ext = Path.GetExtension(doc.FileName);
        var fileName = $"{supplier}-{number}{ext}";

        return Task.FromResult(MoveResolvingCollision(doc.Location, Path.Combine(dir, fileName)));
    }

    // Fallo → {Failed}/20260113-101500-factura.pdf (prefijo de fecha para no colisionar)
    public Task<string> ArchiveFailedAsync(IncomingDocument doc, CancellationToken ct)
    {
        Directory.CreateDirectory(_failed);
        var target = Path.Combine(_failed, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{doc.FileName}");
        return Task.FromResult(MoveResolvingCollision(doc.Location, target));
    }

    // Strips trailing legal suffixes only at the end of the name to avoid false positives
    // (e.g. "JOSE CALATAYUD SANCHO" must not become "JOSE CALATAYUD NCHO").
    private static readonly Regex TrailingSuffix = new(
        @"[\s,]+(S\.A\.U\.?|S\.L\.U\.?|S\.L\.P\.?|S\.A\.?|S\.L\.?|SAU|SLU|SLP|SA|SL)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string CanonicalizeSupplierName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "_SinProveedor";

        var noAccents = string.Concat(
            name.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark));

        var stripped = TrailingSuffix.Replace(noAccents, string.Empty);

        var clean = string.Concat(stripped.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        clean = string.Join('-', clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', '-');

        return string.IsNullOrEmpty(clean) ? "_sin-proveedor" : clean.ToLowerInvariant();
    }

    // Convierte "Repsol, S.A." o "F/2026 0042" en un componente de ruta válido.
    private static string Sanitize(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var clean = string.Concat(value.Where(c => !Path.GetInvalidFileNameChars().Contains(c)))
                          .Trim()
                          .TrimEnd('.', ' ');
        return string.IsNullOrEmpty(clean) ? fallback : clean;
    }

    // Si ya existe Repsol-F2026-0042.pdf, genera "Repsol-F2026-0042 (2).pdf"…
    private static string MoveResolvingCollision(string source, string desired)
    {
        var target = desired;
        var dir = Path.GetDirectoryName(desired)!;
        var name = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);

        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(dir, $"{name} ({n}){ext}");

        File.Move(source, target, overwrite: false);
        return target;
    }
}
