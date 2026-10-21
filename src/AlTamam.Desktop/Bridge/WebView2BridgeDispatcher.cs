using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Contracts.Events;
using AlTamam.Domain.Enums;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Desktop.Bridge;

public sealed class WebView2BridgeDispatcher(DesktopRuntime runtime)
{
    public static readonly JsonSerializerOptions JsonOptions=new()
    {
        PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=false,
        UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectNullableAnnotations=true,
        RespectRequiredConstructorParameters=true,MaxDepth=16
    };
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly Dictionary<string,(string Fingerprint,BridgeResponse Response)> _responses=new(StringComparer.Ordinal);
    private string _nonce="";
    public static bool IsTrustedOrigin(string source) => Uri.TryCreate(source,UriKind.Absolute,out var uri)&&
        uri.Scheme==Uri.UriSchemeHttps&&uri.Host.Equals(LocalWebUiOrigin.Host,StringComparison.OrdinalIgnoreCase)&&uri.Port==443&&string.IsNullOrEmpty(uri.UserInfo);
    private sealed record EmptyPayload();
    private sealed record LoginPayload(string Username,string Password);
    private sealed record EnrollmentPayload(string Password,bool RecoveryAcknowledged);
    private sealed record ListPayload(string Search="",int Offset=0,int Limit=50);
    private sealed record CreatePayload(string Username,string Password,int Role,string LinkedPersonId);
    private sealed record PasswordPayload(string TargetUserId,string CurrentPassword,string NewPassword,long ExpectedVersion);
    private sealed record DisablePayload(string TargetUserId,long ExpectedVersion);
    private sealed record PersonKeyPayload(string PersonId);
    private static T Payload<T>(BridgeRequest request) => request.Payload!.Deserialize<T>(JsonOptions)??throw new JsonException("Missing payload");
    private static BridgeResponse Fail(string id,string code,string message)=>new(id,false,ErrorCode:code,UserMessage:message);
    private static void ValidateJson(JsonElement element)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var p in element.EnumerateObject())
            {
                if(!names.Add(p.Name))throw new JsonException("Duplicate JSON property");ValidateJson(p.Value);
            }
        }
        else if(element.ValueKind==JsonValueKind.Array)foreach(var item in element.EnumerateArray())ValidateJson(item);
    }
    public async Task<BridgeResponse> DispatchAsync(string source,string json,CancellationToken cancellationToken=default)
    {
        var id="invalid";
        if(!IsTrustedOrigin(source))return Fail(id,BridgeErrorCodes.Unauthorized,"مصدر الرسالة غير موثوق.");
        if(Encoding.UTF8.GetByteCount(json)>262144)return Fail(id,BridgeErrorCodes.ValidationError,"الطلب أكبر من الحد المسموح.");
        var acquired=false;
        try {await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);acquired=true;}
        catch(OperationCanceledException){return Fail(id,BridgeErrorCodes.StorageBusy,"تم إلغاء الطلب.");}
        try
        {
            using var document=JsonDocument.Parse(json,new JsonDocumentOptions{MaxDepth=16});
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException();
            if(document.RootElement.EnumerateObject().Count(p=>p.Name=="requestId")==1 &&
               document.RootElement.TryGetProperty("requestId",out var suppliedId) && suppliedId.ValueKind==JsonValueKind.String &&
               Guid.TryParseExact(suppliedId.GetString(),"D",out var correlation))id=correlation.ToString("D");
            ValidateJson(document.RootElement);
            var request=JsonSerializer.Deserialize<BridgeRequest>(json,JsonOptions)??throw new JsonException();
            if(!Guid.TryParseExact(request.RequestId,"D",out var guid))throw new JsonException();
            id=guid.ToString("D");request=request with{RequestId=id};
            if(request.ProtocolVersion!=1||request.Payload is null||request.Action.Length>80||request.SessionNonce.Length>128)
                return Fail(id,BridgeErrorCodes.ValidationError,"عقد الرسالة غير صحيح.");
            var publicAction=request.Action is "GetProvisioningState" or "CompleteProvisioning" or "AuthenticateLocalUser";
            AccountDto? actor=null;
            if(!publicAction)
            {
                if(request.Action=="GetCurrentSession"&&request.SessionNonce.Length==0)request=request with{SessionNonce=_nonce};
                if(string.IsNullOrEmpty(_nonce)||request.SessionNonce!=_nonce)
                    return Fail(id,BridgeErrorCodes.Unauthorized,"سجل الدخول أولاً.");
                var session=await runtime.Authentication.ValidateSessionAsync(request.SessionNonce,cancellationToken).ConfigureAwait(false);
                if(!session.IsSuccess)return session.ToBridgeResponse(id);actor=session.Value!;
                if(actor.Role==3&&request.Action is "ListUserAccounts" or "CreateUserAccount" or "ChangeUserPassword" or "DeactivateUserAccount" or "GetAllowedRoles")
                    return Fail(id,BridgeErrorCodes.Unauthorized,"لا تملك صلاحية إدارة المستخدمين.");
            }
            var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request,JsonOptions))));
            if(request.Action!="GetProvisioningState"&&_responses.TryGetValue(id,out var cached))
            {
                if(cached.Fingerprint!=fingerprint)return Fail(id,BridgeErrorCodes.ConflictStale,"معرف الطلب مستخدم لبيانات مختلفة.");
                if(request.Action=="AuthenticateLocalUser"&&cached.Response.Data is SessionDto oldSession)
                {
                    var valid=await runtime.Authentication.ValidateSessionAsync(oldSession.SessionNonce,cancellationToken).ConfigureAwait(false);
                    if(!valid.IsSuccess)return valid.ToBridgeResponse(id);_nonce=oldSession.SessionNonce;
                }
                return cached.Response;
            }
            cancellationToken.ThrowIfCancellationRequested();
            BridgeResponse response;
            switch(request.Action)
            {
                case "GetProvisioningState":
                    Payload<EmptyPayload>(request);
                    var state=await runtime.Authentication.NeedsEnrollmentAsync(cancellationToken).ConfigureAwait(false);
                    response=state.IsSuccess?new(id,true,new{needsEnrollment=state.Value,username="adwas",recoveryKey=state.Value?runtime.DatabaseKey:null}):state.ToBridgeResponse(id);break;
                case "CompleteProvisioning":
                    var enrollment=Payload<EnrollmentPayload>(request);
                    response=(await runtime.Authentication.EnrollAsync(new(enrollment.Password,enrollment.RecoveryAcknowledged,id),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "AuthenticateLocalUser":
                    var credentials=Payload<LoginPayload>(request);
                    var login=await runtime.Authentication.LoginAsync(new(credentials.Username,credentials.Password,id),cancellationToken).ConfigureAwait(false);
                    if(login.IsSuccess)_nonce=login.Value!.SessionNonce;
                    response=login.ToBridgeResponse(id);break;
                case "GetCurrentSession":
                    Payload<EmptyPayload>(request);response=new(id,true,new{sessionNonce=_nonce,account=actor,businessDate=runtime.Clock.CurrentBusinessDate.ToString("yyyy-MM-dd")});break;
                case "GetNavigationPermissions":
                    Payload<EmptyPayload>(request);response=new(id,true,new{role=actor!.Role,canManageAccounts=actor.Role is 1 or 2,canArchivePersons=actor.Role is 1 or 2,canReset=actor.Role==1});break;
                case "GetAllowedRoles":
                    Payload<EmptyPayload>(request);response=actor!.Role is 1 or 2?new(id,true,actor.Role==1?new[]{2,3}:new[]{3}):Fail(id,BridgeErrorCodes.Unauthorized,"لا تملك صلاحية إدارة المستخدمين.");break;
                case "Logout":
                    Payload<EmptyPayload>(request);response=(await runtime.Authentication.LogoutAsync(request.SessionNonce,cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);
                    if(response.Success){_nonce="";_responses.Clear();response=response with{InvalidationEvents=new[]{new UiInvalidationEventPayload(UiInvalidationEvents.SessionEnded,Array.Empty<string>(),runtime.Clock.CurrentBusinessDate.ToString("yyyy-MM-dd"),1)}};}break;
                case "ListUserAccounts":
                    var query=Payload<ListPayload>(request);response=(await runtime.Authentication.ListAccountsAsync(new(request.SessionNonce,query.Search,query.Offset,query.Limit),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "CreateUserAccount":
                    var create=Payload<CreatePayload>(request);response=(await runtime.Authentication.CreateAccountAsync(new(request.SessionNonce,create.Username,create.Password,create.Role,create.LinkedPersonId,id),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "ChangeUserPassword":
                    var change=Payload<PasswordPayload>(request);response=(await runtime.Authentication.ChangePasswordAsync(new(request.SessionNonce,change.TargetUserId,change.CurrentPassword,change.NewPassword,change.ExpectedVersion,id),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);
                    if(response.Success&&change.TargetUserId==actor!.UserId)_nonce="";break;
                case "DeactivateUserAccount":
                    var disable=Payload<DisablePayload>(request);response=(await runtime.Authentication.DisableAccountAsync(new(request.SessionNonce,disable.TargetUserId,disable.ExpectedVersion,id),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "QueryPersons": case "SearchPersons": case "QueryActivePersonsBySite": case "SearchActivePersonsForAccount":
                    var peopleQuery=Payload<PersonQuery>(request);
                    if(request.Action=="SearchActivePersonsForAccount"&&actor!.Role==3){response=Fail(id,BridgeErrorCodes.Unauthorized,"لا تملك صلاحية إدارة المستخدمين.");break;}
                    if(request.Action is "QueryActivePersonsBySite" or "SearchActivePersonsForAccount")peopleQuery=peopleQuery with{IncludeInactive=false};
                    response=(await runtime.Roster.QueryAsync(request.SessionNonce,peopleQuery,cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "GetPersonSummary":
                    var personKey=Payload<PersonKeyPayload>(request);response=(await runtime.Roster.GetAsync(request.SessionNonce,personKey.PersonId,cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "CreatePerson":
                    response=(await runtime.Roster.CreateAsync(request.SessionNonce,id,Payload<CreatePersonCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "PreviewPersonIdentityUpdate":
                    response=(await runtime.Roster.PreviewIdentityAsync(request.SessionNonce,Payload<IdentityUpdateCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "UpdatePersonIdentity":
                    var identity=Payload<IdentityUpdateCommand>(request);
                    if(!DateOnly.TryParseExact(identity.EffectiveDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var effective)||identity.Name.Length>200||identity.ServiceNumberA.Length>64||identity.Reason.Length>500)
                    {response=Fail(id,BridgeErrorCodes.ValidationError,"بيانات تعديل الهوية غير صحيحة.");break;}
                    response=(await runtime.Identity.ChangeAsync(new(new PersonId(identity.PersonId),identity.Name,identity.ServiceNumberA,effective,identity.ExpectedVersion,id,request.SessionNonce,identity.Reason),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "PreviewArchivePersons":
                    response=(await runtime.Roster.PreviewArchiveAsync(request.SessionNonce,Payload<ArchivePersonsCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "ArchivePersons":
                    response=(await runtime.Roster.ArchiveAsync(request.SessionNonce,id,Payload<ArchivePersonsCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "QuerySites":
                    response=(await runtime.Configuration.SitesAsync(request.SessionNonce,Payload<CatalogQuery>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "CreateSite":
                    response=(await runtime.Configuration.CreateSiteAsync(request.SessionNonce,id,Payload<CreateSiteCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "RenameSite":
                    response=(await runtime.Configuration.RenameSiteAsync(request.SessionNonce,id,Payload<RenameSiteCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "PreviewSiteRemoval":
                    response=(await runtime.Configuration.PreviewDisableSiteAsync(request.SessionNonce,Payload<SiteImpactQuery>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "RemoveSiteAndReassignCurrentPersons":
                    response=(await runtime.Configuration.DisableSiteAsync(request.SessionNonce,id,Payload<DisableSiteCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "QueryAttendanceStatuses": case "GetActiveAttendanceStatuses":
                    var statusQuery=Payload<CatalogQuery>(request);
                    response=(await runtime.Configuration.StatusesAsync(request.SessionNonce,request.Action=="GetActiveAttendanceStatuses"?new(false):statusQuery,cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "CreateAttendanceStatus":
                    response=(await runtime.Configuration.CreateStatusAsync(request.SessionNonce,id,Payload<CreateStatusCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "UpdateAttendanceStatus":
                    response=(await runtime.Configuration.UpdateStatusAsync(request.SessionNonce,id,Payload<UpdateStatusCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                case "DeactivateOrDeleteStatus":
                    response=(await runtime.Configuration.DisableStatusAsync(request.SessionNonce,id,Payload<DisableStatusCommand>(request),cancellationToken).ConfigureAwait(false)).ToBridgeResponse(id);break;
                default:response=Fail(id,BridgeErrorCodes.ValidationError,"العملية المطلوبة غير معروفة أو غير متاحة.");break;
            }
            if(response.Success)
            {
                var eventName=request.Action switch
                {
                    "CreatePerson" or "UpdatePersonIdentity"=>UiInvalidationEvents.PersonIdentityUpdated,
                    "ArchivePersons"=>UiInvalidationEvents.PersonnelArchived,
                    "CreateSite"=>UiInvalidationEvents.SiteCreated,
                    "RenameSite" or "RemoveSiteAndReassignCurrentPersons"=>UiInvalidationEvents.SiteRenamed,
                    "CreateAttendanceStatus"=>UiInvalidationEvents.StatusDefinitionCreated,
                    "UpdateAttendanceStatus" or "DeactivateOrDeleteStatus"=>UiInvalidationEvents.StatusDefinitionChanged,
                    _=>null
                };
                if(eventName is not null)
                {
                    var affected=response.Data switch{PersonCurrentDto p=>new[]{p.PersonId},PersonHistoricIdentityDto h=>new[]{h.PersonId},ArchiveResultDto a=>a.PersonIds.ToArray(),SiteDto s=>new[]{s.SiteId},CatalogStatusDto s=>new[]{s.StatusId},_=>Array.Empty<string>()};
                    response=response with{InvalidationEvents=new[]{new UiInvalidationEventPayload(eventName,affected,runtime.Clock.CurrentBusinessDate.ToString("yyyy-MM-dd"),1)}};
                }
            }
            if(response.Success&&request.Action is "CreateUserAccount" or "ChangeUserPassword" or "DeactivateUserAccount")
            {
                var account=(AccountDto)response.Data!;
                response=response with{InvalidationEvents=new[]{new UiInvalidationEventPayload(UiInvalidationEvents.UserAccountChanged,new[]{account.UserId},runtime.Clock.CurrentBusinessDate.ToString("yyyy-MM-dd"),account.Version)}};
            }
            if(response.Success&&request.Action!="GetProvisioningState")
            {
                if(_responses.Count>=256)_responses.Remove(_responses.Keys.First());
                _responses[id]=(fingerprint,response);
            }
            return response;
        }
        catch(OperationCanceledException){return Fail(id,BridgeErrorCodes.StorageBusy,"تم إلغاء الطلب.");}
        catch(JsonException){return Fail(id,BridgeErrorCodes.ValidationError,"بيانات الرسالة غير صحيحة أو تحتوي حقولاً غير مسموحة.");}
        catch(Exception){return Fail(id,BridgeErrorCodes.BackupIntegrityFailed,"تعذر تنفيذ الطلب بأمان.");}
        finally{if(acquired)_gate.Release();}
    }
}
