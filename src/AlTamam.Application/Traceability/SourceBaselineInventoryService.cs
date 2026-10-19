using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using AlTamam.Application.Abstractions;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Traceability;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Application.Traceability;

/// <summary>
/// Fingerprints foundation files and verifies:
/// - No executable WPF/WebView2 host in the foundation baseline
/// - No obsolete demos, progress files, or localStorage fake employee datasets
/// - No hidden or pre-seeded user accounts in SQL migrations or live DB
/// - Zero broken MSBuild ProjectReferences across all .csproj files
/// Never writes inventory files into the production source repository.
/// </summary>
public sealed class SourceBaselineInventoryService
{
    private readonly ISystemClock _clock;
    private readonly ISchemaCatalogProbe? _schemaProbe;

    public SourceBaselineInventoryService(ISystemClock clock, ISchemaCatalogProbe? schemaProbe = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _schemaProbe = schemaProbe;
    }

    public ServiceResult<SourceBaselineVerificationDto> InspectRepositoryDirectory(string repositoryRootPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryRootPath) || !Directory.Exists(repositoryRootPath))
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                BridgeErrorCodes.MissingData,
                "مسار المستودع المطلوب جرده غير موجود أو غير صالح.");
        }

        var rootFull = Path.GetFullPath(repositoryRootPath);
        var violations = new List<string>();
        var fingerprints = new List<SourceFileFingerprintDto>();
        var projectReferences = new List<ProjectReferenceAuditDto>();

        bool hasExecutableHost = false;
        bool hasObsoleteDemos = false;
        bool hasHiddenAccounts = false;
        bool hasBrokenReferences = false;

        var allFiles = Directory.EnumerateFiles(rootFull, "*", SearchOption.AllDirectories)
            .Where(p => !IsIgnoredBuildDirectory(rootFull, p))
            .OrderBy(p => Path.GetRelativePath(rootFull, p).Replace('\', '/'), StringComparer.Ordinal)
            .ToArray();

        foreach (var filePath in allFiles)
        {
            var relPath = Path.GetRelativePath(rootFull, filePath).Replace('\', '/');
            var bytes = File.ReadAllBytes(filePath);
            var sha256Hex = Convert.ToHexString(SHA256.HashData(bytes));
            var category = ClassifyFile(relPath);

            fingerprints.Add(new SourceFileFingerprintDto(
                RelativePath: relPath,
                Sha256Hex: sha256Hex,
                SizeBytes: bytes.Length,
                Category: category));

            var fileNameLower = Path.GetFileName(relPath).ToLowerInvariant();
            if (fileNameLower.Contains("inventory") ||
                fileNameLower.Contains("progress") ||
                fileNameLower.EndsWith(".log", StringComparison.Ordinal) ||
                fileNameLower.EndsWith(".db", StringComparison.Ordinal) ||
                fileNameLower.EndsWith(".sqlite", StringComparison.Ordinal))
            {
                hasObsoleteDemos = true;
                violations.Add($"ملف غير مسموح به داخل مستودع الإنتاج: {relPath}");
            }

            if (relPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                AuditCsprojFile(rootFull, filePath, relPath, projectReferences, violations, ref hasExecutableHost, ref hasBrokenReferences);
            }
            else if (relPath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                var sqlText = File.ReadAllText(filePath);
                if (Regex.IsMatch(sqlText, @"INSERT\s+(OR\s+\w+\s+)?INTO\s+Users", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(sqlText, @"INSERT\s+(OR\s+\w+\s+)?INTO\s+Persons", RegexOptions.IgnoreCase))
                {
                    hasHiddenAccounts = true;
                    violations.Add($"تم رصد حسابات أو أفراد مضمنين مسبقاً في ملف SQL: {relPath}");
                }
            }
            else if (relPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            {
                var jsText = File.ReadAllText(filePath);
                if (Regex.IsMatch(jsText, @"localStorage\s*\.\s*(setItem|getItem)", RegexOptions.IgnoreCase))
                {
                    hasObsoleteDemos = true;
                    violations.Add($"تم رصد تخزين محلي تجريبي (localStorage) غير مسموح به في: {relPath}");
                }
            }
        }

        bool isClean = !hasExecutableHost
            && !hasObsoleteDemos
            && !hasHiddenAccounts
            && !hasBrokenReferences
            && violations.Count == 0;

        var report = new SourceBaselineVerificationDto(
            VerifiedAtUtc: _clock.UtcNow,
            IsCleanFoundationBaseline: isClean,
            TotalFingerprintedFilesCount: fingerprints.Count,
            HasExecutableHostProject: hasExecutableHost,
            HasObsoleteDemosOrFakeData: hasObsoleteDemos,
            HasHiddenOrPreSeededAccounts: hasHiddenAccounts,
            HasBrokenProjectReferences: hasBrokenReferences,
            FileFingerprints: fingerprints,
            ProjectReferences: projectReferences,
            DetectedViolations: violations);

        if (!isClean)
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                BridgeErrorCodes.ValidationError,
                $"فشل فحص خط الأساس للمستودع: {string.Join(" | ", violations)}");
        }

        return ServiceResult<SourceBaselineVerificationDto>.Ok(report);
    }

    public async Task<ServiceResult<SourceBaselineVerificationDto>> VerifyAndRecordBaselineAsync(
        string repositoryRootPath,
        UserId? actorUserId = null,
        PersonId? actorPersonId = null,
        string? actorUsernameSnapshot = null,
        string? actorRosterFullNameSnapshot = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var inspection = InspectRepositoryDirectory(repositoryRootPath);
        if (!inspection.IsSuccess || inspection.Value is null)
        {
            return inspection;
        }

        if (_schemaProbe is null)
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                BridgeErrorCodes.MissingData,
                "لم يتم توفير خدمة فحص قاعدة البيانات (ISchemaCatalogProbe).");
        }

        var dbSnapshot = await _schemaProbe.InspectSchemaAndSeedStateAsync(cancellationToken).ConfigureAwait(false);
        if (!dbSnapshot.IsSuccess || dbSnapshot.Value is null)
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                dbSnapshot.Failure ?? new ServiceFailure(BridgeErrorCodes.BackupIntegrityFailed, "تعذر فحص قاعدة البيانات."));
        }

        if (dbSnapshot.Value.PreSeededUsersCount > 0 || dbSnapshot.Value.PreSeededPersonsCount > 0)
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                BridgeErrorCodes.ValidationError,
                "تم رصد حسابات مستخدمين أو سجلات أفراد مسبقة في قاعدة البيانات التأسيسية.");
        }

        var summaryJson = JsonSerializer.Serialize(new
        {
            verifiedAtUtc = inspection.Value.VerifiedAtUtc.ToUniversalTime().ToString("O"),
            totalFiles = inspection.Value.TotalFingerprintedFilesCount,
            projectReferencesCount = inspection.Value.ProjectReferences.Count,
            hasExecutableHost = inspection.Value.HasExecutableHostProject,
            hasObsoleteDemos = inspection.Value.HasObsoleteDemosOrFakeData,
            hasHiddenAccounts = inspection.Value.HasHiddenOrPreSeededAccounts,
            hasBrokenReferences = inspection.Value.HasBrokenProjectReferences
        });

        var effectiveActor = actorUserId ?? UserId.SystemActor;
        var effectiveUsername = string.IsNullOrWhiteSpace(actorUsernameSnapshot) ? "SYSTEM" : actorUsernameSnapshot;
        var effectiveCorrelation = string.IsNullOrWhiteSpace(correlationId) ? $"BASELINE-{Guid.NewGuid():N}" : correlationId;

        var auditResult = await _schemaProbe.RecordBaselineInventoryAuditAsync(
            summaryJson,
            effectiveActor,
            actorPersonId,
            effectiveUsername,
            actorRosterFullNameSnapshot,
            effectiveCorrelation,
            cancellationToken).ConfigureAwait(false);

        if (!auditResult.IsSuccess || auditResult.Value is null)
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                auditResult.Failure ?? new ServiceFailure(BridgeErrorCodes.StorageBusy, "تعذر تسجيل قيد تدقيق جرد خط الأساس."));
        }

        var persistedSummary = await _schemaProbe.GetPersistedBaselineSummaryAsync(cancellationToken).ConfigureAwait(false);
        if (!persistedSummary.IsSuccess || string.IsNullOrWhiteSpace(persistedSummary.Value))
        {
            return ServiceResult<SourceBaselineVerificationDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "لم يتم العثور على ملخص خط الأساس في قاعدة البيانات بعد الحفظ.");
        }

        return ServiceResult<SourceBaselineVerificationDto>.Ok(
            inspection.Value with
            {
                IsPersistedInDatabase = true,
                PersistedAuditEventId = auditResult.Value.AuditEventId.Value
            });
    }

    private static bool IsIgnoredBuildDirectory(string rootFull, string filePath)
    {
        var rel = Path.GetRelativePath(rootFull, filePath).Replace('\', '/');
        return rel.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
            || rel.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || rel.Contains("/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static string ClassifyFile(string relPath)
    {
        if (relPath.StartsWith("src/AlTamam.Domain/", StringComparison.Ordinal)) return "Domain";
        if (relPath.StartsWith("src/AlTamam.Contracts/", StringComparison.Ordinal)) return "Contracts";
        if (relPath.StartsWith("src/AlTamam.Application/", StringComparison.Ordinal)) return "Application";
        if (relPath.StartsWith("src/AlTamam.Infrastructure/", StringComparison.Ordinal)) return "Infrastructure";
        if (relPath.StartsWith("src/AlTamam.WebUI/", StringComparison.Ordinal)) return "WebUI";
        return "RepositoryConfig";
    }

    private static void AuditCsprojFile(
        string rootFull,
        string csprojFullPath,
        string csprojRelPath,
        List<ProjectReferenceAuditDto> projectReferences,
        List<string> violations,
        ref bool hasExecutableHost,
        ref bool hasBrokenReferences)
    {
        try
        {
            var doc = XDocument.Load(csprojFullPath);
            var outputType = doc.Descendants("OutputType").FirstOrDefault()?.Value?.Trim();
            var useWpf = doc.Descendants("UseWPF").FirstOrDefault()?.Value?.Trim();
            var tfm = doc.Descendants("TargetFramework").FirstOrDefault()?.Value?.Trim() ?? string.Empty;

            if (string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(useWpf, "true", StringComparison.OrdinalIgnoreCase))
            {
                hasExecutableHost = true;
                violations.Add($"تم رصد مشروع مضيف تنفيذي غير متوقع في خط الأساس: {csprojRelPath}");
            }

            var csprojDir = Path.GetDirectoryName(csprojFullPath)!;
            foreach (var pr in doc.Descendants("ProjectReference"))
            {
                var includeAttr = pr.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(includeAttr))
                {
                    hasBrokenReferences = true;
                    violations.Add($"مرجع مشروع فارغ في {csprojRelPath}");
                    continue;
                }

                var targetFull = Path.GetFullPath(Path.Combine(csprojDir, includeAttr.Replace('\', Path.DirectorySeparatorChar)));
                bool exists = File.Exists(targetFull);
                var targetRel = exists
                    ? Path.GetRelativePath(rootFull, targetFull).Replace('\', '/')
                    : includeAttr.Replace('\', '/');

                projectReferences.Add(new ProjectReferenceAuditDto(
                    SourceProjectRelativePath: csprojRelPath,
                    TargetProjectRelativePath: targetRel,
                    TargetExists: exists,
                    TargetFramework: tfm));

                if (!exists)
                {
                    hasBrokenReferences = true;
                    violations.Add($"مرجع مشروع مكسور في {csprojRelPath} -> {includeAttr}");
                }
            }
        }
        catch (Exception ex)
        {
            hasBrokenReferences = true;
            violations.Add($"تعذر قراءة ملف المشروع {csprojRelPath}: {ex.Message}");
        }
    }
}
