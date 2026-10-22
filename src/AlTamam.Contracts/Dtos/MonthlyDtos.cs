namespace AlTamam.Contracts.Dtos;
public sealed record MonthlyQuery(int Year, int Month, string? SiteId=null, string Search="", int Offset=0, int Limit=50);
public sealed record MonthlyRow(string PersonId,string SiteId,string Name,string ServiceNumberA,string SiteName,IReadOnlyList<MonthlyCellDto> Cells);
public sealed record MonthlyPage(int Year,int Month,int DaysInMonth,string BusinessDate,long Total,int Offset,IReadOnlyList<MonthlyRow> Items);
public sealed record ArchivePeriod(int Year,int Month);
public sealed record MonthlyCellQuery(string PersonId,string SiteId,string Date);
public sealed record MonthlyCellDetails(MonthlyCellDto Cell,string PersonName,string ServiceNumberA,string SiteName,string? CommittedAtUtc,long Revision,string? ActorUserId);
