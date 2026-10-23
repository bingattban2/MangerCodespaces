using System.Security.Cryptography;
using System.Text;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteRegistryStore
{
    private const string SiteSelect = """
        SELECT s.SiteId,s.VisibleName,s.IsSystemSite,s.ActiveFlag,s.RowVersion,
          (SELECT COUNT(*) FROM Persons p JOIN PersonAssignments a ON a.PersonId=p.PersonId
           WHERE p.ActiveFlag=1 AND p.OrganizationalStatus=1 AND a.SiteId=s.SiteId
             AND a.ValidFromLocalDate<=$day AND (a.ValidToLocalDate IS NULL OR a.ValidToLocalDate>$day))
        FROM Sites s
        """;
    private static SiteDto Site(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetInt32(2) == 1, r.GetInt32(3) == 1, r.GetInt64(4), r.GetInt64(5));
    private async Task<SiteDto> SiteAsync(SqliteConnection c, SqliteTransaction? t, string id)
    {
        var rows = await QueryAsync(c, t, SiteSelect + " WHERE s.SiteId=$id", Site, ("$day", Day), ("$id", id)).ConfigureAwait(false);
        Require(rows.Count == 1, "الموقع غير موجود.", BridgeErrorCodes.MissingData); return rows[0];
    }
    public Task<ServiceResult<IReadOnlyList<SiteDto>>> QuerySitesAsync(string nonce, CatalogQuery query, CancellationToken token = default) => ReadAsync<IReadOnlyList<SiteDto>>(nonce, async (c, _) =>
        await QueryAsync(c, null, SiteSelect + " WHERE ($all=1 OR s.ActiveFlag=1) ORDER BY s.IsSystemSite DESC,s.NormalizedName,s.SiteId", Site, ("$day", Day), ("$all", query.IncludeInactive ? 1 : 0)).ConfigureAwait(false), token);
    private async Task UniqueSiteAsync(SqliteConnection c, SqliteTransaction t, string name, string? except = null)
    {
        Text(name, 100, "اسم الموقع مطلوب ولا يتجاوز مائة حرف.");
        var normalized = ArabicTextNormalizer.NormalizeForSearch(name);
        Require(normalized.Length > 0, "اسم الموقع غير صحيح.");
        Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Sites WHERE ActiveFlag=1 AND NormalizedName=$name AND ($id IS NULL OR SiteId<>$id)", ("$name", normalized), ("$id", except)).ConfigureAwait(false)) == 0, "اسم الموقع مستخدم بالفعل.", BridgeErrorCodes.ConflictStale);
    }
    public Task<ServiceResult<SiteDto>> CreateSiteAsync(string nonce, string requestId, CreateSiteCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "SITE_CREATED", command, async (c, t, actor) =>
    {
        await UniqueSiteAsync(c, t, command.Name).ConfigureAwait(false); var id = Id("SITE");
        await ExecuteAsync(c, t, """
            INSERT INTO Sites(SiteId,VisibleName,NormalizedName,CreatedAtUtc) VALUES($id,$name,$normalized,$now);
            INSERT INTO SiteNameHistory(SiteId,VisibleNameSnapshot,ValidFromLocalDate,ChangedByUserId,RecordedAtUtc) VALUES($id,$name,$day,$actor,$now);
            """, ("$id", id), ("$name", command.Name.Trim()), ("$normalized", ArabicTextNormalizer.NormalizeForSearch(command.Name)), ("$now", Now), ("$day", Day), ("$actor", actor.UserId)).ConfigureAwait(false);
        return new Mutation<SiteDto>("Sites", id, "إضافة موقع: " + command.Name.Trim(), null, await SiteAsync(c, t, id).ConfigureAwait(false));
    }, token);
    public Task<ServiceResult<SiteDto>> RenameSiteAsync(string nonce, string requestId, RenameSiteCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "SITE_RENAMED", command, async (c, t, actor) =>
    {
        var before = await SiteAsync(c, t, command.SiteId).ConfigureAwait(false);
        Require(!before.SystemProtected && before.Active, "موقع غير محدد محمي، والموقع المعطل لا يقبل التعديل.", BridgeErrorCodes.Unauthorized);
        Require(before.Version == command.ExpectedVersion, "تغير الموقع؛ حدّث البيانات.", BridgeErrorCodes.ConflictStale);
        await UniqueSiteAsync(c, t, command.Name, command.SiteId).ConfigureAwait(false);
        Require(before.DisplayName != command.Name.Trim(), "لم يتغير اسم الموقع.");
        var latest = Convert.ToString(await ScalarAsync(c, t, "SELECT MAX(ValidFromLocalDate) FROM SiteNameHistory WHERE SiteId=$id", ("$id", command.SiteId)).ConfigureAwait(false));
        Require(!string.IsNullOrEmpty(latest) && string.CompareOrdinal(latest, Day) < 0, "للموقع اسم مسجل اليوم؛ يتاح التغيير من يوم العمل التالي حفاظاً على التاريخ.", BridgeErrorCodes.ConflictStale);
        await ExecuteAsync(c, t, """
            UPDATE SiteNameHistory SET ValidToLocalDate=$day WHERE SiteId=$id AND ValidToLocalDate IS NULL;
            INSERT INTO SiteNameHistory(SiteId,VisibleNameSnapshot,ValidFromLocalDate,ChangedByUserId,RecordedAtUtc) VALUES($id,$name,$day,$actor,$now);
            UPDATE Sites SET VisibleName=$name,NormalizedName=$normalized,RowVersion=RowVersion+1 WHERE SiteId=$id AND RowVersion=$version;
            """, ("$id", command.SiteId), ("$name", command.Name.Trim()), ("$normalized", ArabicTextNormalizer.NormalizeForSearch(command.Name)), ("$day", Day), ("$actor", actor.UserId), ("$now", Now), ("$version", command.ExpectedVersion)).ConfigureAwait(false);
        return new Mutation<SiteDto>("Sites", command.SiteId, "تعديل موقع: " + before.DisplayName, before, await SiteAsync(c, t, command.SiteId).ConfigureAwait(false));
    }, token);
    private sealed record SiteMember(string PersonId, long Version, string Name, string Number, string AssignmentId, string From, long Attendance, long Movements);
    private Task<List<SiteMember>> SiteMembersAsync(SqliteConnection c, SqliteTransaction? t, string id) => QueryAsync(c, t, """
        SELECT p.PersonId,p.RowVersion,p.CurrentVisibleName,p.CurrentServiceNumberA,a.AssignmentId,a.ValidFromLocalDate,
          (SELECT COUNT(*) FROM AttendanceRecords x WHERE x.PersonId=p.PersonId AND x.AttendanceLocalDate=$day),
          (SELECT COUNT(*) FROM PersonnelMovements m WHERE m.PersonId=p.PersonId AND m.EffectiveLocalDate=$day)
        FROM Persons p JOIN PersonAssignments a ON a.PersonId=p.PersonId
        WHERE p.ActiveFlag=1 AND p.OrganizationalStatus=1 AND a.SiteId=$id AND a.ValidFromLocalDate<=$day
          AND (a.ValidToLocalDate IS NULL OR a.ValidToLocalDate>$day) ORDER BY p.PersonId
        """, r => new SiteMember(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt64(6), r.GetInt64(7)), ("$id", id), ("$day", Day));
    private string ImpactHash(SiteDto site, IReadOnlyList<SiteMember> members) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { Day, site, members }))));
    private bool Blocks(SiteMember p) => string.CompareOrdinal(p.From, Day) >= 0 || p.Attendance != 0 || p.Movements != 0;
    public Task<ServiceResult<SiteImpactDto>> PreviewDisableSiteAsync(string nonce, SiteImpactQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, _) =>
    {
        var site = await SiteAsync(c, null, query.SiteId).ConfigureAwait(false);
        Require(!site.SystemProtected && site.Active, "لا يمكن تعطيل موقع غير محدد أو موقع معطل.", BridgeErrorCodes.Unauthorized);
        Require(site.Version == query.ExpectedVersion, "تغير الموقع؛ حدّث البيانات.", BridgeErrorCodes.ConflictStale);
        var members = await SiteMembersAsync(c, null, query.SiteId).ConfigureAwait(false);
        return new SiteImpactDto(site, ImpactHash(site, members), members.Count, members.Count(Blocks), "غير محدد", Day);
    }, token);
    public Task<ServiceResult<SiteDto>> DisableSiteAsync(string nonce, string requestId, DisableSiteCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "SITE_DISABLED", command, async (c, t, actor) =>
    {
        Text(command.Reason, 500, "سبب تعطيل الموقع مطلوب.");
        var before = await SiteAsync(c, t, command.SiteId).ConfigureAwait(false);
        Require(!before.SystemProtected && before.Active, "موقع غير محدد محمي ولا يمكن تعطيله.", BridgeErrorCodes.Unauthorized);
        Require(before.Version == command.ExpectedVersion, "تغير الموقع؛ حدّث البيانات.", BridgeErrorCodes.ConflictStale);
        var members = await SiteMembersAsync(c, t, command.SiteId).ConfigureAwait(false);
        Require(command.ImpactHash == ImpactHash(before, members), "تغير كشف الموقع منذ المعاينة؛ أعد المعاينة قبل التأكيد.", BridgeErrorCodes.ConflictStale);
        Require(!members.Any(Blocks), "لا يمكن تغيير انتساب فرد بدأ انتسابه أو اعتمد تمامه أو تنقل اليوم؛ نفذ العملية في يوم عمل لاحق.", BridgeErrorCodes.ConflictStale);
        foreach (var member in members)
        {
            var movement = Id("MOV");
            await ExecuteAsync(c, t, """
                UPDATE PersonAssignments SET ValidToLocalDate=$day WHERE AssignmentId=$assignment;
                INSERT INTO PersonnelMovements(MovementId,PersonId,PersonNameSnapshot,ServiceNumberASnapshot,MovementType,SourceSiteId,SourceSiteNameSnapshot,DestinationSiteId,DestinationSiteNameSnapshot,EffectiveLocalDate,CommittedAtUtc,ActorUserId,IdempotencyKey)
                VALUES($move,$person,$name,$number,1,$source,$sourceName,'SITE-UNSPECIFIED','غير محدد',$day,$now,$actor,$key);
                INSERT INTO PersonAssignments(AssignmentId,PersonId,SiteId,ValidFromLocalDate,SourceMovementId,CreatedAtUtc) VALUES($newAssignment,$person,'SITE-UNSPECIFIED',$day,$move,$now);
                UPDATE Persons SET RowVersion=RowVersion+1 WHERE PersonId=$person;
                """, ("$day", Day), ("$assignment", member.AssignmentId), ("$move", movement), ("$person", member.PersonId), ("$name", member.Name), ("$number", member.Number), ("$source", before.SiteId), ("$sourceName", before.DisplayName), ("$now", Now), ("$actor", actor.UserId), ("$key", requestId + ":" + member.PersonId), ("$newAssignment", Id("ASN"))).ConfigureAwait(false);
        }
        await ExecuteAsync(c, t, "UPDATE Sites SET ActiveFlag=0,RowVersion=RowVersion+1 WHERE SiteId=$id", ("$id", before.SiteId)).ConfigureAwait(false);
        return new Mutation<SiteDto>("Sites", before.SiteId, "تعطيل موقع وإعادة انتساب القوة إلى غير محدد: " + command.Reason, new { Site = before, Members = members }, await SiteAsync(c, t, before.SiteId).ConfigureAwait(false));
    }, token);
    private const string StatusSelect = """
        SELECT s.StatusId,s.VisibleName,s.ShortSymbol,s.ChosenColorHex,s.ActiveFlag,s.RowVersion,s.SortOrder,
          (SELECT COUNT(*) FROM AttendanceRecords a WHERE a.StatusId=s.StatusId)
        FROM AttendanceStatusDefinitions s
        """;
    private static CatalogStatusDto Status(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4) == 1, r.GetInt64(5), r.GetInt32(6), r.GetInt64(7));
    private async Task<CatalogStatusDto> StatusAsync(SqliteConnection c, SqliteTransaction? t, string id)
    {
        var rows = await QueryAsync(c, t, StatusSelect + " WHERE s.StatusId=$id", Status, ("$id", id)).ConfigureAwait(false);
        Require(rows.Count == 1, "الحالة غير موجودة.", BridgeErrorCodes.MissingData); return rows[0];
    }
    public Task<ServiceResult<IReadOnlyList<CatalogStatusDto>>> QueryStatusesAsync(string nonce, CatalogQuery query, CancellationToken token = default) => ReadAsync<IReadOnlyList<CatalogStatusDto>>(nonce, async (c, _) =>
        await QueryAsync(c, null, StatusSelect + " WHERE ($all=1 OR s.ActiveFlag=1) ORDER BY s.SortOrder,s.NormalizedName,s.StatusId", Status, ("$all", query.IncludeInactive ? 1 : 0)).ConfigureAwait(false), token);
    private static void ValidateStatus(string name, string symbol, string color, int order)
    {
        Text(name, 100, "اسم الحالة مطلوب ولا يتجاوز مائة حرف."); Text(symbol, 8, "رمز الحالة مطلوب ولا يتجاوز ثمانية أحرف.");
        Require(ArabicTextNormalizer.NormalizeForSearch(name).Length > 0 && color.Length == 7 && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0 && order is >= 0 and <= 10000, "اللون أو ترتيب الحالة غير صحيح.");
    }
    private static async Task UniqueStatusAsync(SqliteConnection c, SqliteTransaction t, string name, string symbol, string? except = null) =>
        Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AttendanceStatusDefinitions WHERE ActiveFlag=1 AND ($id IS NULL OR StatusId<>$id) AND (NormalizedName=$name OR ShortSymbol=$symbol)", ("$name", ArabicTextNormalizer.NormalizeForSearch(name)), ("$symbol", symbol.Trim()), ("$id", except)).ConfigureAwait(false)) == 0, "اسم الحالة أو رمزها مستخدم بالفعل.", BridgeErrorCodes.ConflictStale);
    public Task<ServiceResult<CatalogStatusDto>> CreateStatusAsync(string nonce, string requestId, CreateStatusCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "STATUS_CREATED", command, async (c, t, _) =>
    {
        ValidateStatus(command.Name, command.Symbol, command.Color, command.SortOrder); await UniqueStatusAsync(c, t, command.Name, command.Symbol).ConfigureAwait(false); var id = Id("STS");
        await ExecuteAsync(c, t, """
            INSERT INTO AttendanceStatusDefinitions(StatusId,VisibleName,NormalizedName,ShortSymbol,ChosenColorHex,SortOrder,CreatedAtUtc) VALUES($id,$name,$normalized,$symbol,$color,$order,$now);
            INSERT INTO AttendanceStatusNameHistory(StatusId,VisibleNameSnapshot,ShortSymbolSnapshot,ColorSnapshot,ValidFromLocalDate,RecordedAtUtc) VALUES($id,$name,$symbol,$color,$day,$now);
            """, ("$id", id), ("$name", command.Name.Trim()), ("$normalized", ArabicTextNormalizer.NormalizeForSearch(command.Name)), ("$symbol", command.Symbol.Trim()), ("$color", command.Color.ToUpperInvariant()), ("$order", command.SortOrder), ("$now", Now), ("$day", Day)).ConfigureAwait(false);
        return new Mutation<CatalogStatusDto>("AttendanceStatusDefinitions", id, "إضافة حالة: " + command.Name.Trim(), null, await StatusAsync(c, t, id).ConfigureAwait(false));
    }, token);
    public Task<ServiceResult<CatalogStatusDto>> UpdateStatusAsync(string nonce, string requestId, UpdateStatusCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "STATUS_UPDATED", command, async (c, t, _) =>
    {
        ValidateStatus(command.Name, command.Symbol, command.Color, command.SortOrder); var before = await StatusAsync(c, t, command.StatusId).ConfigureAwait(false);
        Require(before.Active && before.Version == command.ExpectedVersion, "الحالة معطلة أو تغيرت؛ حدّث البيانات.", BridgeErrorCodes.ConflictStale);
        await UniqueStatusAsync(c, t, command.Name, command.Symbol, command.StatusId).ConfigureAwait(false);
        var changed = before.DisplayName != command.Name.Trim() || before.ShortSymbol != command.Symbol.Trim() || before.Color != command.Color.ToUpperInvariant();
        Require(changed || before.SortOrder != command.SortOrder, "لم تتغير بيانات الحالة.");
        if (changed)
        {
            var latest = Convert.ToString(await ScalarAsync(c, t, "SELECT MAX(ValidFromLocalDate) FROM AttendanceStatusNameHistory WHERE StatusId=$id", ("$id", command.StatusId)).ConfigureAwait(false));
            Require(!string.IsNullOrEmpty(latest) && string.CompareOrdinal(latest, Day) < 0, "للحالة تعريف مسجل اليوم؛ يتاح تغيير التعريف من يوم العمل التالي حفاظاً على التاريخ.", BridgeErrorCodes.ConflictStale);
            await ExecuteAsync(c, t, """
                UPDATE AttendanceStatusNameHistory SET ValidToLocalDate=$day WHERE StatusId=$id AND ValidToLocalDate IS NULL;
                INSERT INTO AttendanceStatusNameHistory(StatusId,VisibleNameSnapshot,ShortSymbolSnapshot,ColorSnapshot,ValidFromLocalDate,RecordedAtUtc) VALUES($id,$name,$symbol,$color,$day,$now);
                """, ("$id", command.StatusId), ("$name", command.Name.Trim()), ("$symbol", command.Symbol.Trim()), ("$color", command.Color.ToUpperInvariant()), ("$day", Day), ("$now", Now)).ConfigureAwait(false);
        }
        await ExecuteAsync(c, t, "UPDATE AttendanceStatusDefinitions SET VisibleName=$name,NormalizedName=$normalized,ShortSymbol=$symbol,ChosenColorHex=$color,SortOrder=$order,RowVersion=RowVersion+1 WHERE StatusId=$id", ("$id", command.StatusId), ("$name", command.Name.Trim()), ("$normalized", ArabicTextNormalizer.NormalizeForSearch(command.Name)), ("$symbol", command.Symbol.Trim()), ("$color", command.Color.ToUpperInvariant()), ("$order", command.SortOrder)).ConfigureAwait(false);
        return new Mutation<CatalogStatusDto>("AttendanceStatusDefinitions", before.StatusId, "تعديل حالة: " + before.DisplayName, before, await StatusAsync(c, t, before.StatusId).ConfigureAwait(false));
    }, token);
    public Task<ServiceResult<CatalogStatusDto>> DisableStatusAsync(string nonce, string requestId, DisableStatusCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "STATUS_DISABLED", command, async (c, t, _) =>
    {
        var before = await StatusAsync(c, t, command.StatusId).ConfigureAwait(false);
        Require(before.Active && before.Version == command.ExpectedVersion, "الحالة معطلة أو تغيرت؛ حدّث البيانات.", BridgeErrorCodes.ConflictStale);
        Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AttendanceStatusDefinitions WHERE ActiveFlag=1").ConfigureAwait(false)) > 1, "يجب إبقاء حالة تحضير فعالة واحدة على الأقل.");
        await ExecuteAsync(c, t, "UPDATE AttendanceStatusDefinitions SET ActiveFlag=0,RowVersion=RowVersion+1 WHERE StatusId=$id", ("$id", before.StatusId)).ConfigureAwait(false);
        return new Mutation<CatalogStatusDto>("AttendanceStatusDefinitions", before.StatusId, "تعطيل حالة: " + before.DisplayName, before, await StatusAsync(c, t, before.StatusId).ConfigureAwait(false));
    }, token);
}
