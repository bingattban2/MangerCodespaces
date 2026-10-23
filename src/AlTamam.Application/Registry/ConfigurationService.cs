using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;

namespace AlTamam.Application.Registry;

public sealed class ConfigurationService(IRegistryStore store)
{
    public Task<ServiceResult<IReadOnlyList<SiteDto>>> SitesAsync(string nonce, CatalogQuery query, CancellationToken token = default) => store.QuerySitesAsync(nonce, query, token);
    public Task<ServiceResult<SiteDto>> CreateSiteAsync(string nonce, string id, CreateSiteCommand command, CancellationToken token = default) => store.CreateSiteAsync(nonce, id, command, token);
    public Task<ServiceResult<SiteDto>> RenameSiteAsync(string nonce, string id, RenameSiteCommand command, CancellationToken token = default) => store.RenameSiteAsync(nonce, id, command, token);
    public Task<ServiceResult<SiteImpactDto>> PreviewDisableSiteAsync(string nonce, SiteImpactQuery query, CancellationToken token = default) => store.PreviewDisableSiteAsync(nonce, query, token);
    public Task<ServiceResult<SiteDto>> DisableSiteAsync(string nonce, string id, DisableSiteCommand command, CancellationToken token = default) => store.DisableSiteAsync(nonce, id, command, token);
    public Task<ServiceResult<IReadOnlyList<CatalogStatusDto>>> StatusesAsync(string nonce, CatalogQuery query, CancellationToken token = default) => store.QueryStatusesAsync(nonce, query, token);
    public Task<ServiceResult<CatalogStatusDto>> CreateStatusAsync(string nonce, string id, CreateStatusCommand command, CancellationToken token = default) => store.CreateStatusAsync(nonce, id, command, token);
    public Task<ServiceResult<CatalogStatusDto>> UpdateStatusAsync(string nonce, string id, UpdateStatusCommand command, CancellationToken token = default) => store.UpdateStatusAsync(nonce, id, command, token);
    public Task<ServiceResult<CatalogStatusDto>> DisableStatusAsync(string nonce, string id, DisableStatusCommand command, CancellationToken token = default) => store.DisableStatusAsync(nonce, id, command, token);
}
