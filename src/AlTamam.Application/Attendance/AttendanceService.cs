using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Attendance;

public sealed class AttendanceService(IAttendanceStore store, IDayProcessingStore processing)
{
    public Task<ServiceResult<AttendancePreview>> PreviewAsync(string nonce, AttendanceChange change, CancellationToken token = default) => store.PreviewAsync(nonce, change, token);
    public Task<ServiceResult<AttendanceCommitResult>> CommitAsync(string nonce, string requestId, CommitAttendanceCommand command, CancellationToken token = default) => store.CommitAsync(nonce, requestId, command, token);
    public Task<ServiceResult<AttendanceDraftDto>> SaveDraftAsync(string nonce, string requestId, SaveAttendanceDraftCommand command, CancellationToken token = default) => store.SaveDraftAsync(nonce, requestId, command, token);
    public Task<ServiceResult<IReadOnlyList<AttendanceDraftDto>>> ListDraftsAsync(string nonce, CancellationToken token = default) => store.ListDraftsAsync(nonce, token);
    public Task<ServiceResult<string>> DeleteDraftAsync(string nonce, string requestId, DeleteAttendanceDraftCommand command, CancellationToken token = default) => store.DeleteDraftAsync(nonce, requestId, command, token);
    public Task<ServiceResult<ClosedDayCorrectionPreview>> PreviewCorrectionAsync(string nonce, ClosedDayCorrection change, CancellationToken token = default) => processing.PreviewCorrectionAsync(nonce, change, token);
    public Task<ServiceResult<AttendanceCommitResult>> CorrectClosedDayAsync(string nonce, string requestId, CommitClosedDayCorrection command, CancellationToken token = default) => processing.CorrectClosedDayAsync(nonce, requestId, command, token);
}
