using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlTamam.Application.Movement;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteRegistryStore : IMovementStore
{
    private readonly byte[] _movementConfirmationKey = RandomNumberGenerator.GetBytes(32);
    private sealed record MovementState(IReadOnlyList<MovementPerson> Persons, string DestinationLabel, long DestinationVersion, string Hash);
    private async Task<MovementState> MovementStateAsync(SqliteConnection c, SqliteTransaction t, AccountDto actor, MovementChange change)
    {
        Require(change.Type is >= 1 and <= 3 && change.PersonIds.Count is >= 1 and <= 500 && change.PersonIds.Distinct(StringComparer.Ordinal).Count() == change.PersonIds.Count && change.PersonIds.All(x => x.Length is > 0 and <= 100), "اختر أفراداً صالحين دون تكرار، بحد أقصى خمسمائة فرد.");
        Require(change.EffectiveDate == Day, "التنقل والفصل متاحان ليوم العمل الحالي فقط؛ لا يمكن تغيير الفترات السابقة.", BridgeErrorCodes.HistoricDayLocked);
        Require(change.SourceSiteId.Length is > 0 and <= 100 && (change.DestinationSiteId?.Length ?? 0) <= 100, "اختر موقعاً صحيحاً.");
        Require(change.Reason.Length <= 500 && !change.Reason.Any(char.IsControl) && change.DestinationName.Length <= 200 && !change.DestinationName.Any(char.IsControl), "الوجهة أو السبب غير صحيح.");
        Require(change.Type != 2 || !string.IsNullOrWhiteSpace(change.DestinationName), "اسم الجهة الخارجية مطلوب.");
        Require(change.Type != 3 || !string.IsNullOrWhiteSpace(change.Reason), "سبب الفصل مطلوب.");
        Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM PeriodClosures WHERE IsClosed=1 AND (PeriodKey=$day OR PeriodKey=$month)",("$day",Day),("$month",Day[..7])).ConfigureAwait(false)) == 0, "الفترة الحالية مغلقة.", BridgeErrorCodes.HistoricDayLocked);
        var members = await QueryAsync(c,t,"""
            SELECT p.PersonId,p.CurrentVisibleName,p.CurrentServiceNumberA,p.RowVersion,a.AssignmentId,a.ValidFromLocalDate,h.VisibleNameSnapshot
            FROM Persons p JOIN PersonAssignments a ON a.PersonId=p.PersonId
            JOIN SiteNameHistory h ON h.SiteId=a.SiteId AND h.ValidFromLocalDate<=$day AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$day)
            WHERE p.PersonId IN (SELECT value FROM json_each($ids)) AND p.ActiveFlag=1 AND p.OrganizationalStatus=1
              AND a.SiteId=$site AND a.ValidFromLocalDate<=$day AND (a.ValidToLocalDate IS NULL OR a.ValidToLocalDate>$day)
            ORDER BY p.PersonId
            """, r => new MovementPerson(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3),r.GetString(4),r.GetString(5),r.GetString(6)),
            ("$day",Day),("$site",change.SourceSiteId),("$ids",JsonSerializer.Serialize(change.PersonIds))).ConfigureAwait(false);
        Require(members.Count == change.PersonIds.Count, "تغيرت القوة أو الموقع؛ أعد اختيار الأفراد.", BridgeErrorCodes.ConflictStale);
        Require(members.All(p => string.CompareOrdinal(p.AssignmentFrom, Day) < 0), "لا يمكن إنهاء انتساب بدأ اليوم؛ نفذ العملية في يوم عمل لاحق.", BridgeErrorCodes.ConflictStale);
        Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT (SELECT COUNT(*) FROM AttendanceRecords WHERE AttendanceLocalDate=$day AND PersonId IN (SELECT value FROM json_each($ids)))+(SELECT COUNT(*) FROM PersonnelMovements WHERE EffectiveLocalDate=$day AND PersonId IN (SELECT value FROM json_each($ids)))",("$day",Day),("$ids",JsonSerializer.Serialize(change.PersonIds))).ConfigureAwait(false)) == 0,
            "يوجد تحضير معتمد أو حركة سابقة اليوم لأحد المختارين؛ لن يُحذف التاريخ لتنفيذ النقل.", BridgeErrorCodes.ConflictStale);
        if(change.Type != 1)
            Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM Users WHERE LinkedPersonId IN (SELECT value FROM json_each($ids)) AND (Role=1 OR UserId=$actor)",("$ids",JsonSerializer.Serialize(change.PersonIds)),("$actor",actor.UserId)).ConfigureAwait(false)) == 0,
                "لا يمكن إخراج الفرد المرتبط بحساب الإدارة العامة أو بحسابك الحالي.", BridgeErrorCodes.Unauthorized);
        var label=change.Type == 2 ? change.DestinationName.Trim() : "مفصول"; long version=0;
        if(change.Type == 1)
        {
            Require(!string.IsNullOrEmpty(change.DestinationSiteId) && change.DestinationSiteId != change.SourceSiteId, "اختر موقعاً مختلفاً عن الموقع الحالي.", BridgeErrorCodes.InvalidTransferSites);
            var target=await QueryAsync(c,t,"SELECT h.VisibleNameSnapshot,s.RowVersion FROM Sites s JOIN SiteNameHistory h ON h.SiteId=s.SiteId AND h.ValidFromLocalDate<=$day AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$day) WHERE s.SiteId=$id AND s.ActiveFlag=1",r=>(Name:r.GetString(0),Version:r.GetInt64(1)),("$id",change.DestinationSiteId),("$day",Day)).ConfigureAwait(false);
            Require(target.Count == 1,"الموقع المستهدف غير متاح.",BridgeErrorCodes.InvalidTransferSites);label=target[0].Name;version=target[0].Version;
        }
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{actor.UserId,Day,Change=change,Members=members,Destination=label,Version=version}))));
        return new(members,label,version,hash);
    }
    private string ConfirmationMac(string actor,string hash,long ticks) => Convert.ToHexString(HMACSHA256.HashData(_movementConfirmationKey,Encoding.UTF8.GetBytes(actor+"|"+hash+"|"+ticks.ToString(CultureInfo.InvariantCulture))));
    private void ValidateMovementConfirmation(AccountDto actor, CommitMovement command)
    {
        var parts=command.ConfirmationToken.Split('.');
        Require(parts.Length==2 && long.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out _),"انتهت المعاينة؛ أعد تأكيد العملية.",BridgeErrorCodes.ConflictStale);
        var ticks=long.Parse(parts[0],CultureInfo.InvariantCulture);
        var expected=Encoding.ASCII.GetBytes(ConfirmationMac(actor.UserId,command.PreviewHash,ticks));
        Require(CryptographicOperations.FixedTimeEquals(expected,Encoding.ASCII.GetBytes(parts[1])),"تأكيد العملية غير صالح.",BridgeErrorCodes.Unauthorized);
        var elapsed=clock.UtcNow.UtcTicks-ticks;
        Require(elapsed >= (command.Change.Type==3 ? TimeSpan.FromSeconds(5).Ticks : 0) && elapsed <= TimeSpan.FromMinutes(5).Ticks,"انتظر انتهاء مهلة التأكيد، أو أعد المعاينة إذا انتهت صلاحيتها.",BridgeErrorCodes.ConflictStale);
    }
    public Task<ServiceResult<MovementPreview>> PreviewMovementAsync(string nonce,MovementChange change,CancellationToken token=default) => ReadAsync(nonce,async(c,actor)=>
    {
        using var t=c.BeginTransaction(deferred:true);var state=await MovementStateAsync(c,t,actor,change).ConfigureAwait(false);t.Commit();
        var ticks=clock.UtcNow.UtcTicks;return new MovementPreview(change,state.Persons,state.DestinationLabel,state.Hash,ticks.ToString(CultureInfo.InvariantCulture)+"."+ConfirmationMac(actor.UserId,state.Hash,ticks),change.Type==3?5:0);
    },token);
    public Task<ServiceResult<MovementResult>> CommitMovementAsync(string nonce,string requestId,CommitMovement command,CancellationToken token=default) => WriteAsync(nonce,requestId,"PERSONNEL_MOVED",command,async(c,t,actor)=>
    {
        ValidateMovementConfirmation(actor,command);
        var state=await MovementStateAsync(c,t,actor,command.Change).ConfigureAwait(false);
        Require(state.Hash==command.PreviewHash,"تغيرت البيانات بعد المعاينة؛ أعد المعاينة.",BridgeErrorCodes.ConflictStale);
        var change=command.Change;var ids=new List<string>();
        foreach(var p in state.Persons)
        {
            token.ThrowIfCancellationRequested();Require(Day==change.EffectiveDate,"تغير يوم العمل؛ أعد المعاينة.",BridgeErrorCodes.ConflictStale);
            var id=Id("MOV");ids.Add(id);
            await ExecuteAsync(c,t,"""
                UPDATE PersonAssignments SET ValidToLocalDate=$day WHERE AssignmentId=$assignment;
                INSERT INTO PersonnelMovements(MovementId,PersonId,PersonNameSnapshot,ServiceNumberASnapshot,MovementType,SourceSiteId,SourceSiteNameSnapshot,DestinationSiteId,DestinationSiteNameSnapshot,DestinationExternalName,SeparationReason,EffectiveLocalDate,CommittedAtUtc,ActorUserId,IdempotencyKey)
                VALUES($id,$person,$name,$number,$type,$site,$source,$destination,$destinationLabel,$external,$reason,$day,$now,$actor,$request);
                UPDATE Persons SET RowVersion=RowVersion+1,ActiveFlag=$active,OrganizationalStatus=$state WHERE PersonId=$person;
                """,("$day",change.EffectiveDate),("$assignment",p.AssignmentId),("$id",id),("$person",p.PersonId),("$name",p.Name),("$number",p.ServiceNumberA),("$type",change.Type),("$site",change.SourceSiteId),("$source",p.SourceSiteName),
                ("$destination",change.Type==1?change.DestinationSiteId:null),("$destinationLabel",change.Type==1?state.DestinationLabel:null),("$external",change.Type==2?state.DestinationLabel:null),("$reason",change.Type==3?change.Reason.Trim():null),
                ("$now",Now),("$actor",actor.UserId),("$request",requestId+":"+p.PersonId),("$active",change.Type==1?1:0),("$state",change.Type)).ConfigureAwait(false);
            if(change.Type==1)
                await ExecuteAsync(c,t,"INSERT INTO PersonAssignments(AssignmentId,PersonId,SiteId,ValidFromLocalDate,SourceMovementId,CreatedAtUtc) VALUES($id,$person,$site,$day,$move,$now)",("$id",Id("ASN")),("$person",p.PersonId),("$site",change.DestinationSiteId),("$day",change.EffectiveDate),("$move",id),("$now",Now)).ConfigureAwait(false);
            else
                await ExecuteAsync(c,t,"UPDATE Users SET ActiveFlag=0,RowVersion=RowVersion+1 WHERE LinkedPersonId=$person; UPDATE UserSessions SET RevokedFlag=1 WHERE UserId IN(SELECT UserId FROM Users WHERE LinkedPersonId=$person)",("$person",p.PersonId),("$now",Now)).ConfigureAwait(false);
        }
        Require(Day==change.EffectiveDate,"تغير يوم العمل؛ أعد المعاينة.",BridgeErrorCodes.ConflictStale);
        return new Mutation<MovementResult>("PersonnelMovements",requestId,change.Type==1?"نقل داخلي":change.Type==2?"نقل خارجي":"فصل فرد: "+change.Reason,state.Persons,new(ids,ids.Count,change.EffectiveDate,change.Type));
    },token);
}
