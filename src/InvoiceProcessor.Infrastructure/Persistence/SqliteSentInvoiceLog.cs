using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class SqliteSentInvoiceLog(IOptions<DatabaseOptions> opts)
    : ISentInvoiceLog
{
    private readonly string _path = Path.GetFullPath(opts.Value.Path);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS sent_invoices (
                content_hash   TEXT PRIMARY KEY,
                quarter_year   INTEGER NOT NULL,
                quarter_number INTEGER NOT NULL,
                sent_at        TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> WasSentAsync(string contentHash, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM sent_invoices WHERE content_hash = @hash);";
        cmd.Parameters.AddWithValue("@hash", contentHash);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result) == 1;
    }

    public async Task MarkSentAsync(
        IEnumerable<string> contentHashes, Quarter quarter, DateTimeOffset sentAt, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        foreach (var hash in contentHashes)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT OR IGNORE INTO sent_invoices
                    (content_hash, quarter_year, quarter_number, sent_at)
                VALUES (@hash, @year, @quarter, @sentAt);
                """;
            cmd.Parameters.AddWithValue("@hash", hash);
            cmd.Parameters.AddWithValue("@year", quarter.Year);
            cmd.Parameters.AddWithValue("@quarter", quarter.Number);
            cmd.Parameters.AddWithValue("@sentAt", sentAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private SqliteConnection Open() => new($"Data Source={_path}");
}
