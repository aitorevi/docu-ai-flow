using System.Runtime.CompilerServices;
using System.Text.Json;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class SqlitePendingInvoiceRepository(IOptions<DatabaseOptions> opts) : IPendingInvoiceRepository
{
    private readonly string _path = Path.GetFullPath(opts.Value.Path);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        await using var conn = Open();
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        // Amounts are TEXT, not REAL: these are fiscal figures and a float silently loses cents.
        // Microsoft.Data.Sqlite round-trips decimal through TEXT exactly.
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS pending_invoices (
                content_hash          TEXT PRIMARY KEY,
                pending_path          TEXT NOT NULL,
                original_file_name    TEXT NOT NULL,
                invoice_number        TEXT NOT NULL,
                supplier_name         TEXT NOT NULL,
                supplier_tax_id       TEXT,
                issue_date            TEXT NOT NULL,
                due_date              TEXT,
                net_amount            TEXT NOT NULL,
                tax_amount            TEXT NOT NULL,
                total_amount          TEXT NOT NULL,
                currency              TEXT NOT NULL,
                confidence_json       TEXT NOT NULL,
                detected_at           TEXT NOT NULL,
                requires_manual_entry INTEGER NOT NULL DEFAULT 0,
                sourced_from_ocr      INTEGER NOT NULL DEFAULT 0
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveAsync(PendingInvoice p, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO pending_invoices
                (content_hash, pending_path, original_file_name, invoice_number, supplier_name,
                 supplier_tax_id, issue_date, due_date, net_amount, tax_amount, total_amount,
                 currency, confidence_json, detected_at, requires_manual_entry, sourced_from_ocr)
            VALUES
                (@hash, @path, @original, @number, @supplier,
                 @taxId, @issue, @due, @net, @tax, @total,
                 @currency, @confidence, @detectedAt, @manual, @ocr);
            """;
        cmd.Parameters.AddWithValue("@hash", p.ContentHash);
        cmd.Parameters.AddWithValue("@path", p.PendingPath);
        cmd.Parameters.AddWithValue("@original", p.OriginalFileName);
        cmd.Parameters.AddWithValue("@number", p.InvoiceNumber);
        cmd.Parameters.AddWithValue("@supplier", p.SupplierName);
        cmd.Parameters.AddWithValue("@taxId", (object?)p.SupplierTaxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@issue", p.IssueDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@due", p.DueDate.HasValue ? p.DueDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("@net", p.NetAmount);
        cmd.Parameters.AddWithValue("@tax", p.TaxAmount);
        cmd.Parameters.AddWithValue("@total", p.TotalAmount);
        cmd.Parameters.AddWithValue("@currency", p.Currency);
        cmd.Parameters.AddWithValue("@confidence", JsonSerializer.Serialize(p.Confidence));
        cmd.Parameters.AddWithValue("@detectedAt", p.DetectedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@manual", p.RequiresManualEntry ? 1 : 0);
        cmd.Parameters.AddWithValue("@ocr", p.SourcedFromOcr ? 1 : 0);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Oldest first: the review queue is a queue, and the invoice waiting longest goes first.
    public async IAsyncEnumerable<PendingInvoice> ListAllAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM pending_invoices ORDER BY detected_at;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return Map(reader);
    }

    public async Task<PendingInvoice?> FindByContentHashAsync(string contentHash, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM pending_invoices WHERE content_hash = @hash;";
        cmd.Parameters.AddWithValue("@hash", contentHash);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task DeleteAsync(string contentHash, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_invoices WHERE content_hash = @hash;";
        cmd.Parameters.AddWithValue("@hash", contentHash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountByTaxIdAsync(string? taxId, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = taxId is null
            ? "SELECT COUNT(*) FROM pending_invoices WHERE supplier_tax_id IS NULL;"
            : "SELECT COUNT(*) FROM pending_invoices WHERE supplier_tax_id = @taxId;";
        if (taxId is not null) cmd.Parameters.AddWithValue("@taxId", taxId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private SqliteConnection Open() => new($"Data Source={_path}");

    private static PendingInvoice Map(SqliteDataReader r)
    {
        var confidence = JsonSerializer.Deserialize<Dictionary<string, CapturedField>>(
            r.GetString(r.GetOrdinal("confidence_json"))) ?? [];

        return new PendingInvoice(
            ContentHash: r.GetString(r.GetOrdinal("content_hash")),
            PendingPath: r.GetString(r.GetOrdinal("pending_path")),
            OriginalFileName: r.GetString(r.GetOrdinal("original_file_name")),
            InvoiceNumber: r.GetString(r.GetOrdinal("invoice_number")),
            SupplierName: r.GetString(r.GetOrdinal("supplier_name")),
            SupplierTaxId: r.IsDBNull(r.GetOrdinal("supplier_tax_id")) ? null : r.GetString(r.GetOrdinal("supplier_tax_id")),
            IssueDate: DateOnly.Parse(r.GetString(r.GetOrdinal("issue_date"))),
            DueDate: r.IsDBNull(r.GetOrdinal("due_date")) ? null : DateOnly.Parse(r.GetString(r.GetOrdinal("due_date"))),
            NetAmount: r.GetDecimal(r.GetOrdinal("net_amount")),
            TaxAmount: r.GetDecimal(r.GetOrdinal("tax_amount")),
            TotalAmount: r.GetDecimal(r.GetOrdinal("total_amount")),
            Currency: r.GetString(r.GetOrdinal("currency")),
            Confidence: confidence,
            DetectedAt: DateTimeOffset.Parse(r.GetString(r.GetOrdinal("detected_at"))),
            RequiresManualEntry: r.GetInt32(r.GetOrdinal("requires_manual_entry")) != 0,
            SourcedFromOcr: r.GetInt32(r.GetOrdinal("sourced_from_ocr")) != 0);
    }
}
