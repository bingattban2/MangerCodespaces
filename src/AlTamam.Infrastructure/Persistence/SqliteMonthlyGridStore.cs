using System.Globalization;
using System.Text.Json;
using AlTamam.Application.Monthly;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;
namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteRegistryStore : IMonthlyGridStore
{
    private sealed record MonthKey(string PersonId,string SiteId,string Name,string Number,string SiteName);
    private sealed record MonthAssignment(string PersonId,string SiteId,string From,string? To);
    private sealed record MonthAttendance(string PersonId,string SiteId,string Date,string StatusId,string Name,string Symbol,string Color,long RecordId,string Committed,long Revision,string Actor);
    private const string MonthKeysSql="""
        WITH membership AS (
          SELECT PersonId,SiteId FROM PersonAssignments WHERE ValidFromLocalDate<=$last AND (ValidToLocalDate IS NULL OR ValidToLocalDate>$first)
          UNION SELECT PersonId,SourceSiteId FROM PersonnelMovements WHERE EffectiveLocalDate BETWEEN $first AND $last
          UNION SELECT PersonId,DestinationSiteId FROM PersonnelMovements WHERE DestinationSiteId IS NOT NULL AND EffectiveLocalDate BETWEEN $first AND $last
        )
        SELECT m.PersonId,m.SiteId,i.NameSnapshot,i.ServiceNumberASnapshot,h.VisibleNameSnapshot
        FROM membership m
        JOIN PersonIdentityHistory i ON i.PersonId=m.PersonId AND i.ValidFromLocalDate<=$asof AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>$asof)
        JOIN SiteNameHistory h ON h.SiteId=m.SiteId AND h.ValidFromLocalDate<=$asof AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$asof)
        WHERE ($site IS NULL OR m.SiteId=$site) AND (month_name(i.NameSnapshot) LIKE $search ESCAPE '\' OR month_number(i.ServiceNumberASnapshot) LIKE $number ESCAPE '\')
        """;
    private static void MonthFunctions(SqliteConnection c)
    {c.CreateFunction("month_name",(string s)=>ArabicTextNormalizer.NormalizeForSearch(s));c.CreateFunction("month_number",(string s)=>ArabicTextNormalizer.NormalizeDigitsOnly(s));}
    private static string MonthSearch(string s)=>"%"+s.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
    private async Task<List<MonthlyRow>> BuildMonthRowsAsync(SqliteConnection c,SqliteTransaction t,IReadOnlyList<MonthKey> keys,DateOnly first,DateOnly last,CancellationToken token)
    {
        if(keys.Count==0)return [];
        var ids=JsonSerializer.Serialize(keys.Select(k=>k.PersonId).Distinct());
        var parameters=new (string,object?)[]{("$ids",ids),("$first",first.ToString("yyyy-MM-dd")),("$last",last.ToString("yyyy-MM-dd"))};
        var assignments=await QueryAsync(c,t,"SELECT PersonId,SiteId,ValidFromLocalDate,ValidToLocalDate FROM PersonAssignments WHERE PersonId IN(SELECT value FROM json_each($ids)) AND ValidFromLocalDate<=$last AND (ValidToLocalDate IS NULL OR ValidToLocalDate>$first)",r=>new MonthAssignment(r.GetString(0),r.GetString(1),r.GetString(2),r.IsDBNull(3)?null:r.GetString(3)),parameters).ConfigureAwait(false);
        var attendance=await QueryAsync(c,t,"SELECT PersonId,SiteIdAtEffectiveDate,AttendanceLocalDate,StatusId,StatusNameSnapshot,StatusSymbolSnapshot,StatusColorSnapshot,RecordId,CommittedAtUtc,RevisionNumber,CreatedByUserId FROM AttendanceRecords WHERE PersonId IN(SELECT value FROM json_each($ids)) AND AttendanceLocalDate BETWEEN $first AND $last",r=>new MonthAttendance(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetInt64(7),r.GetString(8),r.GetInt64(9),r.GetString(10)),parameters).ConfigureAwait(false);
        var movements=await QueryAsync(c,t,MovementArchiveSelect+" WHERE PersonId IN(SELECT value FROM json_each($ids)) AND EffectiveLocalDate BETWEEN $first AND $last",MovementRow,parameters).ConfigureAwait(false);
        var byAttendance=attendance.ToDictionary(a=>(a.PersonId,a.SiteId,a.Date));var byMovement=movements.ToLookup(m=>(m.PersonId,m.EffectiveDate));var byAssignment=assignments.ToLookup(a=>(a.PersonId,a.SiteId));
        var result=new List<MonthlyRow>();var today=Day;
        foreach(var key in keys)
        {
            token.ThrowIfCancellationRequested();var cells=new List<MonthlyCellDto>();
            for(var date=first;date<=last;date=date.AddDays(1))
            {
                var day=date.ToString("yyyy-MM-dd");MonthlyCellDto cell=new(key.PersonId,key.SiteId,day,"empty",null,null,null,null,null);
                var movement=byMovement[(key.PersonId,day)].FirstOrDefault(m=>m.SourceSiteId==key.SiteId||m.DestinationSiteId==key.SiteId);
                if(movement is not null)
                {
                    var outgoing=movement.SourceSiteId==key.SiteId;
                    var kind=movement.Type==3?"separation":outgoing?"movementOut":"movementIn";
                    var label=movement.Type==3?"مفصول: "+movement.Reason:outgoing?"تم نقله إلى "+movement.DestinationName:"تم نقله من "+movement.SourceSiteName;
                    cell=cell with{Kind=kind,Symbol=movement.Type==3?"ف":"ن",DisplayLabelSnapshot=label,DetailsRef=movement.MovementId};
                }
                else if(byAttendance.TryGetValue((key.PersonId,key.SiteId,day),out var record))
                    cell=cell with{Kind="attendance",StatusId=record.StatusId,Symbol=record.Symbol,Color=record.Color,DisplayLabelSnapshot=record.Name,DetailsRef=record.RecordId.ToString(CultureInfo.InvariantCulture)};
                else if(string.CompareOrdinal(day,today)<=0 && byAssignment[(key.PersonId,key.SiteId)].Any(a=>string.CompareOrdinal(a.From,day)<=0&&(a.To is null||string.CompareOrdinal(a.To,day)>0)))
                    cell=cell with{Kind="unprepared",DisplayLabelSnapshot="غير محضر"};
                cells.Add(cell);
            }
            result.Add(new(key.PersonId,key.SiteId,key.Name,key.Number,key.SiteName,cells));
        }
        return result;
    }
    public Task<ServiceResult<MonthlyPage>> QueryMonthAsync(string nonce,MonthlyQuery query,CancellationToken token=default)=>ReadAsync(nonce,async(c,authenticatedActor)=>
    {
        Require(query.Year is >=2020 and <=9998 && query.Month is >=1 and <=12 && query.Offset>=0 && query.Limit is >=1 and <=100 && query.Search.Length<=100 && (query.SiteId?.Length??0)<=100,"معايير التمام الشهري غير صحيحة.");
        var first=new DateOnly(query.Year,query.Month,1);Require(first<=clock.CurrentBusinessDate,"لا يمكن فتح شهر مستقبلي.");var last=first.AddMonths(1).AddDays(-1);var asof=last<clock.CurrentBusinessDate?last:clock.CurrentBusinessDate;
        MonthFunctions(c);var parameters=new (string,object?)[]{("$first",first.ToString("yyyy-MM-dd")),("$last",last.ToString("yyyy-MM-dd")),("$asof",asof.ToString("yyyy-MM-dd")),("$site",query.SiteId),("$search",MonthSearch(ArabicTextNormalizer.NormalizeForSearch(query.Search))),("$number",MonthSearch(ArabicTextNormalizer.NormalizeDigitsOnly(query.Search))),("$offset",query.Offset),("$limit",query.Limit)};
        using var t=c.BeginTransaction(deferred:true);
        var total=Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM ("+MonthKeysSql+")",parameters).ConfigureAwait(false));
        var keys=await QueryAsync(c,t,MonthKeysSql+" ORDER BY i.NameSnapshot,m.PersonId,m.SiteId LIMIT $limit OFFSET $offset",r=>new MonthKey(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4)),parameters).ConfigureAwait(false);
        var rows=await BuildMonthRowsAsync(c,t,keys,first,last,token).ConfigureAwait(false);t.Commit();return new MonthlyPage(query.Year,query.Month,last.Day,Day,total,query.Offset,rows);
    },token);
    public Task<ServiceResult<IReadOnlyList<ArchivePeriod>>> PeriodsAsync(string nonce,CancellationToken token=default)=>ReadAsync<IReadOnlyList<ArchivePeriod>>(nonce,async(c,authenticatedActor)=>
    {
        var earliest=await ScalarAsync(c,null,"SELECT MIN(ValidFromLocalDate) FROM PersonAssignments").ConfigureAwait(false) as string;
        var date=earliest is not null?DateOnly.ParseExact(earliest,"yyyy-MM-dd",CultureInfo.InvariantCulture):clock.CurrentBusinessDate;
        var first=new DateOnly(date.Year,date.Month,1);var current=new DateOnly(clock.CurrentBusinessDate.Year,clock.CurrentBusinessDate.Month,1);var result=new List<ArchivePeriod>();
        for(var month=current;month>=first;month=month.AddMonths(-1)){token.ThrowIfCancellationRequested();result.Add(new(month.Year,month.Month));}
        return result;
    },token);
    public Task<ServiceResult<MonthlyCellDetails>> CellAsync(string nonce,MonthlyCellQuery query,CancellationToken token=default)=>ReadAsync(nonce,async(c,authenticatedActor)=>
    {
        Require(query.PersonId.Length is >0 and <=100 && query.SiteId.Length is >0 and <=100 && DateOnly.TryParseExact(query.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _),"الخانة المختارة غير صحيحة.");
        var date=DateOnly.ParseExact(query.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture);Require(date<=clock.CurrentBusinessDate,"التاريخ مستقبلي.");using var t=c.BeginTransaction(deferred:true);
        var keys=await QueryAsync(c,t,"SELECT i.NameSnapshot,i.ServiceNumberASnapshot,h.VisibleNameSnapshot FROM PersonIdentityHistory i JOIN SiteNameHistory h ON h.SiteId=$site AND h.ValidFromLocalDate<=$day AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$day) WHERE i.PersonId=$person AND i.ValidFromLocalDate<=$day AND (i.ValidToLocalDate IS NULL OR i.ValidToLocalDate>$day)",r=>new MonthKey(query.PersonId,query.SiteId,r.GetString(0),r.GetString(1),r.GetString(2)),("$site",query.SiteId),("$person",query.PersonId),("$day",query.Date)).ConfigureAwait(false);
        Require(keys.Count==1,"لا توجد هوية مؤرخة لهذا الفرد والموقع في التاريخ المختار.",BridgeErrorCodes.MissingData);
        var row=(await BuildMonthRowsAsync(c,t,keys,date,date,token).ConfigureAwait(false))[0];var cell=row.Cells[0];
        string? committed=null,actor=null;long revision=0;
        if(cell.Kind=="attendance")
        {var metadata=await QueryAsync(c,t,"SELECT CommittedAtUtc,RevisionNumber,CreatedByUserId FROM AttendanceRecords WHERE RecordId=$id",r=>(Time:r.GetString(0),Revision:r.GetInt64(1),Actor:r.GetString(2)),("$id",cell.DetailsRef)).ConfigureAwait(false);(committed,revision,actor)=metadata[0];}
        else if(cell.DetailsRef is not null)
        {var metadata=await QueryAsync(c,t,"SELECT CommittedAtUtc,ActorUserId FROM PersonnelMovements WHERE MovementId=$id",r=>(Time:r.GetString(0),Actor:r.GetString(1)),("$id",cell.DetailsRef)).ConfigureAwait(false);(committed,actor)=metadata[0];}
        t.Commit();return new MonthlyCellDetails(cell,row.Name,row.ServiceNumberA,row.SiteName,committed,revision,actor);
    },token);
}
