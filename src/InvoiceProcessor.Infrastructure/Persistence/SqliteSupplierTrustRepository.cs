using InvoiceProcessor.Application.Ports.Outbound;
using InvoiceProcessor.Domain.Invoices;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace InvoiceProcessor.Infrastructure.Persistence;

public sealed class SqliteSupplierTrustRepository(IOptions<DatabaseOptions> opts) : ISupplierTrustRepository
{
    private readonly string _path = Path.GetFullPath(opts.Value.Path);

    public async Task EnsureCreatedAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS supplier_trust (
                tax_id           TEXT PRIMARY KEY,
                unmodified_count INTEGER NOT NULL,
                is_trusted       INTEGER NOT NULL,
                updated_at       TEXT NOT NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<SupplierTrust?> GetAsync(string taxId, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tax_id, unmodified_count, is_trusted FROM supplier_trust WHERE tax_id = @taxId;";
        cmd.Parameters.AddWithValue("@taxId", taxId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new SupplierTrust(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2) != 0);
    }

    public async Task<IReadOnlyList<SupplierTrust>> ListAllAsync(CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT tax_id, unmodified_count, is_trusted FROM supplier_trust;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var result = new List<SupplierTrust>();
        while (await reader.ReadAsync(ct))
            result.Add(new SupplierTrust(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2) != 0));
        return result;
    }

    public async Task SaveAsync(SupplierTrust trust, CancellationToken ct)
    {
        await using var conn = Open();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO supplier_trust (tax_id, unmodified_count, is_trusted, updated_at)
            VALUES (@taxId, @count, @trusted, @updatedAt)
            ON CONFLICT(tax_id) DO UPDATE SET
                unmodified_count = excluded.unmodified_count,
                is_trusted       = excluded.is_trusted,
                updated_at       = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("@taxId", trust.TaxId);
        cmd.Parameters.AddWithValue("@count", trust.ConsecutiveUnmodifiedCount);
        cmd.Parameters.AddWithValue("@trusted", trust.IsTrusted ? 1 : 0);
        cmd.Parameters.AddWithValue("@updatedAt", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private SqliteConnection Open() => new($"Data Source={_path}");
}
