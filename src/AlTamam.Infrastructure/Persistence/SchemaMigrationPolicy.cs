using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>Versioned, checksummed schema operations; caller owns the transaction.</summary>
public static class SchemaMigrationPolicy
{
    public static async Task RegisterAsync(SqliteConnection connection, SqliteTransaction transaction,
        string migrationName, string sql, CancellationToken cancellationToken = default)
    {
        using var table = connection.CreateCommand();
        table.Transaction = transaction;
        table.CommandText = """
            CREATE TABLE IF NOT EXISTS AppliedSchemaComponents (
                MigrationName TEXT PRIMARY KEY,
                Sha256 TEXT NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );
            """;
        await table.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql)));
        using var lookup = connection.CreateCommand();
        lookup.Transaction = transaction;
        lookup.CommandText = "SELECT Sha256 FROM AppliedSchemaComponents WHERE MigrationName=$name";
        lookup.Parameters.AddWithValue("$name", migrationName);
        var previous = await lookup.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (previous is string existing && existing != hash)
            throw new InvalidOperationException("An applied migration was changed. Explicit versioned migration is required.");
        using var apply = connection.CreateCommand();
        apply.Transaction = transaction;
        apply.CommandText = sql;
        await apply.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        using var receipt = connection.CreateCommand();
        receipt.Transaction = transaction;
        receipt.CommandText = "INSERT OR IGNORE INTO AppliedSchemaComponents VALUES($name,$hash,$utc)";
        receipt.Parameters.AddWithValue("$name", migrationName);
        receipt.Parameters.AddWithValue("$hash", hash);
        receipt.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        await receipt.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>SQLite online backup API preserves WAL contents and SQLCipher encryption.</summary>
    public static void CreateRecoverySnapshot(SqliteConnection source, string path, string key)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = temporary, Password = key, Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };
            using (var destination = new SqliteConnection(builder.ConnectionString))
            {
                destination.Open();
                source.BackupDatabase(destination);
                using var verify = destination.CreateCommand();
                verify.CommandText = "PRAGMA integrity_check;";
                if (!string.Equals(verify.ExecuteScalar()?.ToString(), "ok", StringComparison.Ordinal))
                    throw new InvalidOperationException("Pre-migration recovery snapshot failed integrity verification.");
            }
            File.Move(temporary, path, overwrite: false);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(temporary + suffix)) File.Delete(temporary + suffix);
        }
    }
}
