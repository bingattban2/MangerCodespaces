using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Authentication;

public interface IAuthenticationStore
{
    Task<ServiceResult<bool>> NeedsEnrollmentAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> EnrollAsync(EnrollmentCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<SessionDto>> LoginAsync(LoginCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> ValidateSessionAsync(string nonce, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> LogoutAsync(string nonce, CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> CreateAccountAsync(CreateAccountCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> ChangePasswordAsync(ChangePasswordCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> DisableAccountAsync(DisableAccountCommand command, CancellationToken cancellationToken = default);
    Task<ServiceResult<IReadOnlyList<AccountDto>>> ListAccountsAsync(AccountListQuery query, CancellationToken cancellationToken = default);
}
