using System.Runtime.CompilerServices;
using InvoiceProcessor.Application.Invoices;
using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class SqliteProcessedInvoiceRepository(IOptions<DatabaseOptions> opts)
    : IProcessedInvoiceRepository
{
    private readonly string _path = Path.GetFullPath(opts.Value.Path);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS invoices (
                content_hash     TEXT PRIMARY KEY,
                invoice_number   TEXT NOT NULL,
                supplier_name    TEXT NOT NULL,
                supplier_tax_id  TEXT,
                issue_date       TEXT NOT NULL,
                due_date         TEXT,
                net_amount       REAL NOT NULL,
                tax_amount       REAL NOT NULL,
                total_amount     REAL NOT NULL,
                currency         TEXT NOT NULL,
                archived_path    TEXT NOT NULL,
                declared_year    INTEGER,
                declared_quarter INTEGER
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveAsync(StoredInvoice inv, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO invoices
                (content_hash, invoice_number, supplier_name, supplier_tax_id,
                 issue_date, due_date, net_amount, tax_amount, total_amount,
                 currency, archived_path)
            VALUES
                (@hash, @number, @supplier, @taxId,
                 @issue, @due, @net, @tax, @total,
                 @currency, @path);
            """;
        cmd.Parameters.AddWithValue("@hash", inv.ContentHash);
        cmd.Parameters.AddWithValue("@number", inv.InvoiceNumber);
        cmd.Parameters.AddWithValue("@supplier", inv.SupplierName);
        cmd.Parameters.AddWithValue("@taxId", (object?)inv.SupplierTaxId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@issue", inv.IssueDate.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@due", inv.DueDate.HasValue ? inv.DueDate.Value.ToString("yyyy-MM-dd") : DBNull.Value);
        cmd.Parameters.AddWithValue("@net", inv.NetAmount);
        cmd.Parameters.AddWithValue("@tax", inv.TaxAmount);
        cmd.Parameters.AddWithValue("@total", inv.TotalAmount);
        cmd.Parameters.AddWithValue("@currency", inv.Currency);
        cmd.Parameters.AddWithValue("@path", inv.ArchivedPath);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async IAsyncEnumerable<StoredInvoice> ListByDateRangeAsync(
        DateOnly start, DateOnly end, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM invoices
            WHERE issue_date BETWEEN @start AND @end
            ORDER BY issue_date;
            """;
        cmd.Parameters.AddWithValue("@start", start.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@end", end.ToString("yyyy-MM-dd"));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return Map(reader);
    }

    public async IAsyncEnumerable<StoredInvoice> ListAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM invoices ORDER BY issue_date;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            yield return Map(reader);
    }

    public async Task MarkDeclaredAsync(
        IEnumerable<string> contentHashes, Quarter quarter, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        foreach (var hash in contentHashes)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE invoices
                SET declared_year = @year, declared_quarter = @quarter
                WHERE content_hash = @hash AND declared_year IS NULL;
                """;
            cmd.Parameters.AddWithValue("@year", quarter.Year);
            cmd.Parameters.AddWithValue("@quarter", quarter.Number);
            cmd.Parameters.AddWithValue("@hash", hash);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private SqliteConnection Open() => new($"Data Source={_path}");

    private static StoredInvoice Map(SqliteDataReader r) => new(
        ContentHash: r.GetString(r.GetOrdinal("content_hash")),
        InvoiceNumber: r.GetString(r.GetOrdinal("invoice_number")),
        SupplierName: r.GetString(r.GetOrdinal("supplier_name")),
        SupplierTaxId: r.IsDBNull(r.GetOrdinal("supplier_tax_id")) ? null : r.GetString(r.GetOrdinal("supplier_tax_id")),
        IssueDate: DateOnly.Parse(r.GetString(r.GetOrdinal("issue_date"))),
        DueDate: r.IsDBNull(r.GetOrdinal("due_date")) ? null : DateOnly.Parse(r.GetString(r.GetOrdinal("due_date"))),
        NetAmount: r.GetDecimal(r.GetOrdinal("net_amount")),
        TaxAmount: r.GetDecimal(r.GetOrdinal("tax_amount")),
        TotalAmount: r.GetDecimal(r.GetOrdinal("total_amount")),
        Currency: r.GetString(r.GetOrdinal("currency")),
        ArchivedPath: r.GetString(r.GetOrdinal("archived_path")),
        DeclaredYear: r.IsDBNull(r.GetOrdinal("declared_year")) ? null : r.GetInt32(r.GetOrdinal("declared_year")),
        DeclaredQuarter: r.IsDBNull(r.GetOrdinal("declared_quarter")) ? null : r.GetInt32(r.GetOrdinal("declared_quarter"))
    );
}
