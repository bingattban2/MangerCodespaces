using System.Globalization;
using System.Text.Json;
using AlTamam.Application.Attendance;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.ValueObjects;
using AlTamam.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteAttendanceStore : IDayProcessingStore
{
    private async Task ClosePeriodAsync(SqliteConnection c, SqliteTransaction t, string key, string type, CancellationToken token)
    {
        var count = await ExecuteAsync(c, t, "INSERT OR IGNORE INTO PeriodClosures(PeriodKey,PeriodType,ClosedAtUtc,ClosedByActor,PolicyVersion) VALUES($key,$type,$now,'USR-SYSTEM','current-day-only-v1')",
            ("$key", key), ("$type", type), ("$now", Now)).ConfigureAwait(false);
        if (count == 0) return;
        await SqliteAuditEventWriter.AppendAsync(c, t, "PERIOD_CLOSED", "إغلاق فترة", UserId.SystemActor, null,
            "النظام", null, "PeriodClosures", key, key, null, JsonSerializer.Serialize(new { Period = key, Type = type }),
            "إغلاق الفترة السابقة: " + key, null, clock.CurrentBusinessDate, clock.UtcNow, "CLOSE-" + type + "-" + key, token).ConfigureAwait(false);
    }
    public Task<ServiceResult<DayProcessingAttempt>> ProcessDueAsync(bool catchUp, CancellationToken token = default) => BoundaryAsync(async () =>
        await database.ExecuteInWriteTransactionAsync(async (c, t) =>
        {
            if (Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Users").ConfigureAwait(false)) == 0)
                return ServiceResult<DayProcessingAttempt>.Ok(new(null));
            await ClockGuard.ValidateAsync(c, t, clock.UtcNow, advance: true).ConfigureAwait(false);
            var date = clock.CurrentBusinessDate; var day = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var yesterday = date.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await ClosePeriodAsync(c, t, yesterday, "DAY", token).ConfigureAwait(false);
            await ClosePeriodAsync(c, t, date.AddDays(1 - date.Day).AddDays(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture), "MONTH", token).ConfigureAwait(false);
            // Catch-up is only for TODAY after its deadline. Missed historical days are never fabricated.
            if (clock.LocalNow.TimeOfDay < TimeSpan.FromHours(20)) return ServiceResult<DayProcessingAttempt>.Ok(new(null));
            var job = "AUTO20H-" + day;
            var previous = await ReadAutoRunAsync(c, t, day).ConfigureAwait(false);
            if (previous is not null) return ServiceResult<DayProcessingAttempt>.Ok(new(previous));
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PeriodClosures WHERE IsClosed=1 AND (PeriodKey=$day OR PeriodKey=$month)", ("$day", day), ("$month", day[..7])).ConfigureAwait(false)) == 0,
                "الفترة الحالية مغلقة؛ لم ينفذ الترحيل التلقائي.", BridgeErrorCodes.HistoricDayLocked);
            var started = Now;
            var isCatchUp = catchUp || clock.LocalNow.TimeOfDay >= TimeSpan.FromHours(20) + TimeSpan.FromMinutes(1);
            var candidates = await SqliteOperations.QueryAsync(c, t, """
                SELECT p.PersonId,a.AssignmentId,a.SiteId,sh.VisibleNameSnapshot,
                  EXISTS(SELECT 1 FROM PersonnelMovements m WHERE m.PersonId=p.PersonId AND m.EffectiveLocalDate=$day),
                  y.StatusId,s.ActiveFlag,h.VisibleNameSnapshot,h.ShortSymbolSnapshot,h.ColorSnapshot
                FROM Persons p JOIN PersonAssignments a ON a.PersonId=p.PersonId AND a.ValidFromLocalDate<=$day AND (a.ValidToLocalDate IS NULL OR a.ValidToLocalDate>$day)
                JOIN SiteNameHistory sh ON sh.SiteId=a.SiteId AND sh.ValidFromLocalDate<=$day AND (sh.ValidToLocalDate IS NULL OR sh.ValidToLocalDate>$day)
                LEFT JOIN AttendanceRecords y ON y.PersonId=p.PersonId AND y.AttendanceLocalDate=$yesterday
                LEFT JOIN AttendanceStatusDefinitions s ON s.StatusId=y.StatusId
                LEFT JOIN AttendanceStatusNameHistory h ON h.StatusId=s.StatusId AND h.ValidFromLocalDate<=$day AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$day)
                WHERE p.ActiveFlag=1 AND p.OrganizationalStatus=1 AND NOT EXISTS(SELECT 1 FROM AttendanceRecords ar WHERE ar.PersonId=p.PersonId AND ar.AttendanceLocalDate=$day)
                ORDER BY p.PersonId
                """, r => new CarryCandidate(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) != 0,
                    r.IsDBNull(5) ? null : r.GetString(5), !r.IsDBNull(6) && r.GetInt32(6) == 1,
                    r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9)), ("$day", day), ("$yesterday", yesterday)).ConfigureAwait(false);
            var copied = 0; var missing = 0; var moved = 0; var inactive = 0;
            foreach (var person in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (person.Moved) { moved++; continue; }
                if (person.StatusId is null) { missing++; continue; }
                if (!person.ActiveStatus || person.StatusName is null) { inactive++; continue; }
                await ExecuteAsync(c, t, """
                    INSERT INTO AttendanceRecords(PersonId,AttendanceLocalDate,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,StatusColorSnapshot,AssignmentIdAtEffectiveDate,SiteIdAtEffectiveDate,SiteNameSnapshot,Source,CreatedByUserId,CommittedAtUtc,IdempotencyKey)
                    VALUES($person,$day,$status,$name,$symbol,$color,$assignment,$site,$siteName,$source,'USR-SYSTEM',$now,$id)
                    """, ("$person", person.PersonId), ("$day", day), ("$status", person.StatusId), ("$name", person.StatusName), ("$symbol", person.Symbol), ("$color", person.Color),
                    ("$assignment", person.AssignmentId), ("$site", person.SiteId), ("$siteName", person.SiteName), ("$source", isCatchUp ? "20h-catchup" : "20h-auto"), ("$now", Now), ("$id", job + ":" + person.PersonId)).ConfigureAwait(false);
                copied++;
            }
            Require(clock.CurrentBusinessDate == date, "تغير يوم العمل أثناء الترحيل؛ ستُعاد المحاولة لليوم الجديد.", BridgeErrorCodes.ConflictStale);
            var description = $"تم ترحيل {copied}؛ بلا حالة أمس: {missing}؛ يوم نقل: {moved}؛ حالة معطلة أو غير متاحة: {inactive}.";
            var result = new AutoAttendanceRunDto(job, day, Now, isCatchUp, copied, missing, moved, inactive, description);
            await ExecuteAsync(c, t, """
                INSERT INTO AutoAttendanceRuns(JobKey,BusinessDate,DueTimeLocal,StartedAtUtc,CompletedAtUtc,IsCatchUpRun,CopiedCount,SkippedNoYesterdayCount,SkippedTransferExemptCount,SummaryArabic)
                VALUES($job,$day,'20:00',$started,$finished,$catchUp,$copied,$missing,$moved,$description)
                """, ("$job", job), ("$day", day), ("$started", started), ("$finished", result.CompletedAtUtc), ("$catchUp", isCatchUp ? 1 : 0), ("$copied", copied), ("$missing", missing), ("$moved", moved), ("$description", description)).ConfigureAwait(false);
            await SqliteAuditEventWriter.AppendAsync(c, t, "AUTO_ATTENDANCE_COMPLETED", "التحضير التلقائي", UserId.SystemActor, null, "النظام", null,
                "AutoAttendanceRuns", job, day, null, JsonSerializer.Serialize(result), description, null, date, clock.UtcNow, job, token).ConfigureAwait(false);
            return ServiceResult<DayProcessingAttempt>.Ok(new(result));
        }, token).ConfigureAwait(false));
    private sealed record CarryCandidate(string PersonId, string AssignmentId, string SiteId, string SiteName, bool Moved, string? StatusId, bool ActiveStatus, string? StatusName, string? Symbol, string? Color);
    private static async Task<AutoAttendanceRunDto?> ReadAutoRunAsync(SqliteConnection c, SqliteTransaction? t, string day)
    {
        var json = await ScalarAsync(c, t, "SELECT e.NewValueJson FROM AutoAttendanceRuns r JOIN AuditEvents e ON e.CorrelationId=r.JobKey AND e.OperationCode='AUTO_ATTENDANCE_COMPLETED' WHERE r.BusinessDate=$day", ("$day", day)).ConfigureAwait(false);
        if (json is string value) return JsonSerializer.Deserialize<AutoAttendanceRunDto>(value) ?? throw new InvalidOperationException("Invalid scheduler receipt.");
        Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AutoAttendanceRuns WHERE BusinessDate=$day", ("$day", day)).ConfigureAwait(false)) == 0, "سجل الترحيل موجود دون دليل العملية؛ يلزم فحص سلامة البيانات.", BridgeErrorCodes.BackupIntegrityFailed);
        return null;
    }
    public Task<ServiceResult<DayProcessingStatus>> GetDayStatusAsync(string nonce, DayProcessingQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, t, actor) =>
    {
        var day = query.BusinessDate ?? Day;
        Require(DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= clock.CurrentBusinessDate, "اختر تاريخاً صحيحاً لا يتجاوز اليوم.");
        var run = await ReadAutoRunAsync(c, t, day).ConfigureAwait(false);
        var closed = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PeriodClosures WHERE PeriodKey=$day AND IsClosed=1", ("$day", day)).ConfigureAwait(false)) != 0;
        var month = Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PeriodClosures WHERE PeriodKey=$month AND IsClosed=1", ("$month", day[..7])).ConfigureAwait(false)) != 0;
        var allowed = actor.Role == 1 && string.Equals(Convert.ToString(await ScalarAsync(c, t, "SELECT SettingValue FROM AppSettings WHERE SettingKey='AllowClosedDayEmergencyOverride'").ConfigureAwait(false)), "true", StringComparison.Ordinal);
        var failure = await ScalarAsync(c, t, "SELECT SettingValue FROM AppSettings WHERE SettingKey='LastAttendanceSchedulerFailure'").ConfigureAwait(false) as string;
        return new DayProcessingStatus(day, "20:00", run is not null, run, closed || day != Day, month, allowed, string.IsNullOrEmpty(failure) ? null : failure);
    }, token);
    public Task RecordSchedulerFailureAsync(string? failure, CancellationToken token = default) => database.ExecuteInWriteTransactionAsync(async (c, t) =>
    {
        var prior = await ScalarAsync(c, t, "SELECT SettingValue FROM AppSettings WHERE SettingKey='LastAttendanceSchedulerFailure'").ConfigureAwait(false) as string;
        if ((prior ?? "") == (failure ?? "")) return true;
        await ExecuteAsync(c, t, "INSERT INTO AppSettings(SettingKey,SettingValue,UpdatedAtUtc) VALUES('LastAttendanceSchedulerFailure',$value,$now) ON CONFLICT(SettingKey) DO UPDATE SET SettingValue=$value,UpdatedAtUtc=$now", ("$value", failure ?? ""), ("$now", Now)).ConfigureAwait(false);
        return true;
    }, token);
}
