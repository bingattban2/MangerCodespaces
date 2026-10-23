using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Attendance;

public interface IDayProcessingStore
{
    Task<ServiceResult<DayProcessingAttempt>> ProcessDueAsync(bool catchUp, CancellationToken token = default);
    Task<ServiceResult<DayProcessingStatus>> GetDayStatusAsync(string nonce, DayProcessingQuery query, CancellationToken token = default);
    Task RecordSchedulerFailureAsync(string? failure, CancellationToken token = default);
    Task<ServiceResult<ClosedDayCorrectionPreview>> PreviewCorrectionAsync(string nonce, ClosedDayCorrection change, CancellationToken token = default);
    Task<ServiceResult<AttendanceCommitResult>> CorrectClosedDayAsync(string nonce, string requestId, CommitClosedDayCorrection command, CancellationToken token = default);
}
