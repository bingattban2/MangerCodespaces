using System;
using System.Collections.Generic;

namespace AlTamam.Contracts.Traceability;

/// <summary>
/// SHA-256 fingerprint and classification of a single file in the foundation repository.
/// </summary>
public sealed record SourceFileFingerprintDto(
    string RelativePath,
    string Sha256Hex,
    long SizeBytes,
    string Category
);

/// <summary>
/// Verification record for a MSBuild ProjectReference link between two .csproj files.
/// </summary>
public sealed record ProjectReferenceAuditDto(
    string SourceProjectRelativePath,
    string TargetProjectRelativePath,
    bool TargetExists,
    string TargetFramework
);

/// <summary>
/// External inventory report of the foundation baseline.
/// Must be emitted only in external task output, never written as a file inside the production repository.
/// </summary>
public sealed record SourceBaselineVerificationDto(
    DateTimeOffset VerifiedAtUtc,
    bool IsCleanFoundationBaseline,
    int TotalFingerprintedFilesCount,
    bool HasExecutableHostProject,
    bool HasObsoleteDemosOrFakeData,
    bool HasHiddenOrPreSeededAccounts,
    bool HasBrokenProjectReferences,
    IReadOnlyList<SourceFileFingerprintDto> FileFingerprints,
    IReadOnlyList<ProjectReferenceAuditDto> ProjectReferences,
    IReadOnlyList<string> DetectedViolations,
    bool IsPersistedInDatabase = false,
    string? PersistedAuditEventId = null
);
