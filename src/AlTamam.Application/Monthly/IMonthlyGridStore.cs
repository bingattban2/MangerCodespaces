using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Monthly;
public interface IMonthlyGridStore
{
 Task<ServiceResult<MonthlyPage>> QueryMonthAsync(string nonce,MonthlyQuery query,CancellationToken token=default);
 Task<ServiceResult<IReadOnlyList<ArchivePeriod>>> PeriodsAsync(string nonce,CancellationToken token=default);
 Task<ServiceResult<MonthlyCellDetails>> CellAsync(string nonce,MonthlyCellQuery query,CancellationToken token=default);
}
