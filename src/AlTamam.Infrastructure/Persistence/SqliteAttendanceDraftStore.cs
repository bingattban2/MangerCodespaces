using System.Text.Json;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteAttendanceStore
{
    public Task<ServiceResult<AttendanceDraftDto>> SaveDraftAsync(string nonce, string requestId, SaveAttendanceDraftCommand command, CancellationToken token = default) =>
        WriteAsync<AttendanceDraftDto>(nonce, requestId, "ATTENDANCE_DRAFT_SAVED", command, async (c, t, actor) =>
        {
            Require(!command.Change.Revise, "المسودة مخصصة للتحضير الجديد؛ تعديل التحضير يحتاج معاينة مباشرة.");
            var prepared = await PrepareAsync(c, t, actor, command.Change).ConfigureAwait(false);
            var id = "DRF-" + Guid.NewGuid().ToString("N");
            var ids = prepared.Rows.Select(x => x.PersonId).ToArray();
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AttendanceDrafts WHERE CreatedByUserId=$actor", ("$actor", actor.UserId)).ConfigureAwait(false)) < 100, "بلغت مائة مسودة؛ احذف المسودات غير المطلوبة.");
            await ExecuteAsync(c, t, """
                INSERT INTO AttendanceDrafts(DraftId,BusinessDate,SiteId,TargetStatusId,SelectedPersonIdsJson,CreatedByUserId,UpdatedAtUtc)
                VALUES($id,$day,$site,$status,$ids,$actor,$now)
                """, ("$id", id), ("$day", Day), ("$site", command.Change.Selection.Filter.SiteId), ("$status", command.Change.StatusId), ("$ids", JsonSerializer.Serialize(ids)), ("$actor", actor.UserId), ("$now", Now)).ConfigureAwait(false);
            return new Mutation<AttendanceDraftDto>(new(id, Day, command.Change.Selection.Filter.SiteId, command.Change.StatusId, ids, Now));
        }, token);
    public Task<ServiceResult<IReadOnlyList<AttendanceDraftDto>>> ListDraftsAsync(string nonce, CancellationToken token = default) => ReadAsync<IReadOnlyList<AttendanceDraftDto>>(nonce, async (c, t, actor) =>
        await SqliteOperations.QueryAsync(c, t, "SELECT DraftId,BusinessDate,SiteId,TargetStatusId,SelectedPersonIdsJson,UpdatedAtUtc FROM AttendanceDrafts WHERE CreatedByUserId=$actor ORDER BY UpdatedAtUtc DESC LIMIT 100",
            r => new AttendanceDraftDto(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetString(3), JsonSerializer.Deserialize<string[]>(r.GetString(4)) ?? [], r.GetString(5)), ("$actor", actor.UserId)).ConfigureAwait(false), token);
    public Task<ServiceResult<string>> DeleteDraftAsync(string nonce, string requestId, DeleteAttendanceDraftCommand command, CancellationToken token = default) =>
        WriteAsync<string>(nonce, requestId, "ATTENDANCE_DRAFT_DELETED", command, async (c, t, actor) =>
        {
            Require(command.DraftId.Length is > 0 and <= 100, "معرف المسودة غير صحيح.");
            Require(await ExecuteAsync(c, t, "DELETE FROM AttendanceDrafts WHERE DraftId=$id AND CreatedByUserId=$actor", ("$id", command.DraftId), ("$actor", actor.UserId)).ConfigureAwait(false) == 1, "المسودة غير موجودة.", BridgeErrorCodes.MissingData);
            return new Mutation<string>(command.DraftId);
        }, token);
}
