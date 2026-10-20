namespace AlTamam.Contracts.Dtos;

public sealed record AccountDto(string UserId, string Username, int Role, string? LinkedPersonId, string? FullName, bool Active, long Version);
public sealed record SessionDto(string SessionNonce, AccountDto Account, string ExpiresAtUtc);
public sealed record EnrollmentCommand(string Password, bool RecoveryAcknowledged, string RequestId);
public sealed record LoginCommand(string Username, string Password, string RequestId);
public sealed record CreateAccountCommand(string SessionNonce, string Username, string Password, int Role, string LinkedPersonId, string RequestId);
public sealed record ChangePasswordCommand(string SessionNonce, string TargetUserId, string CurrentPassword, string NewPassword, long ExpectedVersion, string RequestId);
public sealed record DisableAccountCommand(string SessionNonce, string TargetUserId, long ExpectedVersion, string RequestId);
public sealed record AccountListQuery(string SessionNonce, string Search, int Offset = 0, int Limit = 50);
