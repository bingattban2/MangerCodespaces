using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Registry;

public interface IRegistryStore
{
    Task<ServiceResult<PersonPageDto>> QueryPersonsAsync(string nonce, PersonQuery query, CancellationToken token = default);
    Task<ServiceResult<PersonCurrentDto>> GetPersonAsync(string nonce, string personId, CancellationToken token = default);
    Task<ServiceResult<PersonCurrentDto>> CreatePersonAsync(string nonce, string requestId, CreatePersonCommand command, CancellationToken token = default);
    Task<ServiceResult<IdentityPreviewDto>> PreviewIdentityAsync(string nonce, IdentityUpdateCommand command, CancellationToken token = default);
    Task<ServiceResult<ArchivePreviewDto>> PreviewArchiveAsync(string nonce, ArchivePersonsCommand command, CancellationToken token = default);
    Task<ServiceResult<ArchiveResultDto>> ArchiveAsync(string nonce, string requestId, ArchivePersonsCommand command, CancellationToken token = default);
    Task<ServiceResult<IReadOnlyList<SiteDto>>> QuerySitesAsync(string nonce, CatalogQuery query, CancellationToken token = default);
    Task<ServiceResult<SiteDto>> CreateSiteAsync(string nonce, string requestId, CreateSiteCommand command, CancellationToken token = default);
    Task<ServiceResult<SiteDto>> RenameSiteAsync(string nonce, string requestId, RenameSiteCommand command, CancellationToken token = default);
    Task<ServiceResult<SiteImpactDto>> PreviewDisableSiteAsync(string nonce, SiteImpactQuery query, CancellationToken token = default);
    Task<ServiceResult<SiteDto>> DisableSiteAsync(string nonce, string requestId, DisableSiteCommand command, CancellationToken token = default);
    Task<ServiceResult<IReadOnlyList<CatalogStatusDto>>> QueryStatusesAsync(string nonce, CatalogQuery query, CancellationToken token = default);
    Task<ServiceResult<CatalogStatusDto>> CreateStatusAsync(string nonce, string requestId, CreateStatusCommand command, CancellationToken token = default);
    Task<ServiceResult<CatalogStatusDto>> UpdateStatusAsync(string nonce, string requestId, UpdateStatusCommand command, CancellationToken token = default);
    Task<ServiceResult<CatalogStatusDto>> DisableStatusAsync(string nonce, string requestId, DisableStatusCommand command, CancellationToken token = default);
}
