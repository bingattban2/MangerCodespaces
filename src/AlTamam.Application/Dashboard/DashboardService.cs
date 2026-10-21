using AlTamam.Application.Attendance;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Dashboard;

public sealed class DashboardService(IAttendanceStore store)
{
    public Task<ServiceResult<DailyRosterPage>> QueryAsync(string nonce, DailyRosterQuery query, CancellationToken token = default) => store.QueryAsync(nonce, query, token);
    public Task<ServiceResult<DashboardSummary>> SummaryAsync(string nonce, DailyRosterQuery query, CancellationToken token = default) => store.SummaryAsync(nonce, query, token);
}
