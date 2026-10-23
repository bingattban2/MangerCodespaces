using AlTamam.Contracts.Bridge;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

internal sealed class ServiceRejection(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

internal static class SqliteOperations
{
    internal static void Require(bool condition, string message, string code = BridgeErrorCodes.ValidationError)
    {
        if (!condition) throw new ServiceRejection(code, message);
    }
    internal static async Task RequireUnusedRequestAsync(SqliteConnection connection, SqliteTransaction transaction, string requestId)
    {
        Require(Convert.ToInt64(await ScalarAsync(connection, transaction,
            "SELECT COUNT(*) FROM AuditEvents WHERE CorrelationId=$id", ("$id", requestId)).ConfigureAwait(false))==0,
            "معرف الطلب مستخدم؛ حدّث البيانات قبل إعادة المحاولة.", BridgeErrorCodes.ConflictStale);
    }
    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var p in parameters) command.Parameters.AddWithValue(p.Name, p.Value ?? DBNull.Value);
        return command;
    }
    internal static async Task<int> ExecuteAsync(SqliteConnection c, SqliteTransaction? t, string sql, params (string Name, object? Value)[] p)
    {
        using var command = Command(c, t, sql, p); return await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
    internal static async Task<object?> ScalarAsync(SqliteConnection c, SqliteTransaction? t, string sql, params (string Name, object? Value)[] p)
    {
        using var command = Command(c, t, sql, p); return await command.ExecuteScalarAsync().ConfigureAwait(false);
    }
    internal static async Task<List<T>> QueryAsync<T>(SqliteConnection c, SqliteTransaction? t, string sql, Func<SqliteDataReader,T> map, params (string Name, object? Value)[] p)
    {
        using var command = Command(c, t, sql, p); using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var rows = new List<T>(); while (await reader.ReadAsync().ConfigureAwait(false)) rows.Add(map(reader)); return rows;
    }
    internal static async Task<ServiceResult<T>> BoundaryAsync<T>(Func<Task<ServiceResult<T>>> operation)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ServiceRejection ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6) { return ServiceResult<T>.Fail(BridgeErrorCodes.StorageBusy, "قاعدة البيانات مشغولة، حاول مجدداً."); }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) { return ServiceResult<T>.Fail(BridgeErrorCodes.ConflictStale, "تعارضت البيانات مع سجل قائم؛ لم يتم اعتماد العملية."); }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or System.Security.Cryptography.CryptographicException or IOException)
        { return ServiceResult<T>.Fail(BridgeErrorCodes.BackupIntegrityFailed, "تعذر تنفيذ العملية على قاعدة البيانات المشفرة."); }
    }
}
