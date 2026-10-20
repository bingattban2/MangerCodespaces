using System.Reflection;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>Encrypted SQLite connection management and atomic initialization of schema V001.</summary>
public sealed class AlTamamDbContext : IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _databasePath;
    private readonly string _databaseKey;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    static AlTamamDbContext() => SQLitePCL.Batteries_V2.Init();

    public AlTamamDbContext(string databasePath, string databaseKeyHex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseKeyHex);
        byte[] key;
        try { key = Convert.FromHexString(databaseKeyHex); }
        catch (FormatException ex) { throw new ArgumentException("Invalid database key encoding.", nameof(databaseKeyHex), ex); }
        if (key.Length != 32) throw new ArgumentException("The database key must contain 32 bytes.", nameof(databaseKeyHex));
        _databasePath = Path.GetFullPath(databasePath);
        _databaseKey = databaseKeyHex;
    }

    public string DatabasePath => _databasePath;

    public SqliteConnection CreateOpenConnection()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = _databaseKey,
            ForeignKeys = true,
            DefaultTimeout = 15
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            using var cipher = connection.CreateCommand();
            cipher.CommandText = "PRAGMA cipher_version;";
            if (string.IsNullOrWhiteSpace(cipher.ExecuteScalar()?.ToString()))
                throw new InvalidOperationException("SQLCipher native encryption provider is unavailable.");
            using var settings = connection.CreateCommand();
            settings.CommandText = "PRAGMA foreign_keys=ON; PRAGMA journal_mode=WAL; PRAGMA busy_timeout=15000;";
            settings.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            var hasVersionTable = false;
            using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_master WHERE type='table' AND name='SchemaVersions');";
                hasVersionTable = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
            }
            if (hasVersionTable)
            {
                using var check = connection.CreateCommand();
                check.Transaction = transaction;
                check.CommandText = "SELECT MAX(SchemaVersion) FROM SchemaVersions;";
                var result = await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                var version = result is DBNull or null ? 0 : Convert.ToInt32(result);
                if (version != CurrentSchemaVersion)
                    throw new InvalidOperationException($"Database schema version {version} is incompatible with {CurrentSchemaVersion}.");
            }
            else
            {
                var assembly = typeof(AlTamamDbContext).Assembly;
                const string resource = "AlTamam.Infrastructure.Persistence.Migrations.V001_InitialSchema.sql";
                await using var stream = assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException("The embedded schema resource is missing.");
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                using var schema = connection.CreateCommand();
                schema.Transaction = transaction;
                schema.CommandText = sql;
                await schema.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            // Additive 02.02 guards apply to BOTH fresh V001 databases and already
            // initialized V001 databases, without rewriting retained V001 or old rows.
            // The comprehensive versioned migration policy belongs to Phase 02.05.
            // Reject orphaned/contradictory old data; do not silently repair history.
            using (var referenceCheck = connection.CreateCommand())
            {
                referenceCheck.Transaction = transaction;
                referenceCheck.CommandText = "PRAGMA foreign_key_check;";
                await using var invalid = await referenceCheck.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await invalid.ReadAsync(cancellationToken).ConfigureAwait(false))
                    throw new InvalidOperationException("Existing database contains a foreign-key violation; preserve data and block schema hardening.");
            }
            using (var relationCheck = connection.CreateCommand())
            {
                relationCheck.Transaction = transaction;
                relationCheck.CommandText = """
                    SELECT
                        (SELECT COUNT(*) FROM PersonAssignments p
                         WHERE p.SourceMovementId IS NOT NULL AND NOT EXISTS (
                           SELECT 1 FROM PersonnelMovements m
                           WHERE m.MovementId = p.SourceMovementId AND m.PersonId = p.PersonId))
                        +
                        (SELECT COUNT(*) FROM AttendanceDrafts d
                         WHERE NOT EXISTS (SELECT 1 FROM AttendanceStatusDefinitions s
                                          WHERE s.StatusId = d.TargetStatusId)
                            OR (d.SiteId IS NOT NULL AND NOT EXISTS(
                                   SELECT 1 FROM Sites s WHERE s.SiteId = d.SiteId)))
                        +
                        (SELECT COUNT(*) FROM AttendanceRecords a
                         WHERE NOT EXISTS (SELECT 1 FROM PersonAssignments p
                           WHERE p.AssignmentId = a.AssignmentIdAtEffectiveDate
                             AND p.PersonId = a.PersonId AND p.SiteId = a.SiteIdAtEffectiveDate))
                        +
                        (SELECT COUNT(*) FROM AttendanceRevisions r
                         WHERE NOT EXISTS (SELECT 1 FROM AttendanceStatusDefinitions s
                                           WHERE s.StatusId=r.BeforeStatusId)
                            OR NOT EXISTS (SELECT 1 FROM AttendanceStatusDefinitions s
                                           WHERE s.StatusId=r.AfterStatusId))
                        +
                        (SELECT COUNT(*) FROM AttendanceDrafts d
                         WHERE COALESCE(json_valid(d.SelectedPersonIdsJson),0)=0
                            OR CASE WHEN json_valid(d.SelectedPersonIdsJson)
                                    THEN json_type(d.SelectedPersonIdsJson)<>'array'
                                    ELSE 1 END
                            OR EXISTS (
                              SELECT 1 FROM json_each(
                                CASE WHEN json_valid(d.SelectedPersonIdsJson)
                                     THEN d.SelectedPersonIdsJson ELSE '[]' END) AS member
                              WHERE member.type<>'text'
                                OR NOT EXISTS (SELECT 1 FROM Persons p
                                               WHERE p.PersonId=member.value)));
                    """;
                var orphanCount = Convert.ToInt64(await relationCheck.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
                if (orphanCount != 0)
                    throw new InvalidOperationException(
                        $"Existing database has {orphanCount} inconsistent stable-ID relationships; no rows or historical snapshots changed.");
            }
            var guardResource = typeof(AlTamamDbContext).Assembly.GetManifestResourceStream(
                "AlTamam.Infrastructure.Persistence.Migrations.V001_RelationalGuards.sql")
                ?? throw new InvalidOperationException("Required additive 02.02 relational guards are missing.");
            await using (guardResource)
            using (var reader = new StreamReader(guardResource))
            {
                var guardsSql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                using var guards = connection.CreateCommand();
                guards.Transaction = transaction;
                guards.CommandText = guardsSql;
                await guards.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    public async Task<T> ExecuteInWriteTransactionAsync<T>(
        Func<SqliteConnection, SqliteTransaction, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var connection = CreateOpenConnection();
            using var transaction = connection.BeginTransaction();
            var result = await operation(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { _writeGate.Release(); }
    }

    public bool HasValidIntegrity()
    {
        using var connection = CreateOpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check;";
        return string.Equals(cmd.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose() => _writeGate.Dispose();
}
