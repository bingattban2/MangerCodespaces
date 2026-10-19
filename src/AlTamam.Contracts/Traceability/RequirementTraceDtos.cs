using System;
using System.Collections.Generic;

namespace AlTamam.Contracts.Traceability;

/// <summary>
/// Distinguishes what is currently implemented in the delivered core vs planned in subsequent phases.
/// </summary>
public enum RequirementImplementationStage
{
    ImplementedInFoundation = 1,
    FoundationReadyServicePlanned = 2,
    PlannedInSubsequentPhase = 3
}

/// <summary>
/// Canonical requirement traceability record mapping a requirement from الفكره.txt
/// and approved user overrides to SQL tables, owning services, bridge actions, WebUI screens,
/// error codes, UI invalidation events, invariants, and external verification steps.
/// </summary>
public sealed record CanonicalRequirementTraceDto(
    string RequirementId,
    string SourcePassageArabic,
    string ApprovedDecisionArabic,
    string ImplementationPhase,
    RequirementImplementationStage CurrentStage,
    IReadOnlyList<string> ImplementedArtifactsInCurrentCode,
    IReadOnlyList<string> PlannedArtifactsInUpcomingPhases,
    IReadOnlyList<string> ExpectedTables,
    IReadOnlyList<string> OwningServices,
    IReadOnlyList<string> BridgeActions,
    IReadOnlyList<string> ScreenRouteKeys,
    IReadOnlyList<string> ErrorCodes,
    IReadOnlyList<string> InvalidationEvents,
    IReadOnlyList<string> EffectiveDateAndAuditInvariants,
    IReadOnlyList<string> ExternalVerificationSteps
);

/// <summary>
/// Snapshot of the live SQLite/SQLCipher database schema and seed state verified at runtime.
/// </summary>
public sealed record DatabaseSchemaVerificationSnapshotDto(
    bool EncryptionProviderActive,
    bool IntegrityCheckPassed,
    int CurrentSchemaVersion,
    IReadOnlyList<string> PresentTables,
    IReadOnlyList<string> MissingTables,
    IReadOnlyList<string> PresentTriggers,
    IReadOnlyList<string> MissingTriggers,
    bool HasProtectedUnspecifiedSite,
    int SeededDefaultStatusesCount,
    int SeededSystemColumnsCount,
    int SeededAppSettingsCount,
    int PreSeededUsersCount,
    int PreSeededPersonsCount
);

/// <summary>
/// Complete requirement traceability verification report covering static contract mapping
/// and optional live database verification.
/// </summary>
public sealed record RequirementTraceabilityReportDto(
    DateTimeOffset GeneratedAtUtc,
    bool IsCompleteAndConsistent,
    int TotalRequirementsCount,
    int TotalCanonicalTablesCount,
    int TotalCanonicalServicesCount,
    int TotalCanonicalActionsCount,
    int TotalCanonicalScreensCount,
    IReadOnlyList<CanonicalRequirementTraceDto> Requirements,
    IReadOnlyList<string> UnmappedTables,
    IReadOnlyList<string> UnmappedServices,
    IReadOnlyList<string> UnmappedActions,
    IReadOnlyList<string> UnmappedScreens,
    DatabaseSchemaVerificationSnapshotDto? DatabaseSnapshot = null
);
