using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlTamam.Application.Abstractions;
using AlTamam.Application.Traceability;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Traceability;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Entities;
using AlTamam.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>
/// Inspects the encrypted SQLCipher database schema, triggers, and baseline seed catalog
/// to verify alignment with the canonical requirements traceability matrix.
/// </summary>
public sealed class SqliteSchemaCatalogProbe : ISchemaCatalogProbe
{
    private readonly AlTamamDbContext _dbContext;
    private readonly ISystemClock _clock;

    public SqliteSchemaCatalogProbe(AlTamamDbContext dbContext, ISystemClock clock)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(clock);
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<ServiceResult<DatabaseSchemaVerificationSnapshotDto>> InspectSchemaAndSeedStateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _dbContext.CreateOpenConnection();

            var cipherVersion = await ExecuteScalarStringAsync(connection, "PRAGMA cipher_version;", cancellationToken).ConfigureAwait(false);
            bool encryptionActive = !string.IsNullOrWhiteSpace(cipherVersion);

            var integrityResult = await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken).ConfigureAwait(false);
            bool integrityPassed = string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase);

            var presentTables = await QueryNamesFromMasterAsync(connection, "table", cancellationToken).ConfigureAwait(false);
            var presentTriggers = await QueryNamesFromMasterAsync(connection, "trigger", cancellationToken).ConfigureAwait(false);

            var missingTables = RequirementTraceabilityService.CanonicalTables
                .Where(expected => !presentTables.Contains(expected, StringComparer.Ordinal))
                .ToArray();

            var missingTriggers = RequirementTraceabilityService.CanonicalTriggers
                .Where(expected => !presentTriggers.Contains(expected, StringComparer.Ordinal))
                .ToArray();

            int schemaVersion = 0;
            if (presentTables.Contains("SchemaVersions", StringComparer.Ordinal))
            {
                schemaVersion = await ExecuteScalarIntAsync(
                    connection,
                    "SELECT COALESCE(MAX(SchemaVersion), 0) FROM SchemaVersions;",
                    cancellationToken).ConfigureAwait(false);
            }

            bool hasUnspecifiedSite = false;
            if (presentTables.Contains("Sites", StringComparer.Ordinal))
            {
                await using var siteCmd = connection.CreateCommand();
                siteCmd.CommandText = "SELECT COUNT(1) FROM Sites WHERE SiteId = $siteId AND IsSystemSite = 1 AND ActiveFlag = 1;";
                siteCmd.Parameters.AddWithValue("$siteId", SiteId.Unspecified.Value);
                var siteScalar = await siteCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                hasUnspecifiedSite = (siteScalar is null or DBNull ? 0 : Convert.ToInt32(siteScalar)) == 1;
            }

            int seededStatuses = presentTables.Contains("AttendanceStatusDefinitions", StringComparer.Ordinal)
                ? await ExecuteScalarIntAsync(
                    connection,
                    "SELECT COUNT(1) FROM AttendanceStatusDefinitions WHERE IsSystemSeeded = 1 AND ActiveFlag = 1;",
                    cancellationToken).ConfigureAwait(false)
                : 0;

            int seededColumns = presentTables.Contains("CustomFieldDefinitions", StringComparer.Ordinal)
                ? await ExecuteScalarIntAsync(
                    connection,
                    "SELECT COUNT(1) FROM CustomFieldDefinitions WHERE IsSystemProtected = 1 AND ActiveFlag = 1;",
                    cancellationToken).ConfigureAwait(false)
                : 0;

            int seededSettings = presentTables.Contains("AppSettings", StringComparer.Ordinal)
                ? await ExecuteScalarIntAsync(
                    connection,
                    "SELECT COUNT(1) FROM AppSettings;",
                    cancellationToken).ConfigureAwait(false)
                : 0;

            int usersCount = presentTables.Contains("Users", StringComparer.Ordinal)
                ? await ExecuteScalarIntAsync(
                    connection,
                    "SELECT COUNT(1) FROM Users;",
                    cancellationToken).ConfigureAwait(false)
                : 0;

            int personsCount = presentTables.Contains("Persons", StringComparer.Ordinal)
                ? await ExecuteScalarIntAsync(
                    connection,
                    "SELECT COUNT(1) FROM Persons;",
                    cancellationToken).ConfigureAwait(false)
                : 0;

            var snapshot = new DatabaseSchemaVerificationSnapshotDto(
                EncryptionProviderActive: encryptionActive,
                IntegrityCheckPassed: integrityPassed,
                CurrentSchemaVersion: schemaVersion,
                PresentTables: presentTables,
                MissingTables: missingTables,
                PresentTriggers: presentTriggers,
                MissingTriggers: missingTriggers,
                HasProtectedUnspecifiedSite: hasUnspecifiedSite,
                SeededDefaultStatusesCount: seededStatuses,
                SeededSystemColumnsCount: seededColumns,
                SeededAppSettingsCount: seededSettings,
                PreSeededUsersCount: usersCount,
                PreSeededPersonsCount: personsCount);

            return ServiceResult<DatabaseSchemaVerificationSnapshotDto>.Ok(snapshot);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<DatabaseSchemaVerificationSnapshotDto>.Fail(
                BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة حالياً بعملية أخرى. يرجى المحاولة بعد قليل.",
                ex.Message);
        }
        catch (Exception ex)
        {
            return ServiceResult<DatabaseSchemaVerificationSnapshotDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر التحقق من سلامة ومخطط قاعدة البيانات المشفرة.",
                ex.Message);
        }
    }

    public async Task<ServiceResult<AuditEvent>> RecordSchemaVerificationAuditAsync(
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var auditEvent = await _dbContext.ExecuteInWriteTransactionAsync(
                async (connection, transaction) =>
                {
                    return await SqliteAuditEventWriter.AppendAsync(
                        connection: connection,
                        transaction: transaction,
                        operationCode: "TRACEABILITY_SCHEMA_VERIFIED",
                        operationArabicName: "فحص مطابقة المتطلبات ومخطط قاعدة البيانات",
                        actorUserId: actorUserId,
                        actorPersonId: actorPersonId,
                        actorUsernameSnapshot: actorUsernameSnapshot,
                        actorRosterFullNameSnapshot: actorRosterFullNameSnapshot,
                        targetEntityType: "SchemaVersions",
                        targetEntityId: "V001_InitialSchema",
                        targetDisplaySnapshot: "مخطط قاعدة البيانات V001 ومصفوفة المتطلبات",
                        oldValueJson: null,
                        newValueJson: "{\"schemaVersion\":1,\"verified\":true}",
                        defaultArabicDescription: "تم التحقق من مطابقة المتطلبات القياسية وسلامة مخطط قاعدة البيانات المشفرة V001.",
                        reason: null,
                        effectiveLocalDate: _clock.CurrentBusinessDate,
                        executedAtUtc: _clock.UtcNow,
                        correlationId: correlationId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            return ServiceResult<AuditEvent>.Ok(auditEvent);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة حالياً وتعذر تسجيل قيد التدقيق.",
                ex.Message);
        }
        catch (Exception ex)
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.ValidationError,
                "تعذر تسجيل قيد سجل العمليات لفحص مطابقة المتطلبات.",
                ex.Message);
        }
    }

    public async Task<ServiceResult<AuditEvent>> RecordPolicyConflictResolutionAuditAsync(
        string resolutionManifestJson,
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resolutionManifestJson))
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.ValidationError,
                "ملخص حسم تعارضات القواعد مطلوب ولا يمكن تركه فارغاً.");
        }

        try
        {
            var auditEvent = await _dbContext.ExecuteInWriteTransactionAsync(
                async (connection, transaction) =>
                {
                    // Replaying the same correlation must not rewrite AppSettings without a new audit event.
                    // The audit append function returns the original event for this correlation.
                    var isReplay = await HasExistingAuditCorrelationAsync(
                        connection, transaction, "POLICY_CONFLICTS_RESOLVED", correlationId, cancellationToken).ConfigureAwait(false);

                    string? previousManifestJson = null;
                    if (!isReplay)
                    {
                        await using (var readOldCmd = connection.CreateCommand())
                        {
                            readOldCmd.Transaction = transaction;
                            readOldCmd.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = $key LIMIT 1;";
                            readOldCmd.Parameters.AddWithValue("$key", "PolicyConflictsResolutionManifest");
                            var existingScalar = await readOldCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                            if (existingScalar is string existingText && !string.IsNullOrWhiteSpace(existingText))
                            {
                                previousManifestJson = existingText;
                            }
                        }

                        await using (var settingCmd = connection.CreateCommand())
                        {
                            settingCmd.Transaction = transaction;
                            settingCmd.CommandText = """
                                INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAtUtc)
                                VALUES ($key, $val, $updatedAt)
                                ON CONFLICT(SettingKey) DO UPDATE SET
                                    SettingValue = excluded.SettingValue,
                                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                                """;
                            settingCmd.Parameters.AddWithValue("$key", "PolicyConflictsResolutionManifest");
                            settingCmd.Parameters.AddWithValue("$val", resolutionManifestJson);
                            settingCmd.Parameters.AddWithValue("$updatedAt", _clock.UtcNow.ToUniversalTime().ToString("O"));
                            await settingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                    }

                    return await SqliteAuditEventWriter.AppendAsync(
                        connection: connection,
                        transaction: transaction,
                        operationCode: "POLICY_CONFLICTS_RESOLVED",
                        operationArabicName: "اعتماد وحسم تعارضات القواعد الخمسة",
                        actorUserId: actorUserId,
                        actorPersonId: actorPersonId,
                        actorUsernameSnapshot: actorUsernameSnapshot,
                        actorRosterFullNameSnapshot: actorRosterFullNameSnapshot,
                        targetEntityType: "AppSettings",
                        targetEntityId: "PolicyConflictsResolutionManifest",
                        targetDisplaySnapshot: "سجل حسم تعارضات القواعد القياسية (5 محاور)",
                        oldValueJson: previousManifestJson,
                        newValueJson: resolutionManifestJson,
                        defaultArabicDescription: "تم تثبيت وحسم تعارضات القواعد الخمسة مع حفظ موقفي المصدر.",
                        reason: "اعتماد المالك الصريح لقواعد التصفير والأدوار وسجل العمليات ويوم النقل والتهيئة الأولى",
                        effectiveLocalDate: _clock.CurrentBusinessDate,
                        executedAtUtc: _clock.UtcNow,
                        correlationId: correlationId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            return ServiceResult<AuditEvent>.Ok(auditEvent);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة حالياً وتعذر حفظ حسم تعارضات القواعد.",
                ex.Message);
        }
        catch (Exception ex)
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.ValidationError,
                "تعذر حفظ سجل حسم تعارضات القواعد في قاعدة البيانات.",
                ex.Message);
        }
    }

    public async Task<ServiceResult<string?>> GetPersistedPolicyConflictManifestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _dbContext.CreateOpenConnection();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", "PolicyConflictsResolutionManifest");
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var text = result is null or DBNull ? null : Convert.ToString(result);
            return ServiceResult<string?>.Ok(text ?? string.Empty);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<string?>.Fail(
                BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة حالياً وتعذر قراءة سجل حسم التعارضات.",
                ex.Message);
        }
        catch (Exception ex)
        {
            return ServiceResult<string?>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر قراءة سجل حسم تعارضات القواعد من قاعدة البيانات.",
                ex.Message);
        }
    }

    public async Task<ServiceResult<AuditEvent>> RecordBaselineInventoryAuditAsync(
        string baselineSummaryJson,
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(baselineSummaryJson))
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.ValidationError,
                "ملخص فحص خط الأساس للمستودع مطلوب ولا يمكن تركه فارغاً.");
        }

        try
        {
            var auditEvent = await _dbContext.ExecuteInWriteTransactionAsync(
                async (connection, transaction) =>
                {
                    // Replaying the same correlation must not rewrite AppSettings without a new audit event.
                    // The audit append function returns the original event for this correlation.
                    var isReplay = await HasExistingAuditCorrelationAsync(
                        connection, transaction, "SOURCE_BASELINE_INVENTORIED", correlationId, cancellationToken).ConfigureAwait(false);

                    string? previousSummaryJson = null;
                    if (!isReplay)
                    {
                        await using (var readOldCmd = connection.CreateCommand())
                        {
                            readOldCmd.Transaction = transaction;
                            readOldCmd.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = $key LIMIT 1;";
                            readOldCmd.Parameters.AddWithValue("$key", "SourceBaselineVerificationSummary");
                            var existingScalar = await readOldCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                            if (existingScalar is string existingText && !string.IsNullOrWhiteSpace(existingText))
                            {
                                previousSummaryJson = existingText;
                            }
                        }

                        await using (var settingCmd = connection.CreateCommand())
                        {
                            settingCmd.Transaction = transaction;
                            settingCmd.CommandText = """
                                INSERT INTO AppSettings (SettingKey, SettingValue, UpdatedAtUtc)
                                VALUES ($key, $val, $updatedAt)
                                ON CONFLICT(SettingKey) DO UPDATE SET
                                    SettingValue = excluded.SettingValue,
                                    UpdatedAtUtc = excluded.UpdatedAtUtc;
                                """;
                            settingCmd.Parameters.AddWithValue("$key", "SourceBaselineVerificationSummary");
                            settingCmd.Parameters.AddWithValue("$val", baselineSummaryJson);
                            settingCmd.Parameters.AddWithValue("$updatedAt", _clock.UtcNow.ToUniversalTime().ToString("O"));
                            await settingCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        }

                    }

                    return await SqliteAuditEventWriter.AppendAsync(
                        connection: connection,
                        transaction: transaction,
                        operationCode: "SOURCE_BASELINE_INVENTORIED",
                        operationArabicName: "جرد المستودع والتحقق من خط الأساس",
                        actorUserId: actorUserId,
                        actorPersonId: actorPersonId,
                        actorUsernameSnapshot: actorUsernameSnapshot,
                        actorRosterFullNameSnapshot: actorRosterFullNameSnapshot,
                        targetEntityType: "AppSettings",
                        targetEntityId: "SourceBaselineVerificationSummary",
                        targetDisplaySnapshot: "ملخص بصمات ملفات خط الأساس وفحص المراجع",
                        oldValueJson: previousSummaryJson,
                        newValueJson: baselineSummaryJson,
                        defaultArabicDescription: "تم التحقق من بصمات ملفات خط الأساس وخلو المستودع من المضيف التنفيذي والحسابات المسبقة والمراجع المكسورة.",
                        reason: "تثبيت خط الأساس للمرحلة التأسيسية خارج مستودع الكود الإنتاجي",
                        effectiveLocalDate: _clock.CurrentBusinessDate,
                        executedAtUtc: _clock.UtcNow,
                        correlationId: correlationId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

            return ServiceResult<AuditEvent>.Ok(auditEvent);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة حالياً وتعذر حفظ ملخص خط الأساس.",
                ex.Message);
        }
        catch (Exception ex)
        {
            return ServiceResult<AuditEvent>.Fail(
                BridgeErrorCodes.ValidationError,
                "تعذر حفظ ملخص خط الأساس في قاعدة البيانات.",
                ex.Message);
        }
    }

    public async Task<ServiceResult<string?>> GetPersistedBaselineSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = _dbContext.CreateOpenConnection();
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT SettingValue FROM AppSettings WHERE SettingKey = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$key", "SourceBaselineVerificationSummary");
            var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            var text = result is null or DBNull ? null : Convert.ToString(result);
            return ServiceResult<string?>.Ok(text ?? string.Empty);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<string?>.Fail(
                BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة حالياً وتعذر قراءة ملخص خط الأساس.",
                ex.Message);
        }
        catch (Exception ex)
        {
            return ServiceResult<string?>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر قراءة ملخص خط الأساس من قاعدة البيانات.",
                ex.Message);
        }
    }

    /// <summary>Check whether a transaction's correlation was already committed.
    /// Prevents replayed requests from overwriting the persisted settings without a matching new audit event.</summary>
    private static async Task<bool> HasExistingAuditCorrelationAsync(
        SqliteConnection connection, SqliteTransaction transaction, string operationCode,
        string correlationId, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM AuditEvents WHERE OperationCode = $op AND CorrelationId = $cor LIMIT 1);";
        cmd.Parameters.AddWithValue("$op", operationCode);
        cmd.Parameters.AddWithValue("$cor", correlationId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static async Task<string?> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToString(result);
    }

    private static async Task<int> ExecuteScalarIntAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    private static async Task<IReadOnlyList<string>> QueryNamesFromMasterAsync(
        SqliteConnection connection,
        string masterType,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = $type AND name NOT LIKE 'sqlite_%' ORDER BY name;";
        cmd.Parameters.AddWithValue("$type", masterType);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            names.Add(reader.GetString(0));
        }
        return names;
    }
}
