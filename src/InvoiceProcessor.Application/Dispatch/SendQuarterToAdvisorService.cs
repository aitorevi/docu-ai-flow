using InvoiceProcessor.Application.Ports.Inbound;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace InvoiceProcessor.Application.Dispatch;

public sealed class SendQuarterToAdvisorService(
    IArchivedInvoiceSource source,
    ISentInvoiceLog sentLog,
    IInvoiceArchiveCompressor compressor,
    IAdvisorMailSender mailSender,
    IOptions<MailDispatchSettings>? settings = null) : ISendQuarterToAdvisorUseCase
{
    private readonly MailDispatchSettings _settings = settings?.Value ?? new MailDispatchSettings();

    public async Task<DispatchResult> ExecuteAsync(Quarter quarter, CancellationToken ct, bool dryRun = false)
    {
        var (start, end) = quarter.ExcelSourceRange();

        var pending = new List<ArchivedInvoice>();
        await foreach (var inv in source.ListByDateRangeAsync(start, end, ct))
            if (!await sentLog.WasSentAsync(inv.ContentHash, ct))
                pending.Add(inv);

        if (pending.Count == 0)
            return new DispatchResult(quarter, 0, NothingNew: true);

        var maxBytes = (long)_settings.MaxAttachmentMb * 1024 * 1024;
        var filePaths = pending.Select(p => p.FilePath).ToList();
        var archive = await compressor.CompressAsync(filePaths, $"facturas_{quarter}", ct);

        if (archive.FileSizeBytes <= maxBytes)
        {
            await SendSingleAsync(quarter, archive, pending, ct, dryRun);
            return new DispatchResult(quarter, pending.Count, NothingNew: false, Parts: 1, DryRun: dryRun);
        }

        // ZIP exceeds limit — compress and send month by month
        if (File.Exists(archive.Path)) File.Delete(archive.Path);

        var byMonth = pending
            .Where(p => p.InvoiceDate.HasValue)
            .GroupBy(p => p.InvoiceDate!.Value.Month)
            .OrderBy(g => g.Key)
            .ToList();

        // If no dates available, send all in one email regardless of size
        if (byMonth.Count == 0)
        {
            var fallback = await compressor.CompressAsync(filePaths, $"facturas_{quarter}", ct);
            await SendSingleAsync(quarter, fallback, pending, ct, dryRun);
            return new DispatchResult(quarter, pending.Count, NothingNew: false, Parts: 1, DryRun: dryRun);
        }

        var allHashes = new List<string>();
        var parts = byMonth.Count;

        for (var i = 0; i < parts; i++)
        {
            var group = byMonth[i];
            var month = group.Key;
            var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToLower();
            var partPaths = (IReadOnlyCollection<string>)group.Select(p => p.FilePath).ToList();
            var partArchive = await compressor.CompressAsync(partPaths, $"facturas_{quarter}-parte{i + 1}", ct);

            var mail = new MailWithAttachment(
                Subject: $"Facturas {quarter} - Parte {i + 1}/{parts} ({monthName})",
                Body: $"Adjunto las facturas de {monthName} del trimestre {quarter} (parte {i + 1} de {parts}).",
                AttachmentName: partArchive.FileName,
                AttachmentPath: partArchive.Path);

            await mailSender.SendAsync(mail, ct);
            allHashes.AddRange(group.Select(p => p.ContentHash));
        }

        if (!dryRun)
            await sentLog.MarkSentAsync(allHashes, quarter, DateTimeOffset.UtcNow, ct);
        return new DispatchResult(quarter, allHashes.Count, NothingNew: false, Parts: parts, DryRun: dryRun);
    }

    private async Task SendSingleAsync(Quarter quarter, CompressedArchive archive, List<ArchivedInvoice> pending, CancellationToken ct, bool dryRun = false)
    {
        var mail = new MailWithAttachment(
            Subject: $"Facturas {quarter}",
            Body: $"Adjunto las facturas del trimestre {quarter}.",
            AttachmentName: archive.FileName,
            AttachmentPath: archive.Path);

        await mailSender.SendAsync(mail, ct);

        if (!dryRun)
        {
            var hashes = pending.Select(p => p.ContentHash).ToList();
            await sentLog.MarkSentAsync(hashes, quarter, DateTimeOffset.UtcNow, ct);
        }
    }
}
