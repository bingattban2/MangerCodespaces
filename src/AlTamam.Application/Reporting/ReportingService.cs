using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Reporting;
public sealed class ReportingService(IReportingStore store)
{
    public Task<ServiceResult<PersonLifetimeReport>> PersonAsync(string nonce, ReportQuery query, CancellationToken token = default) => store.PersonReportAsync(nonce, query, token);
    public Task<ServiceResult<PersonReportSummary>> TotalsAsync(string nonce, ReportQuery query, CancellationToken token = default) => store.PersonTotalsAsync(nonce, query, token);
    public Task<ServiceResult<ReportPage<ReportAttendance>>> AttendanceAsync(string nonce, ReportQuery query, CancellationToken token = default) => store.PersonAttendanceAsync(nonce, query, token);
    public Task<ServiceResult<ReportPage<ReportOperation>>> OperationsAsync(string nonce, ReportQuery query, CancellationToken token = default) => store.PersonOperationsAsync(nonce, query, token);
    public Task<ServiceResult<ForceReportPage>> ForceAsync(string nonce, ReportQuery query, CancellationToken token = default) => store.ForceReportAsync(nonce, query, token);
}
