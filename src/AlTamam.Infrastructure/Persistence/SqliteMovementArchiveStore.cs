using System.Globalization;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;
namespace AlTamam.Infrastructure.Persistence;
public sealed partial class SqliteRegistryStore
{
    private const string MovementArchiveSelect="""
        SELECT MovementId,PersonId,PersonNameSnapshot,ServiceNumberASnapshot,MovementType,
          SourceSiteId,SourceSiteNameSnapshot,DestinationSiteId,
          COALESCE(DestinationSiteNameSnapshot,DestinationExternalName,'مفصول'),COALESCE(SeparationReason,''),
          EffectiveLocalDate,CommittedAtUtc,ActorUserId FROM PersonnelMovements
        """;
    private static MovementArchiveRow MovementRow(SqliteDataReader r)=>new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt32(4),r.GetString(5),r.GetString(6),r.IsDBNull(7)?null:r.GetString(7),r.GetString(8),r.GetString(9),r.GetString(10),r.GetString(11),r.GetString(12));
    public Task<ServiceResult<MovementArchivePage>> QueryMovementsAsync(string nonce,MovementArchiveQuery query,CancellationToken token=default)=>ReadAsync(nonce,async(c,actor)=>
    {
        Require(query.Search.Length<=100 && query.Offset>=0 && query.Limit is >=1 and <=100 && (query.Type is null or >=1 and <=3) && (query.SiteId?.Length??0)<=100 && (query.PersonId?.Length??0)<=100,"معايير الأرشيف غير صحيحة.");
        static bool Date(string? value)=>value is null || DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
        Require(Date(query.FromDate)&&Date(query.ToDate)&&(query.FromDate is null||query.ToDate is null||string.CompareOrdinal(query.FromDate,query.ToDate)<=0),"نطاق التواريخ غير صحيح.");
        c.CreateFunction("archive_name",(string value)=>ArabicTextNormalizer.NormalizeForSearch(value));c.CreateFunction("archive_number",(string value)=>ArabicTextNormalizer.NormalizeDigitsOnly(value));
        static string Search(string value)=>"%"+value.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_")+"%";
        const string filter="""
             WHERE (archive_name(PersonNameSnapshot) LIKE $search ESCAPE '\' OR archive_number(ServiceNumberASnapshot) LIKE $number ESCAPE '\' OR archive_name(COALESCE(SeparationReason,'')) LIKE $search ESCAPE '\')
             AND ($type IS NULL OR MovementType=$type) AND ($person IS NULL OR PersonId=$person)
             AND ($site IS NULL OR SourceSiteId=$site OR DestinationSiteId=$site)
             AND ($from IS NULL OR EffectiveLocalDate>=$from) AND ($to IS NULL OR EffectiveLocalDate<=$to)
            """;
        var parameters=new (string,object?)[]{("$search",Search(ArabicTextNormalizer.NormalizeForSearch(query.Search))),("$number",Search(ArabicTextNormalizer.NormalizeDigitsOnly(query.Search))),("$type",query.Type),("$person",query.PersonId),("$site",query.SiteId),("$from",query.FromDate),("$to",query.ToDate),("$offset",query.Offset),("$limit",query.Limit)};
        using var t=c.BeginTransaction(deferred:true);var total=Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM PersonnelMovements"+filter,parameters).ConfigureAwait(false));
        var rows=await QueryAsync(c,t,MovementArchiveSelect+filter+" ORDER BY EffectiveLocalDate DESC,CommittedAtUtc DESC,MovementId LIMIT $limit OFFSET $offset",MovementRow,parameters).ConfigureAwait(false);t.Commit();return new MovementArchivePage(rows,total);
    },token);
    public Task<ServiceResult<MovementArchiveRow>> GetMovementAsync(string nonce,string movementId,CancellationToken token=default)=>ReadAsync(nonce,async(c,actor)=>
    {
        Require(movementId.Length is >0 and <=100,"معرف الحركة غير صحيح.");var rows=await QueryAsync(c,null,MovementArchiveSelect+" WHERE MovementId=$id",MovementRow,("$id",movementId)).ConfigureAwait(false);Require(rows.Count==1,"الحركة غير موجودة.",BridgeErrorCodes.MissingData);return rows[0];
    },token);
}
