using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Attendance;

public interface IAttendanceStore
{
    Task<ServiceResult<DailyRosterPage>> QueryAsync(string nonce, DailyRosterQuery query, CancellationToken token = default);
    Task<ServiceResult<DashboardSummary>> SummaryAsync(string nonce, DailyRosterQuery query, CancellationToken token = default);
    Task<ServiceResult<AttendancePreview>> PreviewAsync(string nonce, AttendanceChange change, CancellationToken token = default);
    Task<ServiceResult<AttendanceCommitResult>> CommitAsync(string nonce, string requestId, CommitAttendanceCommand command, CancellationToken token = default);
    Task<ServiceResult<AttendanceDraftDto>> SaveDraftAsync(string nonce, string requestId, SaveAttendanceDraftCommand command, CancellationToken token = default);
    Task<ServiceResult<IReadOnlyList<AttendanceDraftDto>>> ListDraftsAsync(string nonce, CancellationToken token = default);
    Task<ServiceResult<string>> DeleteDraftAsync(string nonce, string requestId, DeleteAttendanceDraftCommand command, CancellationToken token = default);
}
