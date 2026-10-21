using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlTamam.Application.Registry;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;
using AlTamam.Domain.ValueObjects;
using AlTamam.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Persistence;

public sealed partial class SqliteRegistryStore(AlTamamDbContext database, ISystemClock clock) : IRegistryStore
{
    private readonly SqliteAuthenticationStore _sessions = new(database, clock);
    private string Day => clock.CurrentBusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string Now => clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private sealed record Receipt<T>(string Request, T Result);
    private sealed record Mutation<T>(string EntityType, string EntityId, string Label, object? Before, T Result);
    private Task<ServiceResult<T>> WriteAsync<T>(string nonce, string requestId, string operation, object input,
        Func<SqliteConnection, SqliteTransaction, AccountDto, Task<Mutation<T>>> mutate, CancellationToken token,
        bool administratorsOnly = false) => BoundaryAsync(async () =>
    {
        Require(Guid.TryParseExact(requestId, "D", out _), "معرف الطلب غير صحيح.");
        var request = JsonSerializer.Serialize(input);
        return await database.ExecuteInWriteTransactionAsync(async (c, t) =>
        {
            var actor = await _sessions.AuthorizeAsync(c, t, nonce).ConfigureAwait(false);
            Require(!administratorsOnly || RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce((UserRole)actor.Role), "هذه العملية مسموحة للإدارة العامة والإدارة العادية فقط.", BridgeErrorCodes.Unauthorized);
            await ClockGuard.ValidateAsync(c, t, clock.UtcNow, advance: true).ConfigureAwait(false);
            var prior = await QueryAsync(c, t, "SELECT OperationCode,ActorUserId,NewValueJson FROM AuditEvents WHERE CorrelationId=$id", r => (Code: r.GetString(0), Actor: r.GetString(1), Json: r.IsDBNull(2) ? null : r.GetString(2)), ("$id", requestId)).ConfigureAwait(false);
            if (prior.Count != 0)
            {
                Require(prior.Count == 1 && prior[0].Code == operation && prior[0].Actor == actor.UserId && prior[0].Json is not null, "معرف الطلب مستخدم لعملية مختلفة.", BridgeErrorCodes.ConflictStale);
                var receipt = JsonSerializer.Deserialize<Receipt<T>>(prior[0].Json!);
                Require(receipt is not null && receipt.Request == request, "معرف الطلب مستخدم لبيانات مختلفة.", BridgeErrorCodes.ConflictStale);
                return ServiceResult<T>.Ok(receipt!.Result);
            }
            token.ThrowIfCancellationRequested();
            var change = await mutate(c, t, actor).ConfigureAwait(false);
            await SqliteAuditEventWriter.AppendAsync(c, t, operation, change.Label, new UserId(actor.UserId),
                actor.LinkedPersonId is null ? null : new PersonId(actor.LinkedPersonId), actor.Username, actor.FullName,
                change.EntityType, change.EntityId, change.Label, change.Before is null ? null : JsonSerializer.Serialize(change.Before),
                JsonSerializer.Serialize(new Receipt<T>(request, change.Result)), change.Label, null, clock.CurrentBusinessDate, clock.UtcNow, requestId, token).ConfigureAwait(false);
            return ServiceResult<T>.Ok(change.Result);
        }, token).ConfigureAwait(false);
    });
    private Task<ServiceResult<T>> ReadAsync<T>(string nonce, Func<SqliteConnection, AccountDto, Task<T>> read, CancellationToken token) => BoundaryAsync(async () =>
    {
        token.ThrowIfCancellationRequested();
        using var c = database.CreateOpenConnection();
        var actor = await _sessions.AuthorizeAsync(c, null, nonce).ConfigureAwait(false);
        return ServiceResult<T>.Ok(await read(c, actor).ConfigureAwait(false));
    });
    private static void Text(string value, int max, string message) => Require(RegistryValidation.Text(value, max), message);
    private static string Id(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");
    private const string PersonSelect = """
        SELECT p.PersonId,p.CurrentVisibleName,p.CurrentServiceNumberA,COALESCE(a.SiteId,''),COALESCE(s.VisibleName,''),p.OrganizationalStatus,p.RowVersion
        FROM Persons p LEFT JOIN PersonAssignments a ON a.AssignmentId=(
          SELECT x.AssignmentId FROM PersonAssignments x WHERE x.PersonId=p.PersonId AND x.ValidFromLocalDate<=$day
          AND (x.ValidToLocalDate IS NULL OR x.ValidToLocalDate>$day) ORDER BY x.ValidFromLocalDate DESC LIMIT 1)
        LEFT JOIN Sites s ON s.SiteId=a.SiteId
        """;
    private static PersonCurrentDto Person(SqliteDataReader r) => new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), ((OrganizationalStatus)r.GetInt32(5)).ToString(), r.GetInt64(6));
    private async Task<PersonCurrentDto> PersonAsync(SqliteConnection c, SqliteTransaction? t, string id)
    {
        var rows = await QueryAsync(c, t, PersonSelect + " WHERE p.PersonId=$id", Person, ("$day", Day), ("$id", id)).ConfigureAwait(false);
        Require(rows.Count == 1, "الفرد غير موجود.", BridgeErrorCodes.MissingData); return rows[0];
    }
    public Task<ServiceResult<PersonPageDto>> QueryPersonsAsync(string nonce, PersonQuery query, CancellationToken token = default) => ReadAsync(nonce, async (c, _) =>
    {
        Require(query.Search.Length <= 100 && query.Offset >= 0 && query.Limit is >= 1 and <= 100 && (query.SiteId?.Length ?? 0) <= 100, "معايير البحث غير صحيحة.");
        var search = "%" + ArabicTextNormalizer.NormalizeForSearch(query.Search).Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        var number = "%" + ArabicTextNormalizer.NormalizeDigitsOnly(query.Search).Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
        const string filter = @" WHERE ($inactive=1 OR (p.ActiveFlag=1 AND p.OrganizationalStatus=1)) AND (p.NormalizedName LIKE $search ESCAPE '\' OR p.NormalizedServiceNumberA LIKE $number ESCAPE '\') AND ($site IS NULL OR a.SiteId=$site)";
        var parameters = new (string, object?)[] { ("$day", Day), ("$inactive", query.IncludeInactive ? 1 : 0), ("$search", search), ("$number", number), ("$site", query.SiteId), ("$limit", query.Limit), ("$offset", query.Offset) };
        using var transaction = c.BeginTransaction(deferred: true);
        var total = Convert.ToInt64(await ScalarAsync(c, transaction, "SELECT COUNT(*) FROM (" + PersonSelect + filter + ")", parameters).ConfigureAwait(false));
        var items = await QueryAsync(c, transaction, PersonSelect + filter + " ORDER BY p.NormalizedName,p.PersonId LIMIT $limit OFFSET $offset", Person, parameters).ConfigureAwait(false);
        transaction.Commit(); return new PersonPageDto(items, total, Day);
    }, token);
    public Task<ServiceResult<PersonCurrentDto>> GetPersonAsync(string nonce, string personId, CancellationToken token = default) => ReadAsync(nonce, async (c, _) => await PersonAsync(c, null, personId).ConfigureAwait(false), token);
    public Task<ServiceResult<PersonCurrentDto>> CreatePersonAsync(string nonce, string requestId, CreatePersonCommand command, CancellationToken token = default) =>
        WriteAsync(nonce, requestId, "PERSON_CREATED", command, async (c, t, actor) =>
        {
            Text(command.Name, 200, "الاسم مطلوب ولا يتجاوز مائتي حرف."); Text(command.ServiceNumberA, 64, "الرقم (ع) غير صحيح.");
            var name = command.Name.Trim(); var number = command.ServiceNumberA.Trim(); var normalized = ArabicTextNormalizer.NormalizeDigitsOnly(number);
            Require(normalized.Length > 0 && ArabicTextNormalizer.NormalizeForSearch(name).Length > 0, "الاسم والرقم (ع) مطلوبان.");
            Require(DateOnly.TryParseExact(command.EntryDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date <= clock.CurrentBusinessDate, "تاريخ القيد يجب أن يكون صحيحاً ولا يتجاوز اليوم.");
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Sites WHERE SiteId=$site AND ActiveFlag=1", ("$site", command.SiteId)).ConfigureAwait(false)) == 1, "اختر موقعاً فعالاً.");
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM SiteNameHistory WHERE SiteId=$site AND ValidFromLocalDate<=$date AND (ValidToLocalDate IS NULL OR ValidToLocalDate>$date)", ("$site", command.SiteId), ("$date", command.EntryDate)).ConfigureAwait(false)) == 1, "تاريخ القيد يسبق سجل الموقع المختار؛ اختر تاريخاً يغطيه سجل الموقع.");
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Persons WHERE ActiveFlag=1 AND NormalizedServiceNumberA=$n", ("$n", normalized)).ConfigureAwait(false)) == 0, "الرقم (ع) مستخدم لفرد فعال بالفعل.", BridgeErrorCodes.DuplicateServiceNumber);
            var id = Id("PER");
            await ExecuteAsync(c, t, """
                INSERT INTO Persons(PersonId,CurrentVisibleName,NormalizedName,CurrentServiceNumberA,NormalizedServiceNumberA,FirstRecordedLocalDate,FirstRecordedAtUtc)
                VALUES($id,$name,$normalizedName,$number,$normalizedNumber,$date,$now);
                INSERT INTO PersonIdentityHistory(PersonId,NameSnapshot,ServiceNumberASnapshot,ValidFromLocalDate,ChangedByUserId,RecordedAtUtc,Reason)
                VALUES($id,$name,$number,$date,$actor,$now,'قيد فرد');
                INSERT INTO PersonAssignments(AssignmentId,PersonId,SiteId,ValidFromLocalDate,CreatedAtUtc) VALUES($assignment,$id,$site,$date,$now);
                """, ("$id", id), ("$name", name), ("$normalizedName", ArabicTextNormalizer.NormalizeForSearch(name)), ("$number", number), ("$normalizedNumber", normalized), ("$date", command.EntryDate), ("$now", Now), ("$actor", actor.UserId), ("$assignment", Id("ASN")), ("$site", command.SiteId)).ConfigureAwait(false);
            return new Mutation<PersonCurrentDto>("Persons", id, "إضافة فرد: " + name, null, await PersonAsync(c, t, id).ConfigureAwait(false));
        }, token);
    private static void ValidateSelection(ArchivePersonsCommand command)
    {
        Require(command.Persons is { Count: >= 1 and <= 500 } && command.Persons.All(p => RegistryValidation.Text(p.Id, 100) && p.Version > 0) && command.Persons.Select(p => p.Id).Distinct().Count() == command.Persons.Count, "اختر من فرد واحد إلى خمسمائة فرد دون تكرار.");
        Text(command.Reason, 500, "سبب الأرشفة مطلوب ولا يتجاوز خمسمائة حرف.");
    }
    public Task<ServiceResult<IdentityPreviewDto>> PreviewIdentityAsync(string nonce, IdentityUpdateCommand command, CancellationToken token = default) => ReadAsync(nonce, async (c, _) =>
    {
        Text(command.Name, 200, "الاسم غير صحيح."); Text(command.ServiceNumberA, 64, "الرقم (ع) غير صحيح."); Text(command.Reason, 500, "سبب التعديل مطلوب.");
        Require(command.EffectiveDate == Day, "تعديل الهوية نافذ من يوم العمل الحالي فقط.", BridgeErrorCodes.HistoricDayLocked);
        var before = await PersonAsync(c, null, command.PersonId).ConfigureAwait(false);
        Require(before.EntityVersion == command.ExpectedVersion, "تغيرت بيانات الفرد؛ حدّث الكشف.", BridgeErrorCodes.ConflictStale);
        Require(Convert.ToInt64(await ScalarAsync(c, null, "SELECT COUNT(*) FROM Persons WHERE PersonId=$id AND ActiveFlag=1", ("$id", command.PersonId)).ConfigureAwait(false)) == 1, "الفرد غير موجود في القوة الفعالة.", BridgeErrorCodes.MissingData);
        Require(before.DisplayName != command.Name.Trim() || before.ServiceNumberA != command.ServiceNumberA.Trim(), "لم تتغير بيانات الفرد.");
        Require(Convert.ToInt64(await ScalarAsync(c, null, "SELECT COUNT(*) FROM Persons WHERE ActiveFlag=1 AND PersonId<>$id AND NormalizedServiceNumberA=$number", ("$id", command.PersonId), ("$number", ArabicTextNormalizer.NormalizeDigitsOnly(command.ServiceNumberA))).ConfigureAwait(false)) == 0, "الرقم (ع) مستخدم بالفعل.", BridgeErrorCodes.DuplicateServiceNumber);
        var latest = Convert.ToString(await ScalarAsync(c, null, "SELECT MAX(ValidFromLocalDate) FROM PersonIdentityHistory WHERE PersonId=$id", ("$id", command.PersonId)).ConfigureAwait(false));
        Require(!string.IsNullOrEmpty(latest) && string.CompareOrdinal(latest, Day) < 0, "للفرد هوية مسجلة اليوم؛ يتاح التعديل من يوم العمل التالي حفاظاً على التاريخ.", BridgeErrorCodes.ConflictStale);
        return new IdentityPreviewDto(before, command.Name.Trim(), command.ServiceNumberA.Trim(), Day, command.Reason.Trim());
    }, token);
    private async Task<ArchivePreviewDto> ArchivePreviewAsync(SqliteConnection c, SqliteTransaction? t, ArchivePersonsCommand command)
    {
        ValidateSelection(command); var people = new List<PersonCurrentDto>(); long attendance = 0, movements = 0, accounts = 0;
        foreach (var selected in command.Persons)
        {
            var p = await PersonAsync(c, t, selected.Id).ConfigureAwait(false);
            Require(p.EntityVersion == selected.Version, "تغيرت بيانات فرد مختار؛ حدّث الكشف.", BridgeErrorCodes.ConflictStale);
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Persons WHERE PersonId=$id AND ActiveFlag=1", ("$id", p.PersonId)).ConfigureAwait(false)) == 1, "الفرد مؤرشف بالفعل.", BridgeErrorCodes.ConflictStale);
            people.Add(p);
            attendance += Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM AttendanceRecords WHERE PersonId=$id", ("$id", p.PersonId)).ConfigureAwait(false));
            movements += Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM PersonnelMovements WHERE PersonId=$id", ("$id", p.PersonId)).ConfigureAwait(false));
            accounts += Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Users WHERE LinkedPersonId=$id AND ActiveFlag=1", ("$id", p.PersonId)).ConfigureAwait(false));
        }
        return new ArchivePreviewDto(people, attendance, movements, accounts);
    }
    public Task<ServiceResult<ArchivePreviewDto>> PreviewArchiveAsync(string nonce, ArchivePersonsCommand command, CancellationToken token = default) => ReadAsync(nonce, async (c, actor) =>
    {
        Require(RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce((UserRole)actor.Role), "المشرف لا يملك صلاحية حذف أسماء القوة.", BridgeErrorCodes.Unauthorized);
        return await ArchivePreviewAsync(c, null, command).ConfigureAwait(false);
    }, token);
    public Task<ServiceResult<ArchiveResultDto>> ArchiveAsync(string nonce, string requestId, ArchivePersonsCommand command, CancellationToken token = default) => WriteAsync(nonce, requestId, "PERSONS_ARCHIVED", command, async (c, t, actor) =>
    {
        var before = await ArchivePreviewAsync(c, t, command).ConfigureAwait(false);
        Require(!command.Persons.Any(p => p.Id == actor.LinkedPersonId), "لا يمكن أرشفة الفرد المرتبط بحسابك أثناء جلستك.", BridgeErrorCodes.Unauthorized);
        foreach (var person in command.Persons)
        {
            Require(Convert.ToInt64(await ScalarAsync(c, t, "SELECT COUNT(*) FROM Users WHERE LinkedPersonId=$id AND Role=1 AND ActiveFlag=1", ("$id", person.Id)).ConfigureAwait(false)) == 0, "لا يمكن أرشفة حساب الإدارة العامة المرتبط.", BridgeErrorCodes.Unauthorized);
            // Close at tomorrow's boundary so today's committed attendance remains anchored.
            await ExecuteAsync(c, t, """
                UPDATE Persons SET ActiveFlag=0,OrganizationalStatus=4,RowVersion=RowVersion+1 WHERE PersonId=$id AND RowVersion=$version;
                UPDATE PersonAssignments SET ValidToLocalDate=$end WHERE PersonId=$id AND ValidToLocalDate IS NULL;
                UPDATE Users SET ActiveFlag=0,RowVersion=RowVersion+1 WHERE LinkedPersonId=$id AND ActiveFlag=1;
                UPDATE UserSessions SET RevokedFlag=1 WHERE UserId IN (SELECT UserId FROM Users WHERE LinkedPersonId=$id);
                """, ("$id", person.Id), ("$version", person.Version), ("$end", clock.CurrentBusinessDate.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).ConfigureAwait(false);
        }
        return new Mutation<ArchiveResultDto>("Persons", requestId, "أرشفة أسماء من القوة: " + command.Reason, before, new(command.Persons.Select(p => p.Id).ToArray()));
    }, token, administratorsOnly: true);
}
