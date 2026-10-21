using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
using AlTamam.Domain.Policies;

namespace AlTamam.Application.Registry;

public sealed class PersonRosterService(IRegistryStore store)
{
    public Task<ServiceResult<PersonPageDto>> QueryAsync(string nonce, PersonQuery query, CancellationToken token = default) => store.QueryPersonsAsync(nonce, query, token);
    public Task<ServiceResult<PersonCurrentDto>> GetAsync(string nonce, string personId, CancellationToken token = default) => store.GetPersonAsync(nonce, personId, token);
    public Task<ServiceResult<PersonCurrentDto>> CreateAsync(string nonce, string requestId, CreatePersonCommand command, CancellationToken token = default)
    {
        if (!RegistryValidation.Text(command.Name, 200) || !RegistryValidation.Text(command.ServiceNumberA, 64) || ArabicTextNormalizer.NormalizeDigitsOnly(command.ServiceNumberA).Length == 0 || !RegistryValidation.Text(command.SiteId, 100))
            return Task.FromResult(ServiceResult<PersonCurrentDto>.Fail(BridgeErrorCodes.ValidationError, "أدخل الاسم والرقم (ع) والموقع بصورة صحيحة."));
        return store.CreatePersonAsync(nonce, requestId, command with { Name = command.Name.Trim(), ServiceNumberA = command.ServiceNumberA.Trim() }, token);
    }
    public Task<ServiceResult<ArchivePreviewDto>> PreviewArchiveAsync(string nonce, ArchivePersonsCommand command, CancellationToken token = default) => store.PreviewArchiveAsync(nonce, command, token);
    public Task<ServiceResult<IdentityPreviewDto>> PreviewIdentityAsync(string nonce, IdentityUpdateCommand command, CancellationToken token = default) => store.PreviewIdentityAsync(nonce, command, token);
    public Task<ServiceResult<ArchiveResultDto>> ArchiveAsync(string nonce, string requestId, ArchivePersonsCommand command, CancellationToken token = default) => store.ArchiveAsync(nonce, requestId, command, token);
}

public static class RegistryValidation
{
    public static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
}
