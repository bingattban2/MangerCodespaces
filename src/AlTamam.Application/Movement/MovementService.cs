using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Movement;
public sealed class MovementService(IMovementStore store)
{
    public Task<ServiceResult<MovementPreview>> PreviewAsync(string nonce, MovementChange change, CancellationToken token = default) => store.PreviewMovementAsync(nonce, change, token);
    public Task<ServiceResult<MovementResult>> CommitAsync(string nonce, string requestId, CommitMovement command, CancellationToken token = default) => store.CommitMovementAsync(nonce, requestId, command, token);
    public Task<ServiceResult<MovementArchivePage>> QueryAsync(string nonce, MovementArchiveQuery query, CancellationToken token = default) => store.QueryMovementsAsync(nonce, query, token);
    public Task<ServiceResult<MovementArchiveRow>> GetAsync(string nonce, string id, CancellationToken token = default) => store.GetMovementAsync(nonce, id, token);
}
