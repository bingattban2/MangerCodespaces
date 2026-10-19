using System.Threading;
using System.Threading.Tasks;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Traceability;
using AlTamam.Domain.Entities;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Application.Abstractions;

/// <summary>
/// Dependency-inversion port for inspecting the live encrypted database schema,
/// triggers, seed invariants, and recording tamper-evident audit events inside a single write transaction.
/// </summary>
public interface ISchemaCatalogProbe
{
    Task<ServiceResult<DatabaseSchemaVerificationSnapshotDto>> InspectSchemaAndSeedStateAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AuditEvent>> RecordSchemaVerificationAuditAsync(
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AuditEvent>> RecordPolicyConflictResolutionAuditAsync(
        string resolutionManifestJson,
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<string?>> GetPersistedPolicyConflictManifestAsync(
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AuditEvent>> RecordBaselineInventoryAuditAsync(
        string baselineSummaryJson,
        UserId actorUserId,
        PersonId? actorPersonId,
        string actorUsernameSnapshot,
        string? actorRosterFullNameSnapshot,
        string correlationId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<string?>> GetPersistedBaselineSummaryAsync(
        CancellationToken cancellationToken = default);
}
