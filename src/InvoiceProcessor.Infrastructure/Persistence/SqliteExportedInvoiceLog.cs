using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class SqliteExportedInvoiceLog(IOptions<DatabaseOptions> opts)
    : IExportedInvoiceLog
{
    private readonly string _path = Path.GetFullPath(opts.Value.Path);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS exported_invoices (
                content_hash   TEXT PRIMARY KEY,
                quarter_year   INTEGER NOT NULL,
                quarter_number INTEGER NOT NULL,
                exported_at    TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> WasExportedAsync(string contentHash, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM exported_invoices WHERE content_hash = @hash);";
        cmd.Parameters.AddWithValue("@hash", contentHash);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result) == 1;
    }

    public async Task MarkExportedAsync(
        IEnumerable<string> contentHashes, Quarter quarter, DateTimeOffset exportedAt, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        foreach (var hash in contentHashes)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO exported_invoices
                    (content_hash, quarter_year, quarter_number, exported_at)
                VALUES (@hash, @year, @quarter, @exportedAt);
                """;
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.Parameters.AddWithValue("@year", quarter.Year);
            cmd.Parameters.AddWithValue("@quarter", quarter.Number);
            cmd.Parameters.AddWithValue("@exportedAt", exportedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private SqliteConnection Open() => new($"Data Source={_path}");
}
