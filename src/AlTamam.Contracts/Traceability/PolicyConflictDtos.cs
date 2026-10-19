using System;
using System.Collections.Generic;

namespace AlTamam.Contracts.Traceability;

/// <summary>
/// The 5 canonical policy conflict areas identified across the original concept and approved overrides.
/// </summary>
public enum PolicyConflictTopic
{
    FullReset = 1,
    UserRoleRights = 2,
    AuditOverlayEditing = 3,
    TransferDayAttendance = 4,
    FirstRunAccount = 5
}

/// <summary>
/// Explicit approval state for a policy conflict.
/// PendingOwnerApproval prohibits any silent or implied selection.
/// </summary>
public enum PolicyConflictApprovalState
{
    PendingOwnerApproval = 0,
    ApprovedPositionAOriginal = 1,
    ApprovedPositionBEnhanced = 2
}

/// <summary>
/// Preserves both source positions (Position A and Position B) and records the explicit owner-approved resolution.
/// </summary>
public sealed record PolicyConflictResolutionDto(
    string ConflictId,
    PolicyConflictTopic Topic,
    string TitleArabic,
    string SourcePositionAArabic,
    string SourcePositionBArabic,
    PolicyConflictApprovalState ApprovalState,
    string? ApprovedResolutionRuleArabic,
    string? ApprovedByOwnerLabel,
    IReadOnlyList<string> EnforcingCodeArtifacts,
    IReadOnlyList<string> GoverningErrorCodes,
    IReadOnlyList<string> AffectedBridgeActions,
    IReadOnlyList<string> AffectedScreenRouteKeys
);

/// <summary>
/// Complete report of all 5 canonical policy conflicts, their resolution status, and database persistence state.
/// </summary>
public sealed record PolicyConflictRegistryReportDto(
    DateTimeOffset GeneratedAtUtc,
    bool AllConflictsExplicitlyResolved,
    bool BothSourcePositionsPreserved,
    IReadOnlyList<PolicyConflictResolutionDto> Conflicts,
    bool IsPersistedInDatabase = false,
    string? PersistedAuditEventId = null
);
