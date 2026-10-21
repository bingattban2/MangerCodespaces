using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlTamam.Application.Attendance;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.ValueObjects;
using AlTamam.Domain.Policies;
using AlTamam.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteAttendanceStore(AlTamamDbContext database, ISystemClock clock) : IAttendanceStore
{
    private readonly SqliteAuthenticationStore _sessions = new(database, clock);
    private string Day => clock.CurrentBusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string Now => clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private sealed record Receipt<T>(string Request, T Result);
    private sealed record Mutation<T>(T Result, object? Before = null);
    private Task<ServiceResult<T>> ReadAsync<T>(string nonce, Func<SqliteConnection, SqliteTransaction, AccountDto, Task<T>> read, CancellationToken token) => BoundaryAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        using var c = database.CreateOpenConnection();
        using var t = c.BeginTransaction(deferred: true);
        var actor = await _sessions.AuthorizeAsync(c, t, nonce).ConfigureAwait(false);
        var value = await read(c, t, actor).ConfigureAwait(false);
        t.Commit(); return ServiceResult<T>.Ok(value);
    });
    private Task<ServiceResult<T>> WriteAsync<T>(string nonce, string requestId, string operation, object input,
        Func<SqliteConnection, SqliteTransaction, AccountDto, Task<Mutation<T>>> write, CancellationToken token) => BoundaryAsync(async () =>
    {
        Require(Guid.TryParseExact(requestId, "D", out _), "معرف الطلب غير صحيح.");
        var serialized = JsonSerializer.Serialize(input);
        return await database.ExecuteInWriteTransactionAsync(async (c, t) =>
        {
            var actor = await _sessions.AuthorizeAsync(c, t, nonce).ConfigureAwait(false);
            await ClockGuard.ValidateAsync(c, t, clock.UtcNow, advance: true).ConfigureAwait(false);
            var prior = await SqliteOperations.QueryAsync(c, t, "SELECT OperationCode,ActorUserId,NewValueJson FROM AuditEvents WHERE CorrelationId=$id",
                r => (Operation: r.GetString(0), Actor: r.GetString(1), Json: r.GetString(2)), ("$id", requestId)).ConfigureAwait(false);
            if (prior.Count > 0)
            {
                Require(prior.Count == 1 && prior[0].Operation == operation && prior[0].Actor == actor.UserId, "معرف الطلب مستخدم لعملية أخرى.", BridgeErrorCodes.ConflictStale);
                var receipt = JsonSerializer.Deserialize<Receipt<T>>(prior[0].Json);
                Require(receipt is not null && receipt.Request == serialized, "تغيرت بيانات الطلب المعتمد.", BridgeErrorCodes.ConflictStale);
                return ServiceResult<T>.Ok(receipt!.Result);
            }
            token.ThrowIfCancellationRequested();
            var mutation = await write(c, t, actor).ConfigureAwait(false);
            var result = mutation.Result;
            await SqliteAuditEventWriter.AppendAsync(c, t, operation, "التحضير اليومي", new UserId(actor.UserId),
                actor.LinkedPersonId is null ? null : new PersonId(actor.LinkedPersonId), actor.Username, actor.FullName,
                "Attendance", Day, "التحضير اليومي", mutation.Before is null ? null : JsonSerializer.Serialize(mutation.Before), JsonSerializer.Serialize(new Receipt<T>(serialized, result)),
                "عملية تحضير معتمدة", null, clock.CurrentBusinessDate, clock.UtcNow, requestId, token).ConfigureAwait(false);
            return ServiceResult<T>.Ok(result);
        }, token).ConfigureAwait(false);
    });
    private const string RosterSql = """
        SELECT p.PersonId,p.CurrentVisibleName,p.CurrentServiceNumberA,a.SiteId,sh.VisibleNameSnapshot,a.AssignmentId,p.RowVersion,
          ar.StatusId,ar.StatusNameSnapshot,ar.StatusSymbolSnapshot,ar.StatusColorSnapshot,COALESCE(ar.RevisionNumber,0),
          EXISTS(SELECT 1 FROM PersonnelMovements m WHERE m.PersonId=p.PersonId AND m.EffectiveLocalDate=$day) AS MovementExempt,
          yesterday.StatusNameSnapshot
        FROM Persons p JOIN PersonAssignments a ON a.PersonId=p.PersonId AND a.ValidFromLocalDate<=$day
          AND (a.ValidToLocalDate IS NULL OR a.ValidToLocalDate>$day)
        JOIN SiteNameHistory sh ON sh.SiteId=a.SiteId AND sh.ValidFromLocalDate<=$day AND (sh.ValidToLocalDate IS NULL OR sh.ValidToLocalDate>$day)
        LEFT JOIN AttendanceRecords ar ON ar.PersonId=p.PersonId AND ar.AttendanceLocalDate=$day
        LEFT JOIN AttendanceRecords yesterday ON yesterday.PersonId=p.PersonId AND yesterday.AttendanceLocalDate=$yesterday
        WHERE p.ActiveFlag=1 AND p.OrganizationalStatus=1
          AND ($site IS NULL OR a.SiteId=$site)
          AND (p.NormalizedName LIKE $search ESCAPE '\' OR p.NormalizedServiceNumberA LIKE $number ESCAPE '\')
          AND ($status IS NULL OR ar.StatusId=$status)
          AND ($unprepared=0 OR (ar.RecordId IS NULL AND NOT EXISTS(SELECT 1 FROM PersonnelMovements m WHERE m.PersonId=p.PersonId AND m.EffectiveLocalDate=$day)))
        """;
    private static DailyRosterRow Row(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6),
        r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.GetInt64(11), r.GetInt32(12) != 0, r.IsDBNull(13) ? null : r.GetString(13));
    private (string, object?)[] Parameters(DailyRosterQuery query)
    {
        Require(query.Search.Length <= 100 && query.Offset >= 0 && query.Limit is >= 1 and <= 100 && (query.SiteId?.Length ?? 0) <= 100 && (query.StatusId?.Length ?? 0) <= 100, "معايير البحث غير صحيحة.");
        static string Escape(string text) => "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        return [("$day", Day), ("$yesterday", clock.CurrentBusinessDate.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$site", query.SiteId), ("$status", query.StatusId), ("$unprepared", query.UnpreparedOnly ? 1 : 0),
            ("$search", Escape(ArabicTextNormalizer.NormalizeForSearch(query.Search))), ("$number", Escape(ArabicTextNormalizer.NormalizeDigitsOnly(query.Search))),
            ("$limit", query.Limit), ("$offset", query.Offset)];
    }
    public Task<ServiceResult<DailyRosterPage>> QueryAsync(string nonce, DailyRosterQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, t, _) =>
    {
        var p = Parameters(query);
        var count = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM (" + RosterSql + ")", p).ConfigureAwait(false));
        var rows = await SqliteOperations.QueryAsync(c, t, RosterSql + " ORDER BY p.NormalizedName,p.PersonId LIMIT $limit OFFSET $offset", Row, p).ConfigureAwait(false);
        return new DailyRosterPage(Day, rows, count);
    }, token);
    public Task<ServiceResult<DashboardSummary>> SummaryAsync(string nonce, DailyRosterQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, t, _) =>
    {
        // Status filters affect the roster; summary retains the full eligible denominator within search/site.
        var p = Parameters(query with { StatusId = null, UnpreparedOnly = false });
        var counts = await SqliteOperations.QueryAsync(c, t, "SELECT COUNT(*),COALESCE(SUM(MovementExempt),0),COALESCE(SUM(CASE WHEN MovementExempt=0 AND StatusId IS NOT NULL THEN 1 ELSE 0 END),0) FROM (" + RosterSql + ")", r => (Total: r.GetInt64(0), Exempt: r.GetInt64(1), Prepared: r.GetInt64(2)), p).ConfigureAwait(false);
        var statuses = await SqliteOperations.QueryAsync(c, t, "SELECT StatusId,StatusNameSnapshot,COUNT(*) FROM (" + RosterSql + ") WHERE MovementExempt=0 AND StatusId IS NOT NULL GROUP BY StatusId,StatusNameSnapshot ORDER BY StatusNameSnapshot", r => new AttendanceCounter(r.GetString(0), r.GetString(1), r.GetInt64(2)), p).ConfigureAwait(false);
        var sites = await SqliteOperations.QueryAsync(c, t, "SELECT SiteId,VisibleNameSnapshot,COUNT(*) FROM (" + RosterSql + ") GROUP BY SiteId,VisibleNameSnapshot ORDER BY VisibleNameSnapshot", r => new AttendanceCounter(r.GetString(0), r.GetString(1), r.GetInt64(2)), p).ConfigureAwait(false);
        var n = counts[0]; return new DashboardSummary(Day, n.Total, n.Total - n.Exempt, n.Exempt, n.Prepared, n.Total - n.Exempt - n.Prepared, statuses, sites);
    }, token);
    private async Task EnsureOpenAsync(SqliteConnection c, SqliteTransaction t, string day)
    {
        Require(day == Day, "التحضير العادي متاح ليوم العمل الحالي فقط.", BridgeErrorCodes.HistoricDayLocked);
        Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PeriodClosures WHERE IsClosed=1 AND (PeriodKey=$day OR PeriodKey=$month)", ("$day", day), ("$month", day[..7])).ConfigureAwait(false)) == 0, "فترة التحضير مغلقة.", BridgeErrorCodes.HistoricDayLocked);
    }
    private async Task<List<DailyRosterRow>> SelectionAsync(SqliteConnection c, SqliteTransaction t, AccountDto actor, AttendanceSelection selection)
    {
        Require(selection.PersonIds is { Count: <= 500 } && selection.PersonIds.All(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 100), "اختيار الأفراد غير صحيح.");
        IReadOnlyList<string> ids = selection.PersonIds;
        if (selection.DraftId is not null)
        {
            Require(!selection.AllMatching && ids.Count == 0, "اختر مسودة أو أفراداً، دون جمع الاختيارين.");
            var drafts = await SqliteOperations.QueryAsync(c, t, "SELECT BusinessDate,SelectedPersonIdsJson FROM AttendanceDrafts WHERE DraftId=$id AND CreatedByUserId=$actor", r => (Day: r.GetString(0), Ids: r.GetString(1)), ("$id", selection.DraftId), ("$actor", actor.UserId)).ConfigureAwait(false);
            Require(drafts.Count == 1 && drafts[0].Day == Day, "المسودة غير موجودة أو تخص يوماً سابقاً.", BridgeErrorCodes.ConflictStale);
            ids = JsonSerializer.Deserialize<string[]>(drafts[0].Ids) ?? [];
        }
        Require(!selection.AllMatching || (ids.Count == 0 && selection.DraftId is null), "اختيار الكل لا يقبل قائمة منفصلة.");
        Require(selection.AllMatching || (ids.Count is >= 1 and <= 10000 && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count), "اختر أفراداً دون تكرار.");
        var parameters = Parameters(selection.AllMatching ? selection.Filter : new());
        // Explicit selections use JSON1 to remain bounded without thousands of SQL parameters.
        var sql = RosterSql + (selection.AllMatching ? "" : " AND p.PersonId IN (SELECT value FROM json_each($ids))") + " ORDER BY p.PersonId LIMIT 10001";
        if (!selection.AllMatching) parameters = [.. parameters, ("$ids", JsonSerializer.Serialize(ids))];
        var rows = await SqliteOperations.QueryAsync(c, t, sql, Row, parameters).ConfigureAwait(false);
        Require(rows.Count is >= 1 and <= 10000 && (selection.AllMatching || rows.Count == ids.Count), "تغيرت القوة المختارة أو تجاوز الاختيار عشرة آلاف فرد؛ حدّث الكشف.", BridgeErrorCodes.ConflictStale);
        Require(rows.All(x => !x.MovementExempt), "لا يسمح بالتحضير في يوم النقل أو الفصل.", BridgeErrorCodes.ConflictStale);
        return rows;
    }
    private sealed record TargetStatus(string Id, string Name, string Symbol, string Color, long Version);
    private async Task<(AttendancePreview Preview, List<DailyRosterRow> Rows, TargetStatus Status)> PrepareAsync(SqliteConnection c, SqliteTransaction t, AccountDto actor, AttendanceChange change)
    {
        await EnsureOpenAsync(c, t, change.BusinessDate).ConfigureAwait(false);
        Require(change.StatusId.Length is > 0 and <= 100 && change.Reason.Length <= 500 && !change.Reason.Any(char.IsControl), "الحالة أو سبب التعديل غير صحيح.");
        Require(!change.Revise || !string.IsNullOrWhiteSpace(change.Reason), "سبب تعديل التحضير مطلوب.");
        var status = await SqliteOperations.QueryAsync(c, t, """
            SELECT s.StatusId,h.VisibleNameSnapshot,h.ShortSymbolSnapshot,h.ColorSnapshot,s.RowVersion
            FROM AttendanceStatusDefinitions s JOIN AttendanceStatusNameHistory h ON h.StatusId=s.StatusId
            WHERE s.StatusId=$id AND s.ActiveFlag=1 AND h.ValidFromLocalDate<=$day AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$day)
            """, r => new TargetStatus(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4)), ("$id", change.StatusId), ("$day", Day)).ConfigureAwait(false);
        Require(status.Count == 1, "اختر حالة فعالة.");
        var rows = await SelectionAsync(c, t, actor, change.Selection).ConfigureAwait(false);
        Require(rows.All(x => change.Revise ? x.StatusId is not null && x.StatusId != change.StatusId : x.StatusId is null), change.Revise ? "اختر أفراداً محضرين بحالة مختلفة." : "تضم المجموعة أفراداً محضرين؛ استخدم تعديل تحضير اليوم.", BridgeErrorCodes.ConflictStale);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Day, actor.UserId, Change = change, Status = status[0], Rows = rows }))));
        return (new AttendancePreview(Day, hash, rows.Count, status[0].Name, rows.Take(100).ToArray()), rows, status[0]);
    }
    public Task<ServiceResult<AttendancePreview>> PreviewAsync(string nonce, AttendanceChange change, CancellationToken token = default) => ReadAsync(nonce, async (c, t, actor) => (await PrepareAsync(c, t, actor, change).ConfigureAwait(false)).Preview, token);
    public Task<ServiceResult<AttendanceCommitResult>> CommitAsync(string nonce, string requestId, CommitAttendanceCommand command, CancellationToken token = default) =>
        WriteAsync<AttendanceCommitResult>(nonce, requestId, command.Change.Revise ? "ATTENDANCE_REVISED" : "ATTENDANCE_COMMITTED", command, async (c, t, actor) =>
        {
            var prepared = await PrepareAsync(c, t, actor, command.Change).ConfigureAwait(false);
            Require(prepared.Preview.PreviewHash == command.PreviewHash, "تغيرت البيانات بعد المعاينة؛ أعد المعاينة قبل الاعتماد.", BridgeErrorCodes.ConflictStale);
            foreach (var row in prepared.Rows)
            {
                token.ThrowIfCancellationRequested();
                Require(Day == command.Change.BusinessDate, "تغير يوم العمل؛ أعد المعاينة.", BridgeErrorCodes.ConflictStale);
                var status = prepared.Status;
                if (command.Change.Revise)
                {
                    await ExecuteAsync(c, t, """
                        INSERT INTO AttendanceRevisions(PersonId,TargetLocalDate,BeforeStatusId,BeforeStatusNameSnapshot,AfterStatusId,AfterStatusNameSnapshot,Reason,ActorUserId,ExecutedAtUtc,AuthorizationMethod)
                        VALUES($person,$day,$old,$oldName,$status,$name,$reason,$actor,$now,'authenticated-current-day');
                        UPDATE AttendanceRecords SET StatusId=$status,StatusNameSnapshot=$name,StatusSymbolSnapshot=$symbol,StatusColorSnapshot=$color,RevisionNumber=RevisionNumber+1
                        WHERE PersonId=$person AND AttendanceLocalDate=$day AND RevisionNumber=$revision;
                        """, ("$person", row.PersonId), ("$day", Day), ("$old", row.StatusId), ("$oldName", row.StatusName), ("$status", status.Id), ("$name", status.Name), ("$symbol", status.Symbol), ("$color", status.Color), ("$reason", command.Change.Reason.Trim()), ("$actor", actor.UserId), ("$now", Now), ("$revision", row.Revision)).ConfigureAwait(false);
                }
                else
                {
                    await ExecuteAsync(c, t, """
                        INSERT INTO AttendanceRecords(PersonId,AttendanceLocalDate,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,StatusColorSnapshot,AssignmentIdAtEffectiveDate,SiteIdAtEffectiveDate,SiteNameSnapshot,Source,CreatedByUserId,CommittedAtUtc,IdempotencyKey)
                        VALUES($person,$day,$status,$name,$symbol,$color,$assignment,$site,$siteName,$source,$actor,$now,$key)
                        """, ("$person", row.PersonId), ("$day", Day), ("$status", status.Id), ("$name", status.Name), ("$symbol", status.Symbol), ("$color", status.Color), ("$assignment", row.AssignmentId), ("$site", row.SiteId), ("$siteName", row.SiteName), ("$source", prepared.Rows.Count == 1 ? "manual" : "bulk"), ("$actor", actor.UserId), ("$now", Now), ("$key", requestId + ":" + row.PersonId)).ConfigureAwait(false);
                }
            }
            if (command.Change.Selection.DraftId is not null)
                await ExecuteAsync(c, t, "DELETE FROM AttendanceDrafts WHERE DraftId=$id AND CreatedByUserId=$actor", ("$id", command.Change.Selection.DraftId), ("$actor", actor.UserId)).ConfigureAwait(false);
            Require(Day == command.Change.BusinessDate, "تغير يوم العمل؛ أعد المعاينة.", BridgeErrorCodes.ConflictStale);
            return new Mutation<AttendanceCommitResult>(new(Day, prepared.Rows.Count, command.Change.Revise), prepared.Rows);
        }, token);
}
