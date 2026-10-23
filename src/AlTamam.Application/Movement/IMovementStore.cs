using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Dtos;
namespace AlTamam.Application.Movement;
public interface IMovementStore
{
    Task<ServiceResult<MovementPreview>> PreviewMovementAsync(string nonce, MovementChange change, CancellationToken token = default);
    Task<ServiceResult<MovementResult>> CommitMovementAsync(string nonce, string requestId, CommitMovement command, CancellationToken token = default);
    Task<ServiceResult<MovementArchivePage>> QueryMovementsAsync(string nonce, MovementArchiveQuery query, CancellationToken token = default);
    Task<ServiceResult<MovementArchiveRow>> GetMovementAsync(string nonce, string movementId, CancellationToken token = default);
}
