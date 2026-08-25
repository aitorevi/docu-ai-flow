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
    private readonly string _pending = Path.GetFullPath(folders.Value.Pending);
    private readonly string _duplicates = Path.GetFullPath(folders.Value.Duplicates);
    private readonly string _inbox   = Path.GetFullPath(folders.Value.Inbox);

    // Procesado → {Archive}/2026/01/proveedor/proveedor-f2026-0042.pdf (año y mes = fecha de EMISIÓN)
    public Task<string> ArchiveProcessedAsync(IncomingDocument doc, Invoice invoice, CancellationToken ct) =>
        Task.FromResult(MoveResolvingCollision(
            doc.Location, BuildArchiveTarget(invoice, Path.GetExtension(doc.FileName))));

    // Duplicado → {Duplicates}/20260113-101500-factura.pdf. Se saca del inbox para que el
    // watcher deje de reprocesarlo, y el prefijo de fecha deja rastro de cuándo llegó.
    public Task<string> ArchiveDuplicateAsync(IncomingDocument doc, CancellationToken ct)
    {
        Directory.CreateDirectory(_duplicates);
        var target = Path.Combine(_duplicates, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{doc.FileName}");
        return Task.FromResult(MoveResolvingCollision(doc.Location, target));
    }

    // Pendiente de revisión → {Pending}/proveedor-factura.pdf. El PDF se retiene aquí, sin
    // archivar, hasta que un humano lo confirme.
    public Task<string> ArchivePendingAsync(IncomingDocument doc, string supplierName, CancellationToken ct)
    {
        Directory.CreateDirectory(_pending);
        var prefix = string.IsNullOrWhiteSpace(supplierName)
            ? string.Empty : CanonicalizeSupplierName(supplierName) + "-";
        var target = Path.Combine(_pending, $"{prefix}{doc.FileName}");
        return Task.FromResult(MoveResolvingCollision(doc.Location, target));
    }

    // Confirmada → mueve el PDF retenido en pending/ a su ruta definitiva de archive/.
    public Task<string> ArchiveConfirmedAsync(string pendingPath, Invoice invoice, CancellationToken ct) =>
        Task.FromResult(MoveResolvingCollision(
            pendingPath, BuildArchiveTarget(invoice, Path.GetExtension(pendingPath))));

    // Rechazada → pending/ a failed/. false si el PDF ya no está en disco.
    public Task<bool> RejectPendingAsync(string pendingPath, CancellationToken ct)
    {
        if (!File.Exists(pendingPath)) return Task.FromResult(false);
        Directory.CreateDirectory(_failed);
        var target = Path.Combine(_failed, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Path.GetFileName(pendingPath)}");
        MoveResolvingCollision(pendingPath, target);
        return Task.FromResult(true);
    }

    // Reprocesar → pending/ a inbox/, p. ej. tras escribir su plantilla. Vuelve con su NOMBRE
    // ORIGINAL, no con el de pending/ (que lleva el prefijo del proveedor): de lo contrario cada
    // vuelta acumularía otro prefijo — proveedor-proveedor-proveedor-factura.pdf.
    public Task<bool> RequeuePendingAsync(string pendingPath, string originalFileName, CancellationToken ct)
    {
        if (!File.Exists(pendingPath)) return Task.FromResult(false);
        Directory.CreateDirectory(_inbox);
        MoveResolvingCollision(pendingPath, Path.Combine(_inbox, originalFileName));
        return Task.FromResult(true);
    }

    // Ruta definitiva {Archive}/{año}/{mes}/{proveedor}/{proveedor}-{numero}{ext}.
    private string BuildArchiveTarget(Invoice invoice, string extension)
    {
        var supplier = CanonicalizeSupplierName(invoice.Supplier.Name);
        var dir = Path.Combine(
            _archive,
            invoice.IssueDate.Year.ToString("D4"),
            invoice.IssueDate.Month.ToString("D2"),
            supplier);
        Directory.CreateDirectory(dir);

        var number = Sanitize(invoice.InvoiceNumber, fallback: "sin-numero").ToLowerInvariant();
        return Path.Combine(dir, $"{supplier}-{number}{extension.ToLowerInvariant()}");
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
