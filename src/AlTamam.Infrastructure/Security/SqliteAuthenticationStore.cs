using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AlTamam.Application.Authentication;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;
using AlTamam.Domain.ValueObjects;
using AlTamam.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using static AlTamam.Infrastructure.Persistence.SqliteOperations;

namespace AlTamam.Infrastructure.Security;

public sealed class SqliteAuthenticationStore(AlTamamDbContext database, ISystemClock clock) : IAuthenticationStore
{
    private sealed record Credential(AccountDto Account, string Hash, string Salt, int Failures, string? LockedUntil);
    private static readonly Lazy<(string HashBase64, string SaltBase64)> DummyCredential = new(() => PasswordSecurity.HashPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(32))));
    private const string AccountSelect = """
        SELECT u.UserId,u.Username,u.Role,u.LinkedPersonId,
          (SELECT h.NameSnapshot FROM PersonIdentityHistory h WHERE h.PersonId=u.LinkedPersonId
           AND h.ValidFromLocalDate<=$day AND (h.ValidToLocalDate IS NULL OR h.ValidToLocalDate>$day)
           ORDER BY h.ValidFromLocalDate DESC LIMIT 1),u.ActiveFlag,u.RowVersion,
          u.CredentialHash,u.PasswordSalt,u.FailedLoginCount,u.LockedUntilUtc
        FROM Users u
        """;
    private string Now => clock.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private string Day => clock.CurrentBusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static AccountDto Account(SqliteDataReader r) => new(r.GetString(0),r.GetString(1),r.GetInt32(2),r.IsDBNull(3)?null:r.GetString(3),r.IsDBNull(4)?null:r.GetString(4),r.GetInt32(5)==1,r.GetInt64(6));
    private static Credential ReadCredential(SqliteDataReader r) => new(Account(r),r.GetString(7),r.GetString(8),r.GetInt32(9),r.IsDBNull(10)?null:r.GetString(10));
    private async Task<Credential?> FindAsync(SqliteConnection c, SqliteTransaction? t, string id) =>
        (await QueryAsync(c,t,AccountSelect+" WHERE u.UserId=$id",ReadCredential,("$id",id),("$day",Day)).ConfigureAwait(false)).SingleOrDefault();

    internal async Task<AccountDto> AuthorizeAsync(SqliteConnection c, SqliteTransaction? t, string nonce)
    {
        Require(!string.IsNullOrWhiteSpace(nonce) && nonce.Length==64,"الجلسة غير صالحة.",BridgeErrorCodes.Unauthorized);
        var rows=await QueryAsync(c,t,"""
            SELECT u.UserId,s.IssuedAtUtc,s.ExpiresAtUtc FROM UserSessions s JOIN Users u ON u.UserId=s.UserId
            WHERE s.SessionNonce=$nonce AND s.RevokedFlag=0 AND u.ActiveFlag=1
              AND u.RequiresFirstRunSetup=0 AND s.Role=u.Role AND u.Role IN (1,2,3)
              AND ((u.Role=1 AND u.LinkedPersonId IS NULL) OR (u.LinkedPersonId IS NOT NULL AND EXISTS(SELECT 1 FROM Persons p WHERE p.PersonId=u.LinkedPersonId AND p.ActiveFlag=1)))
            """,r=>(Id:r.GetString(0),Issued:r.GetString(1),Expires:r.GetString(2)),("$nonce",nonce)).ConfigureAwait(false);
        Require(rows.Count==1,"انتهت الجلسة أو تم تعطيل الحساب.",BridgeErrorCodes.Unauthorized);
        await ClockGuard.ValidateAsync(c,t,clock.UtcNow).ConfigureAwait(false);
        var row=rows[0];
        Require(DateTimeOffset.TryParse(row.Issued,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var issued)&&DateTimeOffset.TryParse(row.Expires,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var expires)&&clock.UtcNow>=issued&&clock.UtcNow<expires,"انتهت صلاحية الجلسة.",BridgeErrorCodes.Unauthorized);
        var account=await FindAsync(c,t,row.Id).ConfigureAwait(false);
        Require(account is not null,"الحساب غير موجود.",BridgeErrorCodes.Unauthorized);
        return account!.Account;
    }
    private Task AuditAsync(SqliteConnection c,SqliteTransaction t,AccountDto actor,string operation,string target,string description,object? before,object? after,string request) =>
        SqliteAuditEventWriter.AppendAsync(c,t,operation,description,new UserId(actor.UserId),actor.LinkedPersonId is null?null:new PersonId(actor.LinkedPersonId),actor.Username,actor.FullName,"Users",target,description,before is null?null:JsonSerializer.Serialize(before),after is null?null:JsonSerializer.Serialize(after),description,null,clock.CurrentBusinessDate,clock.UtcNow,request);
    private static void ValidateRequest(string id) => Require(Guid.TryParse(id,out _),"معرف الطلب غير صحيح.");
    private static void ValidateSecret(string password)
    {
        var check=PasswordPolicyValidator.Validate(password); Require(check.IsValid && password.Length<=256,check.ArabicErrorMessage??"كلمة المرور غير صحيحة.");
    }
    public Task<ServiceResult<bool>> NeedsEnrollmentAsync(CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        cancellationToken.ThrowIfCancellationRequested(); using var c=database.CreateOpenConnection();
        return ServiceResult<bool>.Ok(Convert.ToInt64(await ScalarAsync(c,null,"SELECT COUNT(*) FROM Users").ConfigureAwait(false))==0);
    });
    public Task<ServiceResult<AccountDto>> EnrollAsync(EnrollmentCommand command,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        ValidateSecret(command.Password);ValidateRequest(command.RequestId);Require(command.RecoveryAcknowledged,"يجب تأكيد حفظ وسيلة الاسترجاع.");
        var hashed=PasswordSecurity.HashPassword(command.Password);
        return await database.ExecuteInWriteTransactionAsync(async(c,t)=>
        {
            Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM Users").ConfigureAwait(false))==0,"تم تأسيس الإدارة العامة مسبقاً.",BridgeErrorCodes.ConflictStale);
            await ClockGuard.ValidateAsync(c,t,clock.UtcNow,advance:true).ConfigureAwait(false);
            var id="USR-"+Guid.NewGuid().ToString("N");
            await ExecuteAsync(c,t,"""
                INSERT INTO Users(UserId,LinkedPersonId,Username,NormalizedUsername,Role,CredentialHash,PasswordSalt,
                  RequiresFirstRunSetup,ActiveFlag,CreatedByUserId,CreatedByDisplaySnapshot,CreatedAtUtc)
                VALUES($id,NULL,'adwas','adwas',1,$hash,$salt,0,1,$id,'adwas',$now);
                INSERT INTO UserRoleHistory(UserId,OldRole,NewRole,ChangedByUserId,ChangedAtUtc) VALUES($id,NULL,1,$id,$now);
                """,("$id",id),("$hash",hashed.HashBase64),("$salt",hashed.SaltBase64),("$now",Now)).ConfigureAwait(false);
            var user=(await FindAsync(c,t,id).ConfigureAwait(false))!.Account;
            await AuditAsync(c,t,user,"AUTH_ENROLL",id,"تأسيس حساب الإدارة العامة",null,new{username=user.Username,role=user.Role,recoveryAcknowledged=command.RecoveryAcknowledged},command.RequestId).ConfigureAwait(false);
            return ServiceResult<AccountDto>.Ok(user);
        },cancellationToken).ConfigureAwait(false);
    });
    public Task<ServiceResult<SessionDto>> LoginAsync(LoginCommand command,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        ValidateRequest(command.RequestId);Require(!string.IsNullOrEmpty(command.Password)&&command.Password.Length<=256&&!string.IsNullOrWhiteSpace(command.Username)&&command.Username.Length<=64,"بيانات الدخول غير صحيحة.");
        return await database.ExecuteInWriteTransactionAsync(async(c,t)=>
        {
            await RequireUnusedRequestAsync(c,t,command.RequestId).ConfigureAwait(false);
            await ClockGuard.ValidateAsync(c,t,clock.UtcNow,advance:true).ConfigureAwait(false);
            var normalized=ArabicTextNormalizer.NormalizeForSearch(command.Username);
            var credentials=(await QueryAsync(c,t,AccountSelect+" WHERE u.NormalizedUsername=$username AND u.RequiresFirstRunSetup=0",ReadCredential,("$username",normalized),("$day",Day)).ConfigureAwait(false)).SingleOrDefault();
            if(credentials?.LockedUntil is string locked && DateTimeOffset.TryParse(locked,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var until)&&clock.UtcNow<until)
                return ServiceResult<SessionDto>.Fail(BridgeErrorCodes.Unauthorized,"الحساب مقفل مؤقتاً بسبب محاولات الدخول؛ حاول بعد خمس عشرة دقيقة.");
            var linkedActive=credentials is not null && (credentials.Account.LinkedPersonId is string personId
                ? Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM Persons WHERE PersonId=$p AND ActiveFlag=1",("$p",personId)).ConfigureAwait(false))==1
                : credentials.Account.Role==1);
            var dummy=credentials is null?DummyCredential.Value:default;
            var verified=PasswordSecurity.VerifyPassword(command.Password,credentials?.Hash??dummy.HashBase64,credentials?.Salt??dummy.SaltBase64);
            if(credentials is null||!credentials.Account.Active||!linkedActive||!verified)
            {
                await ExecuteAsync(c,t,"INSERT INTO FailedLoginEvents(UsernameAttempted,AttemptedAtUtc,FailureReason) VALUES($name,$now,'invalid credential')",("$name",command.Username),("$now",Now)).ConfigureAwait(false);
                if(credentials is not null)
                {
                    var failures=credentials.LockedUntil is not null?1:credentials.Failures+1;
                    await ExecuteAsync(c,t,"UPDATE Users SET FailedLoginCount=$count,LockedUntilUtc=$lock WHERE UserId=$id",("$count",failures),("$lock",failures>=5?clock.UtcNow.AddMinutes(15).ToString("O"):null),("$id",credentials.Account.UserId)).ConfigureAwait(false);
                }
                return ServiceResult<SessionDto>.Fail(BridgeErrorCodes.Unauthorized,"اسم المستخدم أو كلمة المرور غير صحيحة.");
            }
            var nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var expires=clock.UtcNow.AddHours(8).ToString("O",CultureInfo.InvariantCulture);
            await ExecuteAsync(c,t,"""
                UPDATE Users SET FailedLoginCount=0,LockedUntilUtc=NULL WHERE UserId=$user;
                INSERT INTO UserSessions(SessionNonce,UserId,Role,IssuedAtUtc,ExpiresAtUtc,RevokedFlag) VALUES($nonce,$user,$role,$now,$expires,0);
                """,("$user",credentials.Account.UserId),("$nonce",nonce),("$role",credentials.Account.Role),("$now",Now),("$expires",expires)).ConfigureAwait(false);
            await AuditAsync(c,t,credentials.Account,"AUTH_LOGIN",credentials.Account.UserId,"تسجيل الدخول",null,new{authenticated=true},command.RequestId).ConfigureAwait(false);
            return ServiceResult<SessionDto>.Ok(new SessionDto(nonce,credentials.Account,expires));
        },cancellationToken).ConfigureAwait(false);
    });
    public Task<ServiceResult<AccountDto>> ValidateSessionAsync(string nonce,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        cancellationToken.ThrowIfCancellationRequested(); using var c=database.CreateOpenConnection();
        return ServiceResult<AccountDto>.Ok(await AuthorizeAsync(c,null,nonce).ConfigureAwait(false));
    });
    public Task<ServiceResult<bool>> LogoutAsync(string nonce,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
        await database.ExecuteInWriteTransactionAsync(async(c,t)=>
        {
            var actor=await AuthorizeAsync(c,t,nonce).ConfigureAwait(false);
            await ExecuteAsync(c,t,"UPDATE UserSessions SET RevokedFlag=1 WHERE SessionNonce=$nonce",("$nonce",nonce)).ConfigureAwait(false);
            await AuditAsync(c,t,actor,"AUTH_LOGOUT",actor.UserId,"تسجيل الخروج",null,new{revoked=true},Guid.NewGuid().ToString()).ConfigureAwait(false);
            return ServiceResult<bool>.Ok(true);
        },cancellationToken).ConfigureAwait(false));
    public Task<ServiceResult<AccountDto>> CreateAccountAsync(CreateAccountCommand command,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        ValidateSecret(command.Password);ValidateRequest(command.RequestId);
        Require(!string.IsNullOrWhiteSpace(command.Username)&&command.Username.Length<=64&&!command.Username.Any(char.IsWhiteSpace)&&!command.Username.Any(char.IsControl),"اسم المستخدم غير صحيح.");
        return await database.ExecuteInWriteTransactionAsync(async(c,t)=>
        {
            var actor=await AuthorizeAsync(c,t,command.SessionNonce).ConfigureAwait(false);
            Require(ArabicTextNormalizer.NormalizeForSearch(command.Username).Length>0,"اسم المستخدم غير صحيح.");
            Require(RoleAuthorizationPolicy.CanCreateAccountWithRole((UserRole)actor.Role,(UserRole)command.Role),"لا تملك صلاحية إنشاء هذا النوع من الحسابات.",BridgeErrorCodes.Unauthorized);
            Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM Persons WHERE PersonId=$p AND ActiveFlag=1",("$p",command.LinkedPersonId)).ConfigureAwait(false))==1,"يجب ربط الحساب بفرد من القوة الفعالة.");
            var payload=JsonSerializer.Serialize(new{username=command.Username,role=command.Role,person=command.LinkedPersonId});
            var prior=await QueryAsync(c,t,"SELECT ActorUserId,OperationCode,TargetEntityId,NewValueJson FROM AuditEvents WHERE CorrelationId=$r",r=>(Actor:r.GetString(0),Op:r.GetString(1),Target:r.GetString(2),Json:r.IsDBNull(3)?null:r.GetString(3)),("$r",command.RequestId)).ConfigureAwait(false);
            if(prior.Count!=0)
            {
                var old=prior.Single();var target=await FindAsync(c,t,old.Target).ConfigureAwait(false);
                Require(old.Actor==actor.UserId&&old.Op=="AUTH_CREATE_ACCOUNT"&&old.Json==payload&&target is not null&&PasswordSecurity.VerifyPassword(command.Password,target.Hash,target.Salt),"معرف الطلب مستخدم لعملية مختلفة.",BridgeErrorCodes.ConflictStale);
                return ServiceResult<AccountDto>.Ok(target!.Account);
            }
            Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM Users WHERE NormalizedUsername=$name",("$name",ArabicTextNormalizer.NormalizeForSearch(command.Username))).ConfigureAwait(false))==0,"اسم المستخدم موجود بالفعل.",BridgeErrorCodes.DuplicateUsername);
            Require(Convert.ToInt64(await ScalarAsync(c,t,"SELECT COUNT(*) FROM Users WHERE LinkedPersonId=$p AND ActiveFlag=1",("$p",command.LinkedPersonId)).ConfigureAwait(false))==0,"للفرد حساب فعال بالفعل.",BridgeErrorCodes.ConflictStale);
            var id="USR-"+Guid.NewGuid().ToString("N");
            var hash=PasswordSecurity.HashPassword(command.Password);
            await ExecuteAsync(c,t,"""
                INSERT INTO Users(UserId,LinkedPersonId,Username,NormalizedUsername,Role,CredentialHash,PasswordSalt,
                  RequiresFirstRunSetup,ActiveFlag,CreatedByUserId,CreatedByDisplaySnapshot,CreatedAtUtc)
                VALUES($id,$person,$username,$normalized,$role,$hash,$salt,0,1,$actor,$display,$now);
                INSERT INTO UserRoleHistory(UserId,OldRole,NewRole,ChangedByUserId,ChangedAtUtc) VALUES($id,NULL,$role,$actor,$now);
                """,("$id",id),("$person",command.LinkedPersonId),("$username",command.Username),("$normalized",ArabicTextNormalizer.NormalizeForSearch(command.Username)),("$role",command.Role),("$hash",hash.HashBase64),("$salt",hash.SaltBase64),("$actor",actor.UserId),("$display",actor.Username+" - "+actor.FullName),("$now",Now)).ConfigureAwait(false);
            await AuditAsync(c,t,actor,"AUTH_CREATE_ACCOUNT",id,"إضافة مستخدم",null,new{username=command.Username,role=command.Role,person=command.LinkedPersonId},command.RequestId).ConfigureAwait(false);
            return ServiceResult<AccountDto>.Ok((await FindAsync(c,t,id).ConfigureAwait(false))!.Account);
        },cancellationToken).ConfigureAwait(false);
    });
    public Task<ServiceResult<AccountDto>> ChangePasswordAsync(ChangePasswordCommand command,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        ValidateRequest(command.RequestId);ValidateSecret(command.NewPassword);
        Require(!string.IsNullOrEmpty(command.CurrentPassword)&&command.CurrentPassword.Length<=256,"كلمة المرور الحالية مطلوبة.");
        return await database.ExecuteInWriteTransactionAsync(async(c,t)=>
        {
            var actor=await AuthorizeAsync(c,t,command.SessionNonce).ConfigureAwait(false);
            await RequireUnusedRequestAsync(c,t,command.RequestId).ConfigureAwait(false);
            var actorCredential=await FindAsync(c,t,actor.UserId).ConfigureAwait(false);var target=await FindAsync(c,t,command.TargetUserId).ConfigureAwait(false);
            Require(target is not null,"الحساب المطلوب غير موجود.",BridgeErrorCodes.MissingData);
            Require(target!.Account.UserId==actor.UserId||RoleAuthorizationPolicy.CanModifyAccount((UserRole)actor.Role,(UserRole)target.Account.Role),"لا تملك صلاحية تعديل هذا الحساب.",BridgeErrorCodes.Unauthorized);
            Require(PasswordSecurity.VerifyPassword(command.CurrentPassword,actorCredential!.Hash,actorCredential.Salt),"كلمة المرور الحالية غير صحيحة.",BridgeErrorCodes.Unauthorized);
            Require(target.Account.Version==command.ExpectedVersion,"تم تعديل الحساب؛ حدّث البيانات.",BridgeErrorCodes.ConflictStale);
            var hash=PasswordSecurity.HashPassword(command.NewPassword);
            await ExecuteAsync(c,t,"UPDATE Users SET CredentialHash=$h,PasswordSalt=$s,RowVersion=RowVersion+1,FailedLoginCount=0,LockedUntilUtc=NULL WHERE UserId=$id;UPDATE UserSessions SET RevokedFlag=1 WHERE UserId=$id",("$h",hash.HashBase64),("$s",hash.SaltBase64),("$id",command.TargetUserId)).ConfigureAwait(false);
            await AuditAsync(c,t,actor,"AUTH_CHANGE_PASSWORD",command.TargetUserId,"تغيير كلمة المرور وإبطال الجلسات",new{version=target.Account.Version},new{version=target.Account.Version+1},command.RequestId).ConfigureAwait(false);
            return ServiceResult<AccountDto>.Ok((await FindAsync(c,t,command.TargetUserId).ConfigureAwait(false))!.Account);
        },cancellationToken).ConfigureAwait(false);
    });
    public Task<ServiceResult<AccountDto>> DisableAccountAsync(DisableAccountCommand command,CancellationToken cancellationToken=default) => BoundaryAsync(async()=>
    {
        ValidateRequest(command.RequestId);
        return await database.ExecuteInWriteTransactionAsync(async(c,t)=>
        {
            var actor=await AuthorizeAsync(c,t,command.SessionNonce).ConfigureAwait(false);
            await RequireUnusedRequestAsync(c,t,command.RequestId).ConfigureAwait(false);var target=await FindAsync(c,t,command.TargetUserId).ConfigureAwait(false);
            Require(target is not null,"الحساب غير موجود.",BridgeErrorCodes.MissingData);
            Require(target!.Account.Role!=1&&target.Account.UserId!=actor.UserId&&RoleAuthorizationPolicy.CanModifyAccount((UserRole)actor.Role,(UserRole)target.Account.Role),"لا يمكن تعطيل هذا الحساب بهذه الصلاحية.",BridgeErrorCodes.Unauthorized);
            Require(target.Account.Version==command.ExpectedVersion,"تغير الحساب؛ حدّث البيانات.",BridgeErrorCodes.ConflictStale);
            await ExecuteAsync(c,t,"UPDATE Users SET ActiveFlag=0,RowVersion=RowVersion+1 WHERE UserId=$id;UPDATE UserSessions SET RevokedFlag=1 WHERE UserId=$id",("$id",command.TargetUserId)).ConfigureAwait(false);
            await AuditAsync(c,t,actor,"AUTH_DISABLE_ACCOUNT",command.TargetUserId,"تعطيل حساب المستخدم",new{active=target.Account.Active},new{active=false},command.RequestId).ConfigureAwait(false);
            return ServiceResult<AccountDto>.Ok((await FindAsync(c,t,command.TargetUserId).ConfigureAwait(false))!.Account);
        },cancellationToken).ConfigureAwait(false);
    });
    public Task<ServiceResult<IReadOnlyList<AccountDto>>> ListAccountsAsync(AccountListQuery query,CancellationToken cancellationToken=default) => BoundaryAsync<IReadOnlyList<AccountDto>>(async()=>
    {
        cancellationToken.ThrowIfCancellationRequested();using var c=database.CreateOpenConnection();var actor=await AuthorizeAsync(c,null,query.SessionNonce).ConfigureAwait(false);
        Require(RoleAuthorizationPolicy.CanAccessUsersManagement((UserRole)actor.Role),"لا تملك صلاحية إدارة المستخدمين.",BridgeErrorCodes.Unauthorized);
        Require(query.Offset>=0&&query.Limit is >=1 and <=100&&(query.Search?.Length??0)<=100,"معايير البحث غير صحيحة.");
        var rows=await QueryAsync(c,null,AccountSelect+" WHERE u.NormalizedUsername LIKE $q ORDER BY u.NormalizedUsername,u.UserId LIMIT $limit OFFSET $offset",Account,("$day",Day),("$q","%"+ArabicTextNormalizer.NormalizeForSearch(query.Search)+"%"),("$limit",query.Limit),("$offset",query.Offset)).ConfigureAwait(false);
        return ServiceResult<IReadOnlyList<AccountDto>>.Ok(rows);
    });
}
