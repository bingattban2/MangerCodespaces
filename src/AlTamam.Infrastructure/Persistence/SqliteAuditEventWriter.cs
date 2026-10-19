using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AlTamam.Domain.Entities;
using AlTamam.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>
/// Appends immutable, hash-chained AuditEvent records inside an active SQLite write transaction
/// with idempotent deduplication by (OperationCode, CorrelationId).
/// </summary>
public static class SqliteAuditEventWriter
{
    private const string GenesisHash = "GENESIS";

    public static async Task<AuditEvent> AppendAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationCode,
        string operationArabicName,
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string targetEntityType,
        string targetEntityId,
        string targetDisplaySnapshot,
        string? oldValueJson,
        string? newValueJson,
        string defaultArabicDescription,
        string? reason,
        DateOnly effectiveLocalDate,
        DateTimeOffset executedAtUtc,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationArabicName);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorUsernameSnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntityType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDisplaySnapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(defaultArabicDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        // Idempotency check: if an audit event with the same OperationCode and CorrelationId already exists, return it.
        await using (var existingCmd = connection.CreateCommand())
        {
            existingCmd.Transaction = transaction;
            existingCmd.CommandText = """
                SELECT AuditEventId, OperationCode, OperationArabicName,
                       ActorUserId, ActorPersonId, ActorUsernameSnapshot, ActorRosterFullNameSnapshot,
                       TargetEntityType, TargetEntityId, TargetDisplaySnapshot,
                       OldValueJson, NewValueJson, DefaultArabicDescription, Reason,
                       EffectiveLocalDate, ExecutedAtUtc, CorrelationId, EventHash, PriorEventHash
                FROM AuditEvents
                WHERE OperationCode = $opCode AND CorrelationId = $correlationId
                LIMIT 1;
                """;
            existingCmd.Parameters.AddWithValue("$opCode", operationCode);
            existingCmd.Parameters.AddWithValue("$correlationId", correlationId);

            await using var reader = await existingCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return new AuditEvent(
                    AuditEventId: new AuditEventId(reader.GetString(0)),
                    OperationCode: reader.GetString(1),
                    OperationArabicName: reader.GetString(2),
                    ActorUserId: new UserId(reader.GetString(3)),
                    ActorPersonId: reader.IsDBNull(4) ? null : new PersonId(reader.GetString(4)),
                    ActorUsernameSnapshot: reader.GetString(5),
                    ActorRosterFullNameSnapshot: reader.IsDBNull(6) ? null : reader.GetString(6),
                    TargetEntityType: reader.GetString(7),
                    TargetEntityId: reader.GetString(8),
                    TargetDisplaySnapshot: reader.GetString(9),
                    OldValueJson: reader.IsDBNull(10) ? null : reader.GetString(10),
                    NewValueJson: reader.IsDBNull(11) ? null : reader.GetString(11),
                    DefaultArabicDescription: reader.GetString(12),
                    Reason: reader.IsDBNull(13) ? null : reader.GetString(13),
                    EffectiveLocalDate: DateOnly.ParseExact(reader.GetString(14), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ExecutedAtUtc: DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    CorrelationId: reader.GetString(16),
                    EventHash: reader.GetString(17),
                    PriorEventHash: reader.GetString(18));
            }
        }

        string priorHash = GenesisHash;
        await using (var priorCmd = connection.CreateCommand())
        {
            priorCmd.Transaction = transaction;
            priorCmd.CommandText = "SELECT EventHash FROM AuditEvents ORDER BY ExecutedAtUtc DESC, rowid DESC LIMIT 1;";
            var scalar = await priorCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (scalar is string existingHash && !string.IsNullOrWhiteSpace(existingHash))
            {
                priorHash = existingHash;
            }
        }

        var auditEventId = new AuditEventId($"AUD-{Guid.NewGuid():N}".ToUpperInvariant());
        var effectiveDateIso = effectiveLocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var executedAtIso = executedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        var canonicalPayload = string.Join("|",
            priorHash,
            auditEventId.Value,
            operationCode,
            actorUserId.Value,
            actorPersonId?.Value ?? string.Empty,
            actorUsernameSnapshot,
            actorRosterFullNameSnapshot ?? string.Empty,
            targetEntityType,
            targetEntityId,
            oldValueJson ?? string.Empty,
            newValueJson ?? string.Empty,
            effectiveDateIso,
            executedAtIso,
            correlationId);

        var eventHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)));

        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.Transaction = transaction;
            insertCmd.CommandText = """
                INSERT INTO AuditEvents (
                    AuditEventId, OperationCode, OperationArabicName,
                    ActorUserId, ActorPersonId, ActorUsernameSnapshot, ActorRosterFullNameSnapshot,
                    TargetEntityType, TargetEntityId, TargetDisplaySnapshot,
                    OldValueJson, NewValueJson, DefaultArabicDescription, Reason,
                    EffectiveLocalDate, ExecutedAtUtc, CorrelationId, EventHash, PriorEventHash
                ) VALUES (
                    $id, $opCode, $opName,
                    $actorUserId, $actorPersonId, $actorUsername, $actorFullName,
                    $targetType, $targetId, $targetDisplay,
                    $oldJson, $newJson, $desc, $reason,
                    $effectiveDate, $executedAt, $correlationId, $eventHash, $priorHash
                );
                """;

            insertCmd.Parameters.AddWithValue("$id", auditEventId.Value);
            insertCmd.Parameters.AddWithValue("$opCode", operationCode);
            insertCmd.Parameters.AddWithValue("$opName", operationArabicName);
            insertCmd.Parameters.AddWithValue("$actorUserId", actorUserId.Value);
            insertCmd.Parameters.AddWithValue("$actorPersonId", (object?)actorPersonId?.Value ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$actorUsername", actorUsernameSnapshot);
            insertCmd.Parameters.AddWithValue("$actorFullName", (object?)actorRosterFullNameSnapshot ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$targetType", targetEntityType);
            insertCmd.Parameters.AddWithValue("$targetId", targetEntityId);
            insertCmd.Parameters.AddWithValue("$targetDisplay", targetDisplaySnapshot);
            insertCmd.Parameters.AddWithValue("$oldJson", (object?)oldValueJson ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$newJson", (object?)newValueJson ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$desc", defaultArabicDescription);
            insertCmd.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("$effectiveDate", effectiveDateIso);
            insertCmd.Parameters.AddWithValue("$executedAt", executedAtIso);
            insertCmd.Parameters.AddWithValue("$correlationId", correlationId);
            insertCmd.Parameters.AddWithValue("$eventHash", eventHash);
            insertCmd.Parameters.AddWithValue("$priorHash", priorHash);

            await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return new AuditEvent(
            AuditEventId: auditEventId,
            OperationCode: operationCode,
            OperationArabicName: operationArabicName,
            ActorUserId: actorUserId,
            ActorPersonId: actorPersonId,
            ActorUsernameSnapshot: actorUsernameSnapshot,
            ActorRosterFullNameSnapshot: actorRosterFullNameSnapshot,
            TargetEntityType: targetEntityType,
            TargetEntityId: targetEntityId,
            TargetDisplaySnapshot: targetDisplaySnapshot,
            OldValueJson: oldValueJson,
            NewValueJson: newValueJson,
            DefaultArabicDescription: defaultArabicDescription,
            Reason: reason,
            EffectiveLocalDate: effectiveLocalDate,
            ExecutedAtUtc: executedAtUtc,
            CorrelationId: correlationId,
            EventHash: eventHash,
            PriorEventHash: priorHash);
    }
}
