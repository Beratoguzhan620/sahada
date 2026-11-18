using Npgsql;

namespace Sahada.Infrastructure.Storage;

/// <summary>
/// Photos in a table of the application's own database, for an installation with no object storage next to it (the
/// one-container public demo). Each photo is one row, read and written whole, like an object in a bucket.
/// </summary>
internal sealed class PostgresFileStore(NpgsqlDataSource database) : IFileStore, IAsyncDisposable
{
    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct)
    {
        await using var command = database.CreateCommand(
            "INSERT INTO stored_files (key, content, content_type) VALUES ($1, $2, $3) " +
            "ON CONFLICT (key) DO UPDATE SET content = excluded.content, content_type = excluded.content_type");
        command.Parameters.Add(new NpgsqlParameter { Value = key });
        command.Parameters.Add(new NpgsqlParameter { Value = content });
        command.Parameters.Add(new NpgsqlParameter { Value = contentType });
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        await using var command = database.CreateCommand("SELECT content FROM stored_files WHERE key = $1");
        command.Parameters.Add(new NpgsqlParameter { Value = key });
        return await command.ExecuteScalarAsync(ct) as byte[];
    }

    /// <summary>The table lives outside the migrations: only installations that choose this store have it.</summary>
    public async Task EnsureTableAsync(CancellationToken ct)
    {
        await using var command = database.CreateCommand(
            "CREATE TABLE IF NOT EXISTS stored_files (key text PRIMARY KEY, content bytea NOT NULL, content_type text NOT NULL)");
        await command.ExecuteNonQueryAsync(ct);
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();
}
