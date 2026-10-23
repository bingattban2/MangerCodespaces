using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteAttendanceStore
{
    private sealed record CorrectionState(long RecordId, string PersonName, string Number, string SiteName,
        string BeforeId, string BeforeName, string BeforeSymbol, string BeforeColor, long Revision,
        string AfterName, string AfterSymbol, string AfterColor);
    private async Task AuthorizeCorrectionAsync(SqliteConnection c, SqliteTransaction t, AccountDto actor)
    {
        Require(actor.Role == 1, "التصحيح الاستثنائي محصور بالإدارة العامة.", BridgeErrorCodes.Unauthorized);
        Require(string.Equals(Convert.ToString(await ScalarAsync(c, t, "SELECT SettingValue FROM AppSettings WHERE SettingKey='AllowClosedDayEmergencyOverride'").ConfigureAwait(false)), "true", StringComparison.Ordinal),
            "التصحيح الاستثنائي معطل في إعدادات البرنامج.", BridgeErrorCodes.Unauthorized);
    }
    private async Task<(ClosedDayCorrectionPreview Preview, CorrectionState State)> CorrectionPreviewAsync(SqliteConnection c, SqliteTransaction t, AccountDto actor, ClosedDayCorrection change)
    {
        await AuthorizeCorrectionAsync(c, t, actor).ConfigureAwait(false);
        Require(change.PersonId.Length is > 0 and <= 100 && change.StatusId.Length is > 0 and <= 100 && !string.IsNullOrWhiteSpace(change.Reason) && change.Reason.Length <= 500 && !change.Reason.Any(char.IsControl), "الفرد والحالة وسبب التصحيح مطلوبة.");
        Require(DateOnly.TryParseExact(change.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date < clock.CurrentBusinessDate,
            "التصحيح الاستثنائي مخصص ليوم سابق؛ استخدم تعديل تحضير اليوم لليوم الحالي.", BridgeErrorCodes.ValidationError);
        Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PersonnelMovements WHERE PersonId=$id AND EffectiveLocalDate=$day", ("$id", change.PersonId), ("$day", change.BusinessDate)).ConfigureAwait(false)) == 0,
            "لا يسمح بالتحضير في يوم النقل أو الفصل.", BridgeErrorCodes.TransferDayAttendanceForbidden);
        var rows = await SqliteOperations.QueryAsync(c, t, """
            SELECT ar.RecordId,i.NameSnapshot,i.ServiceNumberASnapshot,ar.SiteNameSnapshot,
              ar.StatusId,ar.StatusNameSnapshot,ar.StatusSymbolSnapshot,ar.StatusColorSnapshot,ar.RevisionNumber,
              h.VisibleNameSnapshot,h.ShortSymbolSnapshot,h.ColorSnapshot
            FROM AttendanceRecords ar
            JOIN PersonIdentityHistory i ON i.PersonId=ar.PersonId AND i.ValidFromLocalDate<=ar.AttendanceLocalDate AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>ar.AttendanceLocalDate)
            JOIN AttendanceStatusDefinitions s ON s.StatusId=$status AND s.ActiveFlag=1
            JOIN AttendanceStatusNameHistory h ON h.StatusId=s.StatusId AND h.ValidFromLocalDate<=ar.AttendanceLocalDate AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>ar.AttendanceLocalDate)
            WHERE ar.PersonId=$id AND ar.AttendanceLocalDate=$day
            """, r => new CorrectionState(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetString(7), r.GetInt64(8), r.GetString(9), r.GetString(10), r.GetString(11)),
            ("$id", change.PersonId), ("$status", change.StatusId), ("$day", change.BusinessDate)).ConfigureAwait(false);
        Require(rows.Count == 1, "لا يوجد تحضير معتمد لهذا الفرد في التاريخ المختار، أو أن الحالة لم تكن متاحة فيه.", BridgeErrorCodes.MissingData);
        var state = rows[0]; Require(state.BeforeId != change.StatusId, "اختر حالة مختلفة عن الحالة المعتمدة.");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { actor.UserId, Day, Change = change, State = state }))));
        return (new(change, state.PersonName, state.Number, state.SiteName, state.BeforeName, state.AfterName, state.Revision, hash), state);
    }
    public Task<ServiceResult<ClosedDayCorrectionPreview>> PreviewCorrectionAsync(string nonce, ClosedDayCorrection change, CancellationToken token = default) => ReadAsync(nonce, async (c, t, actor) => (await CorrectionPreviewAsync(c, t, actor, change).ConfigureAwait(false)).Preview, token);
    public Task<ServiceResult<AttendanceCommitResult>> CorrectClosedDayAsync(string nonce, string requestId, CommitClosedDayCorrection command, CancellationToken token = default) =>
        WriteAsync<AttendanceCommitResult>(nonce, requestId, "CLOSED_DAY_CORRECTED", command, async (c, t, actor) =>
        {
            var prepared = await CorrectionPreviewAsync(c, t, actor, command.Change).ConfigureAwait(false);
            Require(prepared.Preview.PreviewHash == command.PreviewHash, "تغيرت البيانات بعد المعاينة؛ أعد معاينة التصحيح.", BridgeErrorCodes.ConflictStale);
            var state = prepared.State; var change = command.Change;
            await ExecuteAsync(c, t, """
                INSERT INTO AttendanceRevisions(PersonId,TargetLocalDate,BeforeStatusId,BeforeStatusNameSnapshot,AfterStatusId,AfterStatusNameSnapshot,Reason,ActorUserId,ExecutedAtUtc,AuthorizationMethod)
                VALUES($person,$day,$before,$beforeName,$after,$afterName,$reason,$actor,$now,'general-admin-enabled-override');
                UPDATE AttendanceRecords SET StatusId=$after,StatusNameSnapshot=$afterName,StatusSymbolSnapshot=$symbol,StatusColorSnapshot=$color,RevisionNumber=RevisionNumber+1
                WHERE RecordId=$record AND RevisionNumber=$revision;
                """, ("$person", change.PersonId), ("$day", change.BusinessDate), ("$before", state.BeforeId), ("$beforeName", state.BeforeName), ("$after", change.StatusId), ("$afterName", state.AfterName),
                ("$reason", change.Reason.Trim()), ("$actor", actor.UserId), ("$now", Now), ("$symbol", state.AfterSymbol), ("$color", state.AfterColor), ("$record", state.RecordId), ("$revision", state.Revision)).ConfigureAwait(false);
            return new Mutation<AttendanceCommitResult>(new(change.BusinessDate, 1, true), state);
        }, token, effectiveDay: command.Change.BusinessDate, reason: command.Change.Reason, authorize: AuthorizeCorrectionAsync);
}
