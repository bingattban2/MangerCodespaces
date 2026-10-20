using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;

namespace AlTamam.Application.Authentication;

public sealed class AuthenticationService(IAuthenticationStore store)
{
    public Task<ServiceResult<bool>> NeedsEnrollmentAsync(CancellationToken token = default) => store.NeedsEnrollmentAsync(token);
    public Task<ServiceResult<AccountDto>> EnrollAsync(EnrollmentCommand command, CancellationToken token = default)
    {
        var password = PasswordPolicyValidator.Validate(command.Password);
        if (!password.IsValid || command.Password.Length > 256 || !command.RecoveryAcknowledged || !ValidRequest(command.RequestId))
            return Task.FromResult(ServiceResult<AccountDto>.Fail(BridgeErrorCodes.ValidationError, password.ArabicErrorMessage ?? "يجب تأكيد حفظ وسيلة الاسترجاع وإكمال بيانات التهيئة."));
        return store.EnrollAsync(command, token);
    }
    public Task<ServiceResult<SessionDto>> LoginAsync(LoginCommand command, CancellationToken token = default) =>
        !ValidUsername(command.Username) || string.IsNullOrEmpty(command.Password) || command.Password.Length > 256 || !ValidRequest(command.RequestId)
            ? Task.FromResult(ServiceResult<SessionDto>.Fail(BridgeErrorCodes.ValidationError, "أدخل اسم المستخدم وكلمة المرور بصورة صحيحة."))
            : store.LoginAsync(command, token);
    public Task<ServiceResult<AccountDto>> ValidateSessionAsync(string nonce, CancellationToken token = default) => store.ValidateSessionAsync(nonce, token);
    public Task<ServiceResult<bool>> LogoutAsync(string nonce, CancellationToken token = default) => store.LogoutAsync(nonce, token);
    public Task<ServiceResult<AccountDto>> CreateAccountAsync(CreateAccountCommand command, CancellationToken token = default)
    {
        var password = PasswordPolicyValidator.Validate(command.Password);
        if (!ValidUsername(command.Username) || !password.IsValid || command.Password.Length > 256 || command.Role is < 2 or > 3 || string.IsNullOrWhiteSpace(command.LinkedPersonId) || !ValidRequest(command.RequestId))
            return Task.FromResult(ServiceResult<AccountDto>.Fail(BridgeErrorCodes.ValidationError, password.ArabicErrorMessage ?? "بيانات المستخدم أو الدور أو الفرد المرتبط غير صحيحة."));
        return store.CreateAccountAsync(command with { Username = command.Username.Trim() }, token);
    }
    public Task<ServiceResult<AccountDto>> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken token = default)
    {
        var password = PasswordPolicyValidator.Validate(command.NewPassword);
        if (!password.IsValid || command.NewPassword.Length > 256 || string.IsNullOrEmpty(command.CurrentPassword) || command.CurrentPassword.Length > 256 || command.ExpectedVersion < 1 || !ValidRequest(command.RequestId))
            return Task.FromResult(ServiceResult<AccountDto>.Fail(BridgeErrorCodes.ValidationError, password.ArabicErrorMessage ?? "بيانات تغيير كلمة المرور غير صحيحة."));
        return store.ChangePasswordAsync(command, token);
    }
    public Task<ServiceResult<AccountDto>> DisableAccountAsync(DisableAccountCommand command, CancellationToken token = default) => store.DisableAccountAsync(command, token);
    public Task<ServiceResult<IReadOnlyList<AccountDto>>> ListAccountsAsync(AccountListQuery query, CancellationToken token = default) => store.ListAccountsAsync(query, token);
    private static bool ValidRequest(string value) => Guid.TryParse(value, out _);
    private static bool ValidUsername(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && !value.Any(char.IsWhiteSpace) && !value.Any(char.IsControl);
}
