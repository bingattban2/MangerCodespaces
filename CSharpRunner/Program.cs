// EXTERNAL VERIFICATION ONLY. NOT production. Requires actual .NET 10 + native SQLCipher.
// Run against a trusted checkout of the pinned commit, never against a personnel database.
using System.Text;
using System.Text.Json;
using AlTamam.Application.Rules;
using AlTamam.Application.Traceability;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;
using AlTamam.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

var results = new List<object>();
var failures = 0;
async Task Check(string title, Func<Task<bool>> test)
{
    try
    {
        bool success = await test();
        results.Add(new { name = title, status = success ? "PASS" : "FAIL" });
        if (!success) failures++;
    }
    catch (Exception ex)
    {
        results.Add(new { name = title, status = "FAIL", exception = ex.GetType().Name, detail = ex.Message });
        failures++;
    }
}
Task<bool> Ready(bool value) => Task.FromResult(value);

await Check("Supervisor denied users screen", () => Ready(!RoleAuthorizationPolicy.CanAccessUsersManagement(UserRole.Supervisor)));
await Check("General administrator can manage users", () => Ready(RoleAuthorizationPolicy.CanAccessUsersManagement(UserRole.GeneralAdministration)));
await Check("Ordinary administrator may create Supervisor", () => Ready(RoleAuthorizationPolicy.CanCreateAccountWithRole(UserRole.OrdinaryAdministration,UserRole.Supervisor)));
await Check("Ordinary administrator denied creation of OrdinaryAdministration", () => Ready(!RoleAuthorizationPolicy.CanCreateAccountWithRole(UserRole.OrdinaryAdministration,UserRole.OrdinaryAdministration)));
await Check("Supervisor denied archival", () => Ready(!RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce(UserRole.Supervisor)));
await Check("General/Ordinary allowed visible audit overlay", () => Ready(RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(UserRole.GeneralAdministration) && RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(UserRole.OrdinaryAdministration)));
await Check("Supervisor denied visible audit overlay", () => Ready(!RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(UserRole.Supervisor)));
// Observed only: this role rule still requires explicit owner signoff as part of Phase 00 Step 02.
await Check("Current source full-reset predicate is General-only (not owner approval)", () => Ready(RoleAuthorizationPolicy.CanExecuteFullDatabaseReset(UserRole.GeneralAdministration) && !RoleAuthorizationPolicy.CanExecuteFullDatabaseReset(UserRole.OrdinaryAdministration) && !RoleAuthorizationPolicy.CanExecuteFullDatabaseReset(UserRole.Supervisor)));
await Check("Password with only alphanumeric characters is rejected", () => Ready(!PasswordPolicyValidator.Validate("Password123").IsValid));
await Check("Valid strong password accepted", () => Ready(PasswordPolicyValidator.Validate("Good@12345Password").IsValid));
await Check("Arabic digits normalized", () => Ready(ArabicTextNormalizer.NormalizeDigitsOnly("۱۲٣") == "123"));
await Check("Movement day forbids attendance", () => Ready(Throws<InvalidOperationException>(() => AttendanceDayPolicy.EnsureDayCanBePrepared(new DateOnly(2026,10,19),new DateOnly(2026,10,19),true,false))));
await Check("Past day forbidden for ordinary attendance", () => Ready(Throws<InvalidOperationException>(() => AttendanceDayPolicy.EnsureDayCanBePrepared(new DateOnly(2026,10,19),new DateOnly(2026,10,18),false,false))));
await Check("Today can be prepared", () => Ready(!Throws<InvalidOperationException>(() => AttendanceDayPolicy.EnsureDayCanBePrepared(new DateOnly(2026,10,19),new DateOnly(2026,10,19),false,false))));
await Check("Half-open invalid date interval denied", () => Ready(Throws<ArgumentOutOfRangeException>(() => AssignmentPeriodPolicy.ValidateInterval(new DateOnly(2026,1,5),new DateOnly(2026,1,5)))));
await Check("Half-open adjoining date interval accepted", () => Ready(!Throws<ArgumentOutOfRangeException>(() => AssignmentPeriodPolicy.ValidateInterval(new DateOnly(2026,1,5),new DateOnly(2026,1,6)))));

var clock = new SystemClock();
var traceService = new RequirementTraceabilityService(clock);
await Check("Actual C# static trace report returns canonical tables", () =>
{
    var result = traceService.BuildAndValidateStaticReport();
    return Ready(result.IsSuccess && result.Value is { TotalCanonicalTablesCount: 26, TotalCanonicalScreensCount: 69 });
});
await Check("Missing probe is typed failure, not false success", async () =>
{
    var result = await traceService.VerifyAgainstDatabaseAsync();
    return !result.IsSuccess && result.Failure is not null;
});

var scratch = Path.Combine(Path.GetTempPath(),"AlTamam_External_Verify_"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
var dbfile = Path.Combine(scratch,"only_fictional_test_data.db");
var key = new string('A',64);
try
{
    using var db = new AlTamamDbContext(dbfile,key);
    await Check("Native SQLCipher V001 InitializeAsync succeeds",async()=> { await db.InitializeAsync(); return File.Exists(dbfile); });
    await Check("Encrypted file header is not ordinary SQLite",() => Ready(File.Exists(dbfile) && !Encoding.ASCII.GetString(File.ReadAllBytes(dbfile).Take(16).ToArray()).StartsWith("SQLite format 3",StringComparison.Ordinal)));
    await Check("Native SQLCipher reports cipher_version and integrity",() =>
    {
        using var c=db.CreateOpenConnection(); using var q=c.CreateCommand();
        q.CommandText="PRAGMA cipher_version;";
        var version=q.ExecuteScalar()?.ToString();
        q.CommandText="PRAGMA integrity_check;";
        return Ready(!string.IsNullOrWhiteSpace(version) && q.ExecuteScalar()?.ToString()=="ok");
    });
    await Check("Seeded metadata exists and no default accounts",() =>
    {
        using var c=db.CreateOpenConnection();using var q=c.CreateCommand();
        q.CommandText="SELECT (SELECT count(*) FROM Users), (SELECT count(*) FROM AttendanceStatusDefinitions), (SELECT count(*) FROM Sites WHERE SiteId='SITE-UNSPECIFIED');";
        using var r=q.ExecuteReader();
        return Ready(r.Read() && r.GetInt32(0)==0 && r.GetInt32(1)==4 && r.GetInt32(2)==1);
    });
    await Check("C# actual SQLCipher schema probe validates encrypted state", async () =>
    {
        var probe = new SqliteSchemaCatalogProbe(db, clock);
        var result = await new RequirementTraceabilityService(clock,probe).VerifyAgainstDatabaseAsync();
        return result.IsSuccess && result.Value?.DatabaseSnapshot is { EncryptionProviderActive: true, IntegrityCheckPassed: true };
    });
    await Check("Write transaction commits a real test row",async () =>
    {
        await db.ExecuteInWriteTransactionAsync(async (c,t)=>
        {
            using var q=c.CreateCommand();q.Transaction=t;
            q.CommandText="INSERT INTO Persons(PersonId,CurrentVisibleName,NormalizedName,CurrentServiceNumberA,NormalizedServiceNumberA,FirstRecordedLocalDate,FirstRecordedAtUtc) VALUES ('P-EXT-CHECK','شخص اختبار','شخص اختبار','X-CHECK','X-CHECK','2026-01-01','2026-01-01T00:00:00Z');";
            await q.ExecuteNonQueryAsync();return true;
        });
        using var c=db.CreateOpenConnection();using var q=c.CreateCommand();q.CommandText="SELECT count(*) FROM Persons WHERE PersonId='P-EXT-CHECK';";
        return Convert.ToInt32(q.ExecuteScalar())==1;
    });
    await Check("Rollback after thrown transaction preserves persisted state",async()=>
    {
        try
        {
            await db.ExecuteInWriteTransactionAsync<int>(async (c,t)=>
            {
                using var q=c.CreateCommand();q.Transaction=t;
                q.CommandText="INSERT INTO Persons(PersonId,CurrentVisibleName,NormalizedName,CurrentServiceNumberA,NormalizedServiceNumberA,FirstRecordedLocalDate,FirstRecordedAtUtc) VALUES ('P-ROLLBACK','شخص اختبار','شخص اختبار','X-ROLLBACK','X-ROLLBACK','2026-01-01','2026-01-01T00:00:00Z');";
                await q.ExecuteNonQueryAsync();throw new InvalidOperationException("intentional rollback");
            });
        }
        catch(InvalidOperationException) { /* expected */ }
        using var c=db.CreateOpenConnection();using var q=c.CreateCommand();q.CommandText="SELECT count(*) FROM Persons WHERE PersonId='P-ROLLBACK';";
        return Convert.ToInt32(q.ExecuteScalar())==0;
    });
    await Check("Wrong key rejected when reading encrypted database",() =>
    {
        try
        {
            using var wrong = new AlTamamDbContext(dbfile,new string('B',64));
            using var c = wrong.CreateOpenConnection();using var q=c.CreateCommand();
            q.CommandText="SELECT count(*) FROM sqlite_master;";
            q.ExecuteScalar();return Ready(false);
        }
        catch(SqliteException) {return Ready(true);}
        catch(InvalidOperationException) {return Ready(true);}
    });
    await Check("Correct key can reopen persistent data in new context",() =>
    {
        using var second = new AlTamamDbContext(dbfile,key);
        using var c=second.CreateOpenConnection();using var q=c.CreateCommand();
        q.CommandText="SELECT count(*) FROM Persons WHERE PersonId='P-EXT-CHECK';";
        return Ready(Convert.ToInt32(q.ExecuteScalar())==1);
    });
}
finally
{
    // On Windows SQLCipher with WAL mode may leave -wal / -shm; delete full ephemeral directory.
    try { Directory.Delete(scratch,recursive:true); } catch { /* temp cleanup best effort */ }
}
var report = new { source = "External .NET 10 C# runner, NO production changes", passed=results.Count-failures, total=results.Count, failed=failures, checks=results };
var json = JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
Console.WriteLine(json);
var outputPath = Environment.GetEnvironmentVariable("ALTAMAM_RESULTS_JSON");
if (!string.IsNullOrWhiteSpace(outputPath))
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
    await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8);
}
return failures==0 ? 0 : 1;

static bool Throws<TException>(Action action) where TException:Exception
{
    try { action(); return false; } catch (TException) { return true; }
}
