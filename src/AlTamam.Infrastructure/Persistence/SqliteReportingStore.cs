using System.Globalization;
using System.Text.Json;
using AlTamam.Application.Reporting;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteRegistryStore : IReportingStore
{
    private async Task<string> ReportTokenAsync(SqliteConnection c, SqliteTransaction t, ReportQuery query)
    {
        var last = Convert.ToString(await ScalarAsync(c, t, "SELECT EventHash FROM AuditEvents ORDER BY rowid DESC LIMIT 1").ConfigureAwait(false)) ?? "";
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Last = last, Day, Query = query with { Offset = 0, Limit = 50 } }))));
    }
    private ReportQuery ValidateReport(ReportQuery query, bool person)
    {
        Require(query.Offset >= 0 && query.Limit is >= 1 and <= 100 && query.Search.Length <= 100 &&
            (query.SiteId is null || query.SiteId.Length is > 0 and <= 100) &&
            (query.PersonId is null || query.PersonId.Length is > 0 and <= 100) && (!person || query.PersonId is not null), "معايير التقرير غير صحيحة.");
        var from = query.FromDate ?? "0001-01-01"; var to = query.ToDate ?? Day;
        Require(DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) &&
            DateOnly.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last) && first <= last && last <= clock.CurrentBusinessDate,
            "اختر نطاق تواريخ صحيحاً لا يتجاوز اليوم.");
        return query with { FromDate = from, ToDate = to };
    }
    private static (string, object?)[] ReportParameters(ReportQuery q) =>
        [("$person", q.PersonId), ("$from", q.FromDate), ("$to", q.ToDate), ("$site", q.SiteId), ("$offset", q.Offset), ("$limit", q.Limit)];
    private const string ReportAttendanceFilter = " WHERE PersonId=$person AND AttendanceLocalDate BETWEEN $from AND $to AND ($site IS NULL OR SiteIdAtEffectiveDate=$site)";
    private const string ReportMovementFilter = " WHERE PersonId=$person AND EffectiveLocalDate BETWEEN $from AND $to AND MovementType=1 AND ($site IS NULL OR SourceSiteId=$site OR DestinationSiteId=$site)";
    private static ReportStatusDays ReportStatus(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3));
    private async Task<PersonReportSummary> PersonSummaryAsync(SqliteConnection c, SqliteTransaction t, ReportQuery q)
    {
        var p = ReportParameters(q);
        var identities = await QueryAsync(c, t, """
            SELECT i.NameSnapshot,i.ServiceNumberASnapshot,p.FirstRecordedLocalDate FROM Persons p
            JOIN PersonIdentityHistory i ON i.PersonId=p.PersonId AND i.ValidFromLocalDate<=$to
            AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>$to) WHERE p.PersonId=$person
            """, r => (Name: r.GetString(0), Number: r.GetString(1), Entry: r.GetString(2)), p).ConfigureAwait(false);
        Require(identities.Count == 1, "الفرد غير موجود في التاريخ المختار.", BridgeErrorCodes.MissingData);
        var statuses = await QueryAsync(c, t, "SELECT StatusId,StatusNameSnapshot,StatusSymbolSnapshot,COUNT(*) FROM AttendanceRecords" + ReportAttendanceFilter +
            " GROUP BY StatusId,StatusNameSnapshot,StatusSymbolSnapshot ORDER BY StatusId,StatusNameSnapshot,StatusSymbolSnapshot", ReportStatus, p).ConfigureAwait(false);
        var moves = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PersonnelMovements" + ReportMovementFilter, p).ConfigureAwait(false));
        var identity = identities[0];
        return new(q.PersonId!, identity.Name, identity.Number, identity.Entry, q.FromDate!, q.ToDate!, Now, statuses.Sum(s => s.Days), moves, statuses, await ReportTokenAsync(c, t, q).ConfigureAwait(false));
    }
    public Task<ServiceResult<PersonReportSummary>> PersonTotalsAsync(string nonce, ReportQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        var q = ValidateReport(query, true); using var t = c.BeginTransaction(deferred: true);
        var result = await PersonSummaryAsync(c, t, q).ConfigureAwait(false); t.Commit(); return result;
    }, token);
    public Task<ServiceResult<PersonLifetimeReport>> PersonReportAsync(string nonce, ReportQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        var q = ValidateReport(query, true); using var t = c.BeginTransaction(deferred: true);
        var summary = await PersonSummaryAsync(c, t, q).ConfigureAwait(false); var p = ReportParameters(q);
        const string filter = " WHERE PersonId=$person AND ValidFromLocalDate<=$to AND (ValidToLocalDate IS NULL OR ValidToLocalDate>$from)";
        var count = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PersonIdentityHistory" + filter, p).ConfigureAwait(false));
        var identities = await QueryAsync(c, t, "SELECT NameSnapshot,ServiceNumberASnapshot,ValidFromLocalDate,ValidToLocalDate,Reason FROM PersonIdentityHistory" + filter +
            " ORDER BY ValidFromLocalDate,HistoryId LIMIT $limit OFFSET $offset", r => new ReportIdentity(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4)), p).ConfigureAwait(false);
        t.Commit(); return new PersonLifetimeReport(summary, new(identities, count, q.Offset));
    }, token);
    public Task<ServiceResult<ReportPage<ReportAttendance>>> PersonAttendanceAsync(string nonce, ReportQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        var q = ValidateReport(query, true); using var t = c.BeginTransaction(deferred: true); await PersonSummaryAsync(c, t, q).ConfigureAwait(false); var p = ReportParameters(q);
        var count = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AttendanceRecords" + ReportAttendanceFilter, p).ConfigureAwait(false));
        var rows = await QueryAsync(c, t, """
            SELECT a.RecordId,a.AttendanceLocalDate,i.NameSnapshot,i.ServiceNumberASnapshot,a.SiteIdAtEffectiveDate,
            a.SiteNameSnapshot,a.StatusId,a.StatusNameSnapshot,a.StatusSymbolSnapshot,a.Source,a.RevisionNumber,a.CommittedAtUtc
            FROM AttendanceRecords a JOIN PersonIdentityHistory i ON i.PersonId=a.PersonId
            AND i.ValidFromLocalDate<=a.AttendanceLocalDate AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>a.AttendanceLocalDate)
            WHERE a.PersonId=$person AND a.AttendanceLocalDate BETWEEN $from AND $to AND ($site IS NULL OR a.SiteIdAtEffectiveDate=$site)
            ORDER BY a.AttendanceLocalDate DESC,a.RecordId LIMIT $limit OFFSET $offset
            """, r => new ReportAttendance(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetString(8), r.GetString(9), r.GetInt64(10), r.GetString(11)), p).ConfigureAwait(false);
        t.Commit(); return new ReportPage<ReportAttendance>(rows, count, q.Offset);
    }, token);
    // Exact structured PersonId references only. Descriptions and names are never used to associate an audit event.
    // Batch attendance receipts contain a serialized Request and do not always enumerate AllMatching results;
    // their immutable Before rows and matching committed record idempotency keys provide the remaining linkage.
    private const string PersonOperationFilter = """
        WHERE a.EffectiveLocalDate BETWEEN $from AND $to AND (
          (a.TargetEntityType='Persons' AND a.TargetEntityId=$person)
          OR EXISTS(SELECT 1 FROM json_tree(CASE WHEN json_valid(a.OldValueJson) THEN a.OldValueJson ELSE '{}' END) j WHERE j.key='PersonId' AND j.value=$person)
          OR EXISTS(SELECT 1 FROM json_tree(CASE WHEN json_valid(a.NewValueJson) THEN a.NewValueJson ELSE '{}' END) j WHERE j.key='PersonId' AND j.value=$person)
          OR EXISTS(SELECT 1 FROM json_tree(CASE WHEN json_valid(json_extract(CASE WHEN json_valid(a.NewValueJson) THEN a.NewValueJson ELSE '{}' END,'$.Request'))
             THEN json_extract(a.NewValueJson,'$.Request') ELSE '{}' END) j WHERE (j.key='PersonId' OR j.path LIKE '%.PersonIds') AND j.value=$person)
          OR EXISTS(SELECT 1 FROM AttendanceRecords ar WHERE ar.PersonId=$person AND ar.IdempotencyKey=a.CorrelationId||':'||$person)
          OR EXISTS(SELECT 1 FROM PersonnelMovements m WHERE m.PersonId=$person AND m.IdempotencyKey=a.CorrelationId||':'||$person))
        """;
    public Task<ServiceResult<ReportPage<ReportOperation>>> PersonOperationsAsync(string nonce, ReportQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        var q = ValidateReport(query, true); using var t = c.BeginTransaction(deferred: true); await PersonSummaryAsync(c, t, q).ConfigureAwait(false); var p = ReportParameters(q);
        var total = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AuditEvents a " + PersonOperationFilter, p).ConfigureAwait(false));
        var rows = await QueryAsync(c, t, "SELECT a.AuditEventId,a.EffectiveLocalDate,a.ExecutedAtUtc,a.OperationArabicName,a.ActorUsernameSnapshot,COALESCE(a.ActorRosterFullNameSnapshot,''),a.DefaultArabicDescription,COALESCE(a.Reason,'') FROM AuditEvents a " + PersonOperationFilter +
            " ORDER BY a.ExecutedAtUtc DESC,a.rowid DESC LIMIT $limit OFFSET $offset", r => new ReportOperation(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4) + (r.GetString(5).Length == 0 ? "" : " - " + r.GetString(5)), r.GetString(6), r.GetString(7)), p).ConfigureAwait(false);
        t.Commit(); return new ReportPage<ReportOperation>(rows, total, q.Offset);
    }, token);
    private const string ForcePeopleSql = """
        SELECT p.PersonId,i.NameSnapshot,i.ServiceNumberASnapshot FROM Persons p
        JOIN PersonIdentityHistory i ON i.PersonId=p.PersonId AND i.ValidFromLocalDate<=$to AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>$to)
        WHERE ($person IS NULL OR p.PersonId=$person)
        AND (month_name(i.NameSnapshot) LIKE $search ESCAPE '\' OR month_number(i.ServiceNumberASnapshot) LIKE $number ESCAPE '\')
        AND ($site IS NULL OR EXISTS(SELECT 1 FROM PersonAssignments a WHERE a.PersonId=p.PersonId AND a.SiteId=$site
            AND a.ValidFromLocalDate<=$to AND (a.ValidToLocalDate IS NULL OR a.ValidToLocalDate>$from))
            OR EXISTS(SELECT 1 FROM PersonnelMovements m WHERE m.PersonId=p.PersonId AND m.EffectiveLocalDate BETWEEN $from AND $to AND (m.SourceSiteId=$site OR m.DestinationSiteId=$site)))
        """;
    public Task<ServiceResult<ForceReportPage>> ForceReportAsync(string nonce, ReportQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        var q = ValidateReport(query, false); MonthFunctions(c); using var t = c.BeginTransaction(deferred: true);
        var p = ReportParameters(q).Concat(new (string, object?)[] { ("$search", MonthSearch(ArabicTextNormalizer.NormalizeForSearch(q.Search))), ("$number", MonthSearch(ArabicTextNormalizer.NormalizeDigitsOnly(q.Search))) }).ToArray();
        var total = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM (" + ForcePeopleSql + ")", p).ConfigureAwait(false));
        var keys = await QueryAsync(c, t, ForcePeopleSql + " ORDER BY i.NameSnapshot,p.PersonId LIMIT $limit OFFSET $offset", r => (Id: r.GetString(0), Name: r.GetString(1), Number: r.GetString(2)), p).ConfigureAwait(false);
        const string attendance = " FROM AttendanceRecords WHERE AttendanceLocalDate BETWEEN $from AND $to AND ($site IS NULL OR SiteIdAtEffectiveDate=$site)";
        const string moves = " FROM PersonnelMovements WHERE MovementType=1 AND EffectiveLocalDate BETWEEN $from AND $to AND ($site IS NULL OR SourceSiteId=$site OR DestinationSiteId=$site)";
        var all = " AND PersonId IN(SELECT PersonId FROM (" + ForcePeopleSql + "))";
        var totals = await QueryAsync(c, t, "SELECT StatusId,StatusNameSnapshot,StatusSymbolSnapshot,COUNT(*)" + attendance + all + " GROUP BY StatusId,StatusNameSnapshot,StatusSymbolSnapshot ORDER BY StatusId,StatusNameSnapshot,StatusSymbolSnapshot", ReportStatus, p).ConfigureAwait(false);
        var transferTotal = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*)" + moves + all, p).ConfigureAwait(false));
        var batch = p.Concat(new (string, object?)[] { ("$ids", JsonSerializer.Serialize(keys.Select(k => k.Id))) }).ToArray();
        var selected = " AND PersonId IN(SELECT value FROM json_each($ids))";
        var statuses = await QueryAsync(c, t, "SELECT PersonId,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,COUNT(*)" + attendance + selected + " GROUP BY PersonId,StatusId,StatusNameSnapshot,StatusSymbolSnapshot", r => (Id: r.GetString(0), Status: new ReportStatusDays(r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4))), batch).ConfigureAwait(false);
        var movementCounts = await QueryAsync(c, t, "SELECT PersonId,COUNT(*)" + moves + selected + " GROUP BY PersonId", r => (Id: r.GetString(0), Count: r.GetInt64(1)), batch).ConfigureAwait(false);
        var byStatus = statuses.ToLookup(s => s.Id); var byMovement = movementCounts.ToDictionary(m => m.Id, m => m.Count);
        var rows = keys.Select(k => { token.ThrowIfCancellationRequested(); var days = byStatus[k.Id].Select(s => s.Status).ToArray(); return new ForceReportRow(k.Id, k.Name, k.Number, days.Sum(s => s.Days), byMovement.GetValueOrDefault(k.Id), days); }).ToArray();
        var historyToken = await ReportTokenAsync(c, t, q).ConfigureAwait(false);
        t.Commit(); return new ForceReportPage(q.FromDate!, q.ToDate!, new(rows, total, q.Offset), totals, totals.Sum(s => s.Days), transferTotal, historyToken);
    }, token);
}
