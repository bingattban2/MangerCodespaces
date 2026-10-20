using System.Globalization;
using System.Text.Json;
using AlTamam.Application.Identity;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;
using AlTamam.Domain.ValueObjects;
using Microsoft.Data.Sqlite;

namespace AlTamam.Infrastructure.Persistence;

/// <summary>
/// SQLCipher-backed, session-checked as-of identities. Existing history rows are
/// never rewritten; a later ValidFrom supersedes the earlier snapshot for lookup.
/// All identity updates and immutable audit append are a single SQL transaction.
/// </summary>
public sealed class SqlitePersonIdentityStore : IPersonIdentityStore
{
    private readonly AlTamamDbContext _db;
    private readonly ISystemClock _clock;

    public SqlitePersonIdentityStore(AlTamamDbContext db, ISystemClock clock)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    private sealed record Actor(UserId Id, string Username, PersonId? RosterId, string? RosterName);

    private async Task<Actor?> GetAuthorizedActorAsync(
        SqliteConnection connection, SqliteTransaction? transaction, string sessionNonce,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT u.UserId, u.Username, u.Role, u.ActiveFlag, u.RequiresFirstRunSetup,
                   u.LinkedPersonId, p.CurrentVisibleName, s.Role, s.RevokedFlag,
                   s.IssuedAtUtc, s.ExpiresAtUtc
            FROM UserSessions s
            JOIN Users u ON u.UserId = s.UserId
            LEFT JOIN Persons p ON p.PersonId = u.LinkedPersonId
            WHERE s.SessionNonce = $nonce LIMIT 1;
            """;
        command.Parameters.AddWithValue("$nonce", sessionNonce);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        var role = (UserRole)reader.GetInt32(2);
        if (reader.GetInt32(3) != 1 || reader.GetInt32(4) != 0 ||
            reader.GetInt32(7) != (int)role || reader.GetInt32(8) != 0 ||
            !RoleAuthorizationPolicy.CanPerformStandardOperations(role))
            return null;
        var now = _clock.UtcNow;
        if (!DateTimeOffset.TryParse(reader.GetString(9), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var issued) ||
            !DateTimeOffset.TryParse(reader.GetString(10), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var expires) ||
            now < issued || now >= expires)
            return null;
        return new Actor(new UserId(reader.GetString(0)), reader.GetString(1),
            reader.IsDBNull(5) ? null : new PersonId(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6));
    }

    private static string ToIso(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static async Task<PersonHistoricIdentityDto?> LoadIdentityAsync(
        SqliteConnection connection, SqliteTransaction? transaction,
        PersonId personId, DateOnly businessDate, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT h.ValidFromLocalDate, h.NameSnapshot, h.ServiceNumberASnapshot,
                   COALESCE(
                       (SELECT MIN(n.ValidFromLocalDate) FROM PersonIdentityHistory n
                        WHERE n.PersonId = h.PersonId
                          AND n.ValidFromLocalDate > h.ValidFromLocalDate
                          AND (h.ValidToLocalDate IS NULL OR
                               n.ValidFromLocalDate < h.ValidToLocalDate)),
                       h.ValidToLocalDate
                   ) AS NextDate
            FROM PersonIdentityHistory h
            WHERE h.PersonId = $person AND h.ValidFromLocalDate <= $day
              AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate > $day)
            ORDER BY h.ValidFromLocalDate DESC, h.HistoryId DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$person", personId.Value);
        command.Parameters.AddWithValue("$day", ToIso(businessDate));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return new PersonHistoricIdentityDto(personId.Value, reader.GetString(0),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(1), reader.GetString(2));
    }

    public async Task<ServiceResult<PersonHistoricIdentityDto>> ReadAsOfAsync(
        PersonId personId, DateOnly businessDate, string sessionNonce,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId.Value) || string.IsNullOrWhiteSpace(sessionNonce))
            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.ValidationError, "معرف الفرد والجلسة مطلوبان.");
        try
        {
            using var connection = _db.CreateOpenConnection();
            if (await GetAuthorizedActorAsync(connection, null, sessionNonce, cancellationToken).ConfigureAwait(false) is null)
                return ServiceResult<PersonHistoricIdentityDto>.Fail(
                    BridgeErrorCodes.Unauthorized, "الجلسة غير صالحة أو انتهت صلاحيتها.");
            var value = await LoadIdentityAsync(connection, null, personId, businessDate, cancellationToken).ConfigureAwait(false);
            return value is null
                ? ServiceResult<PersonHistoricIdentityDto>.Fail(
                    BridgeErrorCodes.MissingData, "لا يوجد سجل هوية نافذ لهذا التاريخ.")
                : ServiceResult<PersonHistoricIdentityDto>.Ok(value);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة، حاول مجدداً.", ex.Message);
        }
        catch (SqliteException ex)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر قراءة هوية الفرد التاريخية من قاعدة البيانات المشفرة.", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر فتح بيانات هوية الفرد التاريخية.", ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>> ReadMonthlyHistoryAsync(
        PersonId personId, int year, int month, string sessionNonce,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(personId.Value) || string.IsNullOrWhiteSpace(sessionNonce) ||
            year is < 1 or > 9999 || month is < 1 or > 12)
            return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                BridgeErrorCodes.ValidationError, "بيانات استعلام التمام الشهري غير صحيحة.");

        try
        {
            using var connection = _db.CreateOpenConnection();
            if (await GetAuthorizedActorAsync(connection, null, sessionNonce, cancellationToken).ConfigureAwait(false) is null)
                return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                    BridgeErrorCodes.Unauthorized, "الجلسة غير صالحة أو انتهت صلاحيتها.");

            var first = new DateOnly(year, month, 1);
            var last = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT a.PersonId, h.NameSnapshot, h.ServiceNumberASnapshot,
                       a.AttendanceLocalDate, a.StatusId, a.StatusNameSnapshot,
                       a.StatusSymbolSnapshot, a.StatusColorSnapshot,
                       a.AssignmentIdAtEffectiveDate, a.SiteIdAtEffectiveDate,
                       a.SiteNameSnapshot, p.SourceMovementId, a.RevisionNumber
                FROM AttendanceRecords a
                JOIN PersonAssignments p ON p.AssignmentId = a.AssignmentIdAtEffectiveDate
                LEFT JOIN PersonIdentityHistory h ON h.HistoryId = (
                    SELECT history.HistoryId FROM PersonIdentityHistory history
                    WHERE history.PersonId = a.PersonId
                      AND history.ValidFromLocalDate <= a.AttendanceLocalDate
                      AND (history.ValidToLocalDate IS NULL OR
                           history.ValidToLocalDate > a.AttendanceLocalDate)
                    ORDER BY history.ValidFromLocalDate DESC, history.HistoryId DESC
                    LIMIT 1
                )
                WHERE a.PersonId = $person AND a.AttendanceLocalDate >= $from
                  AND a.AttendanceLocalDate <= $through
                ORDER BY a.AttendanceLocalDate;
                """;
            command.Parameters.AddWithValue("$person", personId.Value);
            command.Parameters.AddWithValue("$from", ToIso(first));
            command.Parameters.AddWithValue("$through", ToIso(last));
            var days = new List<HistoricAttendanceDayDto>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.IsDBNull(1) || reader.IsDBNull(2))
                    return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                        BridgeErrorCodes.MissingData,
                        "تاريخ هوية فرد في سجل التمام غير مكتمل، ولا يمكن إظهار اسم حالي بديل.");
                days.Add(new HistoricAttendanceDayDto(
                    PersonId: reader.GetString(0),
                    PersonNameSnapshot: reader.GetString(1),
                    ServiceNumberASnapshot: reader.GetString(2),
                    BusinessDate: reader.GetString(3),
                    StatusId: reader.GetString(4),
                    StatusNameSnapshot: reader.GetString(5),
                    StatusSymbolSnapshot: reader.GetString(6),
                    StatusColorSnapshot: reader.GetString(7),
                    AssignmentIdAtEffectiveDate: reader.GetString(8),
                    SiteIdAtEffectiveDate: reader.GetString(9),
                    SiteNameSnapshot: reader.GetString(10),
                    SourceMovementId: reader.IsDBNull(11) ? null : reader.GetString(11),
                    RevisionNumber: reader.GetInt32(12)));
            }
            return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Ok(days);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                BridgeErrorCodes.StorageBusy, "قاعدة البيانات مشغولة، حاول مجدداً.", ex.Message);
        }
        catch (SqliteException ex)
        {
            return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed, "تعذر قراءة سجل التمام التاريخي من قاعدة البيانات المشفرة.", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<IReadOnlyList<HistoricAttendanceDayDto>>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed, "تعذر فتح بيانات التمام التاريخية.", ex.Message);
        }
    }

    public async Task<ServiceResult<PersonHistoricIdentityDto>> ChangeAsync(
        PersonIdentityChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (change.EffectiveLocalDate != _clock.CurrentBusinessDate ||
            string.IsNullOrWhiteSpace(change.PersonId.Value) ||
            string.IsNullOrWhiteSpace(change.VisibleName) ||
            string.IsNullOrWhiteSpace(change.ServiceNumberA) ||
            string.IsNullOrWhiteSpace(change.Reason) ||
            string.IsNullOrWhiteSpace(change.RequestId) ||
            string.IsNullOrWhiteSpace(change.SessionNonce) ||
            change.ExpectedRowVersion < 1)
            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.ValidationError, "بيانات تغيير الهوية غير مكتملة أو تاريخ سريانها غير مسموح.");
        try
        {
            // Auth, optimistic concurrency, immutable snapshot insert, and audit share ONE transaction.
            return await _db.ExecuteInWriteTransactionAsync(async (connection, transaction) =>
            {
                var actor = await GetAuthorizedActorAsync(connection, transaction,
                    change.SessionNonce, cancellationToken).ConfigureAwait(false);
                if (actor is null)
                    return ServiceResult<PersonHistoricIdentityDto>.Fail(
                        BridgeErrorCodes.Unauthorized, "لا تملك جلسة فعّالة لتنفيذ التعديل.");

                // Retain requestId as an immutable audit correlation; mismatched replays must fail.
                var proposedJson = JsonSerializer.Serialize(new
                {
                    name = change.VisibleName,
                    number = change.ServiceNumberA,
                    effectiveDate = ToIso(change.EffectiveLocalDate)
                });
                await using (var replay = connection.CreateCommand())
                {
                    replay.Transaction = transaction;
                    replay.CommandText = """
                        SELECT OperationCode, ActorUserId, TargetEntityId, NewValueJson, Reason
                        FROM AuditEvents WHERE CorrelationId = $id LIMIT 1;
                        """;
                    replay.Parameters.AddWithValue("$id", change.RequestId);
                    await using var existing = await replay.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (await existing.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (existing.GetString(0) != "PERSON_IDENTITY_UPDATED" ||
                            existing.GetString(1) != actor.Id.Value ||
                            existing.GetString(2) != change.PersonId.Value ||
                            existing.IsDBNull(3) || existing.GetString(3) != proposedJson ||
                            existing.IsDBNull(4) || existing.GetString(4) != change.Reason)
                            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                                BridgeErrorCodes.ConflictStale, "معرف الطلب مستخدم لعملية مختلفة.");
                        var original = await LoadIdentityAsync(connection, transaction,
                            change.PersonId, change.EffectiveLocalDate, cancellationToken).ConfigureAwait(false);
                        return original is null
                            ? ServiceResult<PersonHistoricIdentityDto>.Fail(
                                BridgeErrorCodes.MissingData, "سجل الهوية الأصلي غير موجود.")
                            : ServiceResult<PersonHistoricIdentityDto>.Ok(original);
                    }
                }

                string previousName;
                string previousNumber;
                long rowVersion;
                await using (var person = connection.CreateCommand())
                {
                    person.Transaction = transaction;
                    person.CommandText = """
                        SELECT CurrentVisibleName, CurrentServiceNumberA, RowVersion
                        FROM Persons WHERE PersonId = $person AND ActiveFlag = 1 LIMIT 1;
                        """;
                    person.Parameters.AddWithValue("$person", change.PersonId.Value);
                    await using var found = await person.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (!await found.ReadAsync(cancellationToken).ConfigureAwait(false))
                        return ServiceResult<PersonHistoricIdentityDto>.Fail(
                            BridgeErrorCodes.MissingData, "الفرد غير موجود في القوة الفعالة.");
                    previousName = found.GetString(0);
                    previousNumber = found.GetString(1);
                    rowVersion = found.GetInt64(2);
                }
                if (rowVersion != change.ExpectedRowVersion)
                    return ServiceResult<PersonHistoricIdentityDto>.Fail(
                        BridgeErrorCodes.ConflictStale, "تغيرت بيانات الفرد منذ فتحها. حدّث البيانات أولاً.");
                if (previousName == change.VisibleName && previousNumber == change.ServiceNumberA)
                    return ServiceResult<PersonHistoricIdentityDto>.Fail(
                        BridgeErrorCodes.ValidationError, "لم تتغير بيانات الاسم أو الرقم (ع).");

                // A pre-existing baseline snapshot must have been created when the person was enrolled.
                // Reject a missing baseline rather than retroactively inventing yesterday's identity.
                await using (var boundary = connection.CreateCommand())
                {
                    boundary.Transaction = transaction;
                    boundary.CommandText = """
                        SELECT COUNT(1),
                               MAX(ValidFromLocalDate),
                               (SELECT last.ValidToLocalDate FROM PersonIdentityHistory last
                                WHERE last.PersonId=$person
                                ORDER BY last.ValidFromLocalDate DESC, last.HistoryId DESC
                                LIMIT 1)
                        FROM PersonIdentityHistory WHERE PersonId = $person;
                        """;
                    boundary.Parameters.AddWithValue("$person", change.PersonId.Value);
                    await using var reader = await boundary.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (reader.GetInt64(0) == 0 || reader.IsDBNull(1))
                        return ServiceResult<PersonHistoricIdentityDto>.Fail(
                            BridgeErrorCodes.MissingData, "سجل الهوية التأسيسي مفقود؛ لا يمكن تغيير التاريخ بأمان.");
                    if (string.CompareOrdinal(reader.GetString(1), ToIso(change.EffectiveLocalDate)) >= 0)
                        return ServiceResult<PersonHistoricIdentityDto>.Fail(
                            BridgeErrorCodes.ConflictStale, "توجد هوية مسجلة في نفس اليوم أو بتاريخ أحدث.");
                    // The most recent interval may end exactly on the new start date.
                    // Reject a gap; never silently extend or relabel historical identity.
                    if (!reader.IsDBNull(2) &&
                        string.CompareOrdinal(reader.GetString(2), ToIso(change.EffectiveLocalDate)) < 0)
                        return ServiceResult<PersonHistoricIdentityDto>.Fail(
                            BridgeErrorCodes.ConflictStale, "انتهت فترة الهوية السابقة قبل تاريخ التغيير؛ لا يمكن تجاوز الفجوة تاريخياً.");
                }

                await using (var update = connection.CreateCommand())
                {
                    update.Transaction = transaction;
                    update.CommandText = """
                        UPDATE Persons SET CurrentVisibleName = $name, NormalizedName = $normalizedName,
                            CurrentServiceNumberA = $number, NormalizedServiceNumberA = $normalizedNumber,
                            RowVersion = RowVersion + 1
                        WHERE PersonId = $person AND RowVersion = $expected AND ActiveFlag = 1;
                        """;
                    update.Parameters.AddWithValue("$name", change.VisibleName);
                    update.Parameters.AddWithValue("$normalizedName", ArabicTextNormalizer.NormalizeForSearch(change.VisibleName));
                    update.Parameters.AddWithValue("$number", change.ServiceNumberA);
                    update.Parameters.AddWithValue("$normalizedNumber", ArabicTextNormalizer.NormalizeDigitsOnly(change.ServiceNumberA));
                    update.Parameters.AddWithValue("$person", change.PersonId.Value);
                    update.Parameters.AddWithValue("$expected", change.ExpectedRowVersion);
                    if (await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                        return ServiceResult<PersonHistoricIdentityDto>.Fail(
                            BridgeErrorCodes.ConflictStale, "تغيرت بيانات الفرد أثناء التعديل.");
                }
                // Close only the latest actual effective interval. This date-only
                // boundary is exclusive: [oldStart, newStart), [newStart, NULL).
                // Person update, interval closure, new snapshot and audit all roll
                // back together if any storage constraint or audit append fails.
                await using (var close = connection.CreateCommand())
                {
                    close.Transaction = transaction;
                    close.CommandText = """
                        UPDATE PersonIdentityHistory
                           SET ValidToLocalDate=$effective
                         WHERE HistoryId=(
                           SELECT last.HistoryId FROM PersonIdentityHistory last
                           WHERE last.PersonId=$person
                           ORDER BY last.ValidFromLocalDate DESC, last.HistoryId DESC
                           LIMIT 1)
                           AND ValidFromLocalDate < $effective
                           AND (ValidToLocalDate IS NULL OR ValidToLocalDate >= $effective);
                        """;
                    close.Parameters.AddWithValue("$person", change.PersonId.Value);
                    close.Parameters.AddWithValue("$effective", ToIso(change.EffectiveLocalDate));
                    if (await close.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                        throw new InvalidOperationException(
                            "Failed to close the preceding identity interval atomically.");
                }
                await using (var history = connection.CreateCommand())
                {
                    history.Transaction = transaction;
                    history.CommandText = """
                        INSERT INTO PersonIdentityHistory
                            (PersonId, NameSnapshot, ServiceNumberASnapshot, ValidFromLocalDate,
                             ValidToLocalDate, ChangedByUserId, RecordedAtUtc, Reason)
                        VALUES ($person, $name, $number, $day, NULL, $actor, $now, $reason);
                        """;
                    history.Parameters.AddWithValue("$person", change.PersonId.Value);
                    history.Parameters.AddWithValue("$name", change.VisibleName);
                    history.Parameters.AddWithValue("$number", change.ServiceNumberA);
                    history.Parameters.AddWithValue("$day", ToIso(change.EffectiveLocalDate));
                    history.Parameters.AddWithValue("$actor", actor.Id.Value);
                    history.Parameters.AddWithValue("$now", _clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
                    history.Parameters.AddWithValue("$reason", change.Reason);
                    await history.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }

                await SqliteAuditEventWriter.AppendAsync(
                    connection, transaction,
                    "PERSON_IDENTITY_UPDATED", "تعديل اسم الفرد أو الرقم (ع)",
                    actor.Id, actor.RosterId, actor.Username, actor.RosterName,
                    "Persons", change.PersonId.Value, change.VisibleName,
                    JsonSerializer.Serialize(new { name = previousName, number = previousNumber }),
                    proposedJson, "تم تعديل بيانات الهوية مع الاحتفاظ بالسجل التاريخي.",
                    change.Reason, change.EffectiveLocalDate, _clock.UtcNow,
                    change.RequestId, cancellationToken).ConfigureAwait(false);

                var persisted = await LoadIdentityAsync(connection, transaction,
                    change.PersonId, change.EffectiveLocalDate, cancellationToken).ConfigureAwait(false);
                return persisted is null
                    ? throw new InvalidOperationException("Persisted identity unavailable in the committed transaction.")
                    : ServiceResult<PersonHistoricIdentityDto>.Ok(persisted, change.ExpectedRowVersion + 1);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(BridgeErrorCodes.StorageBusy,
                "قاعدة البيانات مشغولة، حاول مجدداً.", ex.Message);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 &&
            ex.SqliteExtendedErrorCode == 2067 &&
            ex.Message.Contains("Persons.NormalizedServiceNumberA", StringComparison.Ordinal))
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(BridgeErrorCodes.DuplicateServiceNumber,
                "الرقم (ع) مستخدم بالفعل في القوة الفعالة.", ex.Message);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(BridgeErrorCodes.ValidationError,
                "رفضت قاعدة البيانات تعديل الهوية بسبب قيد سلامة البيانات.", ex.Message);
        }
        catch (SqliteException ex)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر تنفيذ تعديل الهوية بسبب خطأ في قاعدة البيانات المشفرة.", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return ServiceResult<PersonHistoricIdentityDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "تعذر إتمام معاملة الهوية المشفرة.", ex.Message);
        }
    }
}
