using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Monthly;
public sealed class MonthlyGridService(IMonthlyGridStore store)
{
 public Task<ServiceResult<MonthlyPage>> QueryAsync(string nonce,MonthlyQuery query,CancellationToken token=default)=>store.QueryMonthAsync(nonce,query,token);
 public Task<ServiceResult<IReadOnlyList<ArchivePeriod>>> PeriodsAsync(string nonce,CancellationToken token=default)=>store.PeriodsAsync(nonce,token);
 public Task<ServiceResult<MonthlyCellDetails>> CellAsync(string nonce,MonthlyCellQuery query,CancellationToken token=default)=>store.CellAsync(nonce,query,token);
}
