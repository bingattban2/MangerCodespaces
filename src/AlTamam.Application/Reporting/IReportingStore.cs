using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Reporting;
public interface IReportingStore
{
    Task<ServiceResult<PersonLifetimeReport>> PersonReportAsync(string nonce, ReportQuery query, CancellationToken token = default);
    Task<ServiceResult<PersonReportSummary>> PersonTotalsAsync(string nonce, ReportQuery query, CancellationToken token = default);
    Task<ServiceResult<ReportPage<ReportAttendance>>> PersonAttendanceAsync(string nonce, ReportQuery query, CancellationToken token = default);
    Task<ServiceResult<ReportPage<ReportOperation>>> PersonOperationsAsync(string nonce, ReportQuery query, CancellationToken token = default);
    Task<ServiceResult<ForceReportPage>> ForceReportAsync(string nonce, ReportQuery query, CancellationToken token = default);
}
