using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AlTamam.Application.Abstractions;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Traceability;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.Entities;
using AlTamam.Domain.Enums;
using AlTamam.Domain.Policies;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Application.Rules;

/// <summary>
/// Explicitly lists, preserves both source positions for, and enforces the resolved rules
/// for all 5 canonical policy conflicts in step "02_حسم تعارضات القواعد":
/// 1) Full reset (حذف جميع بيانات البرنامج)
/// 2) User role rights (صلاحيات الأدوار وإدارة المستخدمين وحذف القوة)
/// 3) Audit overlay editing (تعديل وحذف قيود سجل العمليات)
/// 4) Transfer-day attendance (تحضير الفرد في يوم النقل الداخلي والترحيل الآلي الساعة 20:00)
/// 5) First-run account (حساب التشغيل الأول والبيانات التأسيسية)
/// Never makes an implied or silent choice when a conflict is PendingOwnerApproval.
/// </summary>
public sealed class PolicyConflictResolutionService
{
    private readonly ISystemClock _clock;
    private readonly ISchemaCatalogProbe? _schemaProbe;

    public PolicyConflictResolutionService(ISystemClock clock, ISchemaCatalogProbe? schemaProbe = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _schemaProbe = schemaProbe;
    }

    public static IReadOnlyList<PolicyConflictResolutionDto> GetCanonicalConflictsCatalog() => new[]
    {
        new PolicyConflictResolutionDto(
            ConflictId: "POL-CONFLICT-01-FULL-RESET",
            Topic: PolicyConflictTopic.FullReset,
            TitleArabic: "حذف جميع بيانات البرنامج (Full Application Data Reset)",
            SourcePositionAArabic: "الموقف السابق الملغى (محفوظ للتدقيق التاريخي فقط): السماح بالحذف دون تحقق إضافي أو فرض إنشاء نسخة احتياطية موثقة قبل الحذف؛ لا يُطبّق أيّ من هذين البديلين بوصفه القاعدة الحالية.",
            SourcePositionBArabic: "قرار المالك النهائي: حصر حذف جميع بيانات البرنامج ونسخه الاحتياطية المُدارة ضمن مسارات البرنامج المعتمدة بدور الإدارة العامة (GeneralAdministration) فقط، بعد تحذير واضح يبين نطاق الحذف، وإعادة التحقق من كلمة مرور الحساب الحالي والتأكيد الصريح؛ لا يُشترط إنشاء نسخة احتياطية قبل الحذف؛ يشمل التصفير الشامل حذف سجل العمليات الأصلي AuditEvents وطبقة تعديل العرض AuditLogPresentationEdits وجميع سجلات التاريخ المرتبطة بالبرنامج. تُستثنى الملفات والنسخ الموجودة خارج المسارات المخصصة للبرنامج.",
            ApprovalState: PolicyConflictApprovalState.ApprovedPositionBEnhanced,
            ApprovedResolutionRuleArabic: "يُسمح لـ GeneralAdministration فقط بطلب حذف بيانات البرنامج وجميع النسخ الاحتياطية التي يديرها ضمن مساراته المعتمدة؛ ويشمل التصفير الشامل حصرياً إزالة AuditEvents وAuditLogPresentationEdits وكل سجلات الهوية والتحضير والتنقلات والأرشيف التاريخية، دون الاحتفاظ باستثناء لسجلات التدقيق. يرفض CommitFullReset ما لم يعرض تحذيراً يذكر حذف التاريخ وسجل العمليات والنسخ الاحتياطية، ويُقرّه المستخدم وتُتحقَّق كلمة المرور الحالية من مصدر موثوق في C# ويؤكد المستخدم الحذف صراحة. النسخ الاحتياطي قبل الحذف اختياري وليس بوابة له. لا يحذف البرنامج ملفات أو نسخاً احتياطية خارج جذوره المخصصة، ويجب رفض أي مسار متجاوز أو رابط يعيد توجيه الحذف إلى خارجها. يُرفض كل دور آخر بكود Unauthorized.",
            ApprovedByOwnerLabel: "قرار المالك النهائي — 2026-10-19: إدارة عامة + تحذير + تحقق كلمة المرور + تأكيد دون نسخة احتياطية إلزامية",
            EnforcingCodeArtifacts: new[]
            {
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteFullDatabaseReset",
                "AlTamam.Application.Rules.PolicyConflictResolutionService.EvaluateFullResetGate",
                "AlTamam.Contracts.Bridge.BridgeErrorCodes.ResetConfirmationRequired"
            },
            GoverningErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.ResetConfirmationRequired,
                BridgeErrorCodes.StorageBusy
            },
            AffectedBridgeActions: new[] { "PreviewFullReset", "CommitFullReset" },
            AffectedScreenRouteKeys: new[] { "Settings", "ResetApplicationData" }),

        new PolicyConflictResolutionDto(
            ConflictId: "POL-CONFLICT-02-USER-ROLE-RIGHTS",
            Topic: PolicyConflictTopic.UserRoleRights,
            TitleArabic: "صلاحيات الأدوار وإدارة المستخدمين وحذف الأسماء من القوة (User Role Hierarchy & Force Removal)",
            SourcePositionAArabic: "الموقف الأول (النص الأولي غير المفصل): ظهور شاشة المستخدمين أو حذف الأسماء من القوة ضمن شاشة الإدارة دون تقييد هرمي صارم بين الإدارة العامة والإدارة العادية والمشرف.",
            SourcePositionBArabic: "الموقف الثاني (قرار المالك والتسلسل الهرمي المعتمد): منع «مشرف» كلياً من شاشة المستخدمين ومن حذف/أرشفة الأفراد من القوة؛ السماح لـ «الإدارة العامة» بإضافة/إدارة «الإدارة العادية» و«مشرف»؛ السماح لـ «الإدارة العادية» بإضافة/إدارة «مشرف» فقط؛ وحصر حذف/أرشفة الأسماء من القوة بـ «الإدارة العامة» و«الإدارة العادية» كأرشفة غير مدمرة للتاريخ.",
            ApprovalState: PolicyConflictApprovalState.ApprovedPositionBEnhanced,
            ApprovedResolutionRuleArabic: "تُفرض الصلاحيات الهرمية في طبقة C# عبر RoleAuthorizationPolicy: المشرف ممنوع من إدارة المستخدمين ومن أرشفة الأفراد؛ الإدارة العادية تدير حسابات المشرفين فقط؛ وأرشفة الفرد تغير حالته إلى ArchivedFromForce مع ActiveFlag=0 دون مسح تاريخه.",
            ApprovedByOwnerLabel: "قرار المالك المعتمد — التسلسل الهرمي للأدوار الثلاثة",
            EnforcingCodeArtifacts: new[]
            {
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanAccessUsersManagement",
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanCreateAccountWithRole",
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanModifyAccount",
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce",
                "AlTamam.Application.Rules.PolicyConflictResolutionService.EvaluateUserRoleAction"
            },
            GoverningErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.DuplicateUsername,
                BridgeErrorCodes.ValidationError
            },
            AffectedBridgeActions: new[]
            {
                "GetAllowedRoles", "ListUserAccounts", "SearchActivePersonsForAccount",
                "CreateUserAccount", "ChangeUserPassword", "DeactivateUserAccount",
                "PreviewArchivePersons", "ArchivePersons"
            },
            AffectedScreenRouteKeys: new[]
            {
                "Users", "AddUser", "ChangePassword", "DeleteAccount",
                "EditPersonnelNames", "RemovePersonnel"
            }),

        new PolicyConflictResolutionDto(
            ConflictId: "POL-CONFLICT-03-AUDIT-OVERLAY-EDITING",
            Topic: PolicyConflictTopic.AuditOverlayEditing,
            TitleArabic: "تعديل وحذف قيود سجل العمليات وعرض القيود المعدلة والمحذوفة (Audit Log Overlay Editing)",
            SourcePositionAArabic: "الموقف الأول (تعارض بين السجل غير القابل للمسح أو حصر التعديل بالمدير العام فقط أو المسح الفيزيائي المباشر): إما منع تعديل سجل العمليات مطلقاً، أو حصره بالإدارة العامة وحدها، أو تعديل/حذف الصف الأصلي في جدول AuditEvents مباشرة.",
            SourcePositionBArabic: "الموقف الثاني (تعديل المالك الصريح وبنية الطبقة الفوقية): السماح لكل من «الإدارة العامة» و«الإدارة العادية» معاً بتعديل وحذف القيود الظاهرة في «سجل العمليات» والاطلاع على «سجل القيود المعدلة والمحذوفة»، ومنع «مشرف» كلياً، مع إبقاء جدول AuditEvents غير قابل للتعديل أو الحذف الفيزيائي أثناء الاستخدام المعتاد وتنفيذ التعديل/الحذف الظاهري عبر AuditLogPresentationEdits وعرض المنفذ بصيغة [اسم المستخدم] - [الاسم الكامل بحسب الكشف]. الاستثناء الوحيد هو التصفير الشامل المعتمد، الذي يمحو السجل الأصلي وطبقة العرض وجميع البيانات التاريخية التابعة للبرنامج.",
            ApprovalState: PolicyConflictApprovalState.ApprovedPositionBEnhanced,
            ApprovedResolutionRuleArabic: "أثناء الاستخدام المعتاد يُسمح لـ GeneralAdministration وOrdinaryAdministration بتعديل وحذف العرض الظاهري لسجل العمليات عبر AuditLogPresentationEdits ومراجعة ModifiedDeletedAuditEntries، ويُمنع Supervisor بكود Unauthorized، ولا يجوز حذف أو تعديل AuditEvents الأصلي المرتبط بسلسلة تجزئة SHA-256. عند التصفير الشامل وحده، وبعد اجتياز بوابة حذف البيانات للإدارة العامة، تُزال قاعدة بيانات البرنامج بما فيها AuditEvents وAuditLogPresentationEdits وكل السجلات التاريخية، دون استثناء دائم لسجل العمليات.",
            ApprovedByOwnerLabel: "تعديل المالك الصريح — صلاحيات سجل العمليات والطبقة الفوقية",
            EnforcingCodeArtifacts: new[]
            {
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog",
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanReviewAuditOverlayHistory",
                "AlTamam.Domain.Entities.AuditEvent.ProjectActorDisplay",
                "AlTamam.Domain.Entities.AuditLogPresentationEdit",
                "AlTamam.Infrastructure.Persistence.SqliteAuditEventWriter"
            },
            GoverningErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.MissingData
            },
            AffectedBridgeActions: new[]
            {
                "QueryAuditLog", "GetAuditEntryDetails", "UpdateAuditPresentation",
                "DeleteAuditPresentation", "QueryAuditPresentationChanges"
            },
            AffectedScreenRouteKeys: new[]
            {
                "AuditLog", "EditAuditEntry", "DeleteAuditEntry", "ModifiedDeletedAuditEntries"
            }),

        new PolicyConflictResolutionDto(
            ConflictId: "POL-CONFLICT-04-TRANSFER-DAY-ATTENDANCE",
            Topic: PolicyConflictTopic.TransferDayAttendance,
            TitleArabic: "تحضير الفرد في يوم النقل الداخلي والترحيل التلقائي الساعة 20:00 (Transfer-Day Attendance & 20:00 Carry-Forward)",
            SourcePositionAArabic: "الموقف الأول (التحضير اليومي المعتاد): إمكانية تحضير الفرد يدوياً في الموقع القديم أو الجديد في يوم نقله الداخلي، أو شموله بالنسخ التلقائي لحالة الأمس عند الساعة الثامنة مساءً.",
            SourcePositionBArabic: "الموقف الثاني (قاعدة يوم النقل الداخلي المعتمدة): لا يُعتمد التحضير في يوم النقل الداخلي مطلقاً (TransferDayAttendanceForbidden)، ويُستثنى الفرد المنقول داخلياً في ذلك اليوم من الترحيل التلقائي الساعة 20:00 (SkippedTransferExemptCount)، وتُعرض حركة النقل في خلايا التمام الشهري.",
            ApprovalState: PolicyConflictApprovalState.ApprovedPositionBEnhanced,
            ApprovedResolutionRuleArabic: "يُرفض التحضير اليدوي (الفردي أو الجماعي) أو التعديل للفرد في يوم النقل الداخلي بكود TransferDayAttendanceForbidden، ويتخطاه مجدول الساعة 20:00 تلقائياً مع توثيق العدد في SkippedTransferExemptCount.",
            ApprovedByOwnerLabel: "تعديل المالك الصريح — قاعدة الإعفاء والمنع في يوم النقل الداخلي",
            EnforcingCodeArtifacts: new[]
            {
                "AlTamam.Application.Rules.AttendanceDayPolicy.EnsureDayCanBePrepared",
                "AlTamam.Application.Rules.AssignmentPeriodPolicy",
                "AlTamam.Application.Rules.PolicyConflictResolutionService.EvaluateTransferDayAttendance",
                "AlTamam.Contracts.Bridge.BridgeErrorCodes.TransferDayAttendanceForbidden"
            },
            GoverningErrorCodes: new[]
            {
                BridgeErrorCodes.TransferDayAttendanceForbidden,
                BridgeErrorCodes.HistoricDayLocked,
                BridgeErrorCodes.OverlappingAssignments,
                BridgeErrorCodes.InvalidTransferSites
            },
            AffectedBridgeActions: new[]
            {
                "PreviewAttendanceChange", "SubmitAttendance", "ReviseTodayAttendance",
                "PreviewInternalTransfer", "CommitInternalTransfer", "GetAutoAttendanceRun"
            },
            AffectedScreenRouteKeys: new[]
            {
                "Dashboard", "UnpreparedPersonnel", "Attendance", "ConfirmAttendance",
                "InternalTransfer", "ConfirmTransfer", "MonthlyAttendance", "EightPmScheduler"
            }),

        new PolicyConflictResolutionDto(
            ConflictId: "POL-CONFLICT-05-FIRST-RUN-ACCOUNT",
            Topic: PolicyConflictTopic.FirstRunAccount,
            TitleArabic: "حساب التشغيل الأول والبيانات التأسيسية وهوية المنفذ غير المرتبط بالكشف (First-Run Account & Bootstrap Actor)",
            SourcePositionAArabic: "الموقف الأول (شحن حساب افتراضي مسبق أو اشتراط ربط كل مستخدم بفرد في الكشف منذ اللحظة الأولى): تضمين حساب مدير افتراضي بكلمة مرور معروفة في قاعدة البيانات، أو تعذر إنشاء أول مدير عام قبل استيراد كشف الأفراد.",
            SourcePositionBArabic: "الموقف الثاني (التأسيس الآمن المعتمد): عدم شحن أي مستخدم أو كلمة مرور مسبقة في V001_InitialSchema.sql؛ إتمام مراسم التهيئة الأولى (FirstRunSetup) لإنشاء أول حساب إدارة عامة بكلمة مرور قوية مجزأة بـ Argon2id؛ وتمييز هذا الحساب التأسيسي في سجل العمليات بعبارة «(حساب تأسيسي غير مرتبط بالكشف)» بدلاً من اختلاق اسم فرد وهمي.",
            ApprovalState: PolicyConflictApprovalState.ApprovedPositionBEnhanced,
            ApprovedResolutionRuleArabic: "تُشحن قاعدة البيانات بصفر مستخدمين (PreSeededUsersCount = 0)، ويُنشأ الحساب التأسيسي الأول عبر FirstRunSetup فقط عندما لا يوجد أي مستخدم، ويُعرض في AuditEvents عبر ProjectActorDisplay() بصيغة صريحة غير مضللة.",
            ApprovedByOwnerLabel: "قرار المالك والأمن المعتمد — التهيئة الأولى بدون بيانات اعتماد مسبقة",
            EnforcingCodeArtifacts: new[]
            {
                "AlTamam.Domain.Entities.AuditEvent.ProjectActorDisplay",
                "AlTamam.Domain.Policies.PasswordPolicyValidator.Validate",
                "AlTamam.Infrastructure.Security.PasswordSecurity.HashPassword",
                "AlTamam.Application.Rules.PolicyConflictResolutionService.EvaluateFirstRunProvisioningState",
                "V001_InitialSchema.sql (Zero seeded Users)"
            },
            GoverningErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.ClockRollbackSuspected
            },
            AffectedBridgeActions: new[]
            {
                "GetProvisioningState", "AuthenticateLocalUser", "CreateUserAccount"
            },
            AffectedScreenRouteKeys: new[] { "FirstRunSetup", "Login", "Users", "AddUser" })
    };

    /// <summary>
    /// Validates a collection of conflict definitions, rejecting any silent/implied choice
    /// or any entry that fails to preserve both source positions.
    /// </summary>
    public ServiceResult<PolicyConflictRegistryReportDto> ValidateConflictResolutions(
        IReadOnlyList<PolicyConflictResolutionDto>? customConflicts = null)
    {
        var conflicts = customConflicts ?? GetCanonicalConflictsCatalog();

        var requiredTopics = Enum.GetValues<PolicyConflictTopic>();
        var presentTopics = conflicts.Select(c => c.Topic).ToHashSet();
        if (requiredTopics.Any(t => !presentTopics.Contains(t)))
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.MissingData,
                "سجل حسم تعارضات القواعد غير مكتمل؛ يجب تضمين جميع المحاور الخمسة الإلزامية.");
        }

        bool bothPositionsPreserved = conflicts.All(c =>
            !string.IsNullOrWhiteSpace(c.SourcePositionAArabic) &&
            !string.IsNullOrWhiteSpace(c.SourcePositionBArabic) &&
            !string.Equals(c.SourcePositionAArabic.Trim(), c.SourcePositionBArabic.Trim(), StringComparison.Ordinal));

        if (!bothPositionsPreserved)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.ValidationError,
                "يجب حفظ موقفي المصدر (الموقف الأول والموقف الثاني) نصياً وبوضوح لكل تعارض دون حذف أو دمج صامت.");
        }

        var unapproved = conflicts
            .Where(c => c.ApprovalState == PolicyConflictApprovalState.PendingOwnerApproval
                     || string.IsNullOrWhiteSpace(c.ApprovedResolutionRuleArabic)
                     || string.IsNullOrWhiteSpace(c.ApprovedByOwnerLabel))
            .ToArray();

        var report = new PolicyConflictRegistryReportDto(
            GeneratedAtUtc: _clock.UtcNow,
            AllConflictsExplicitlyResolved: unapproved.Length == 0,
            BothSourcePositionsPreserved: bothPositionsPreserved,
            Conflicts: conflicts);

        if (unapproved.Length > 0)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.ValidationError,
                $"يوجد تعارض غير محسوم ({string.Join(", ", unapproved.Select(u => u.ConflictId))})؛ يُمنع الاختيار الضمني أو الصامت قبل اعتماد المالك.",
                $"PendingCount={unapproved.Length}");
        }

        return ServiceResult<PolicyConflictRegistryReportDto>.Ok(report);
    }

    /// <summary>
    /// Validates the owner-approved full-reset prerequisites. The caller MUST obtain
    /// currentPasswordVerified from server-side C# password reauthentication of the
    /// signed-in GeneralAdministration account; never trust a value supplied by WebView2.
    /// This check alone does not delete data or authenticate a password.
    /// Backup creation is independent and is not a prerequisite for full reset.
    /// Reset scope: all application data (including raw AuditEvents, presentation edits,
    /// and all personnel/attendance/movement/archive histories) plus app-managed backups
    /// inside explicitly approved application-owned roots only. AuditEvents remain
    /// append-only during NORMAL usage; full reset is the sole deletion exception.
    /// This gate does not perform filesystem deletion.
    /// A future executor must reject traversal/reparse/symlink escapes, unexpected roots,
    /// and any external backup copies; never use a path supplied untrusted by the UI.
    /// </summary>
    public static ServiceResult<bool> EvaluateFullResetGate(
        UserRole actorRole,
        bool warningAcknowledged,
        bool currentPasswordVerified,
        bool explicitConfirmation)
    {
        if (!RoleAuthorizationPolicy.CanExecuteFullDatabaseReset(actorRole))
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.Unauthorized,
                "عملية حذف جميع بيانات البرنامج محصورة بالإدارة العامة فقط.");
        }

        if (!warningAcknowledged)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.ResetConfirmationRequired,
                "يجب عرض تحذير حذف جميع بيانات البرنامج والحصول على إقرار المستخدم قبل المتابعة.");
        }

        if (!currentPasswordVerified)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.Unauthorized,
                "يجب إعادة التحقق من كلمة مرور حساب الإدارة العامة الحالي قبل الحذف.");
        }

        if (!explicitConfirmation)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.ResetConfirmationRequired,
                "لم يتم تأكيد الحذف صراحة؛ لم تُحذف أي بيانات.");
        }

        return ServiceResult<bool>.Ok(true);
    }

    public static ServiceResult<bool> EvaluateUserRoleAction(
        UserRole actorRole,
        UserRole? targetRole,
        bool isAccountCreation,
        bool isForceArchival)
    {
        if (isForceArchival)
        {
            if (!RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce(actorRole))
            {
                return ServiceResult<bool>.Fail(
                    BridgeErrorCodes.Unauthorized,
                    "حذف أو أرشفة الأسماء من القوة الفعالة مسموح للإدارة العامة والإدارة العادية فقط وممنوع عن المشرف.");
            }
            return ServiceResult<bool>.Ok(true);
        }

        if (!RoleAuthorizationPolicy.CanAccessUsersManagement(actorRole))
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.Unauthorized,
                "إدارة حسابات المستخدمين محصورة بالإدارة العامة والإدارة العادية فقط.");
        }

        if (targetRole is null)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.ValidationError,
                "يجب تحديد دور الحساب المستهدف.");
        }

        bool allowed = isAccountCreation
            ? RoleAuthorizationPolicy.CanCreateAccountWithRole(actorRole, targetRole.Value)
            : RoleAuthorizationPolicy.CanModifyAccount(actorRole, targetRole.Value);

        if (!allowed)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.Unauthorized,
                "لا تملك الصلاحية الهرمية لإنشاء أو تعديل حساب بهذا الدور.");
        }

        return ServiceResult<bool>.Ok(true);
    }

    public static ServiceResult<bool> EvaluateAuditOverlayMutation(UserRole actorRole, string? reason)
    {
        if (!RoleAuthorizationPolicy.CanEditOrDeleteVisibleAuditLog(actorRole))
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.Unauthorized,
                "تعديل أو حذف القيود الظاهرة في سجل العمليات مسموح للإدارة العامة والإدارة العادية فقط وممنوع عن المشرف.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.ValidationError,
                "يجب إدخال سبب التعديل أو الحذف الظاهري لقيد سجل العمليات.");
        }

        return ServiceResult<bool>.Ok(true);
    }

    public static ServiceResult<bool> EvaluateTransferDayAttendance(
        DateOnly businessDate,
        DateOnly targetDate,
        bool movementOccursOnTargetDate,
        bool isAuthorizedClosedDayOverride,
        UserRole actorRole)
    {
        if (movementOccursOnTargetDate)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.TransferDayAttendanceForbidden,
                "لا يُعتمد التحضير في يوم النقل الداخلي، ويُعفى الفرد من الترحيل التلقائي الساعة 20:00 في هذا اليوم.");
        }

        if (businessDate != targetDate)
        {
            if (!isAuthorizedClosedDayOverride)
            {
                return ServiceResult<bool>.Fail(
                    BridgeErrorCodes.HistoricDayLocked,
                    "التحضير العادي مسموح لليوم الحالي فقط؛ الأيام السابقة مغلقة.");
            }

            if (!RoleAuthorizationPolicy.CanExecuteClosedDayEmergencyOverride(actorRole))
            {
                return ServiceResult<bool>.Fail(
                    BridgeErrorCodes.Unauthorized,
                    "التصحيح الاستثنائي لليوم المغلق محصور بالإدارة العامة فقط.");
            }
        }

        AttendanceDayPolicy.EnsureDayCanBePrepared(
            businessDate,
            targetDate,
            movementOccursOnTargetDate,
            isAuthorizedClosedDayOverride);

        return ServiceResult<bool>.Ok(true);
    }

    public static ServiceResult<bool> EvaluateFirstRunProvisioningState(
        int existingUsersCount,
        string? proposedPassword)
    {
        if (existingUsersCount > 0)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.Unauthorized,
                "تمت التهيئة الأولى للبرنامج مسبقاً؛ لا يمكن إعادة إنشاء حساب التأسيس الأولي.");
        }

        var (isValid, arabicError) = PasswordPolicyValidator.Validate(proposedPassword);
        if (!isValid)
        {
            return ServiceResult<bool>.Fail(
                BridgeErrorCodes.ValidationError,
                arabicError ?? "كلمة المرور غير مستوفية لشروط الأمان.");
        }

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>
    /// Verifies that all 5 policy conflicts are explicitly resolved and persists the resolution
    /// manifest snapshot and tamper-evident audit event atomically in the database.
    /// </summary>
    public async Task<ServiceResult<PolicyConflictRegistryReportDto>> VerifyAndPersistResolutionsAsync(
        UserId? actorUserId = null,
        PersonId? actorPersonId = null,
        string? actorUsernameSnapshot = null,
        string? actorRosterFullNameSnapshot = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateConflictResolutions();
        if (!validation.IsSuccess || validation.Value is null)
        {
            return validation;
        }

        if (_schemaProbe is null)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.MissingData,
                "لم يتم توفير خدمة فحص وحفظ قاعدة البيانات (ISchemaCatalogProbe).");
        }

        var snapshotResult = await _schemaProbe.InspectSchemaAndSeedStateAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshotResult.IsSuccess || snapshotResult.Value is null)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                snapshotResult.Failure ?? new ServiceFailure(BridgeErrorCodes.BackupIntegrityFailed, "تعذر فحص قاعدة البيانات."));
        }

        if (snapshotResult.Value.PreSeededUsersCount != 0)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.ValidationError,
                "انتهاك قاعدة حساب التشغيل الأول: تحتوي قاعدة البيانات التأسيسية على مستخدمين مسبقين.");
        }

        var manifestSummary = JsonSerializer.Serialize(new
        {
            version = 1,
            resolvedConflictIds = validation.Value.Conflicts.Select(c => c.ConflictId).ToArray(),
            allResolved = validation.Value.AllConflictsExplicitlyResolved,
            bothPositionsPreserved = validation.Value.BothSourcePositionsPreserved
        });

        var effectiveActorId = actorUserId ?? UserId.SystemActor;
        var effectiveUsername = string.IsNullOrWhiteSpace(actorUsernameSnapshot) ? "SYSTEM" : actorUsernameSnapshot;
        var effectiveCorrelation = string.IsNullOrWhiteSpace(correlationId) ? $"POL-RES-{Guid.NewGuid():N}" : correlationId;

        var auditResult = await _schemaProbe.RecordPolicyConflictResolutionAuditAsync(
            manifestSummary,
            effectiveActorId,
            actorPersonId,
            effectiveUsername,
            actorRosterFullNameSnapshot,
            effectiveCorrelation,
            cancellationToken).ConfigureAwait(false);

        if (!auditResult.IsSuccess || auditResult.Value is null)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                auditResult.Failure ?? new ServiceFailure(BridgeErrorCodes.StorageBusy, "تعذر حفظ سجل حسم تعارضات القواعد."));
        }

        var persistedCheck = await _schemaProbe.GetPersistedPolicyConflictManifestAsync(cancellationToken).ConfigureAwait(false);
        if (!persistedCheck.IsSuccess || string.IsNullOrWhiteSpace(persistedCheck.Value))
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "لم يتم العثور على سجل حسم تعارضات القواعد في قاعدة البيانات بعد الحفظ.");
        }

        return ServiceResult<PolicyConflictRegistryReportDto>.Ok(
            validation.Value with
            {
                IsPersistedInDatabase = true,
                PersistedAuditEventId = auditResult.Value.AuditEventId.Value
            });
    }

    /// <summary>
    /// Reads and validates the persisted policy conflict resolution manifest from the encrypted database.
    /// </summary>
    public async Task<ServiceResult<PolicyConflictRegistryReportDto>> GetPersistedResolutionReportAsync(
        CancellationToken cancellationToken = default)
    {
        if (_schemaProbe is null)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.MissingData,
                "لم يتم توفير خدمة فحص قاعدة البيانات (ISchemaCatalogProbe).");
        }

        var persistedCheck = await _schemaProbe.GetPersistedPolicyConflictManifestAsync(cancellationToken).ConfigureAwait(false);
        if (!persistedCheck.IsSuccess)
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                persistedCheck.Failure ?? new ServiceFailure(BridgeErrorCodes.BackupIntegrityFailed, "تعذر قراءة سجل حسم التعارضات."));
        }

        if (string.IsNullOrWhiteSpace(persistedCheck.Value))
        {
            return ServiceResult<PolicyConflictRegistryReportDto>.Fail(
                BridgeErrorCodes.MissingData,
                "لم يتم حفظ سجل حسم تعارضات القواعد في قاعدة البيانات بعد.");
        }

        var validation = ValidateConflictResolutions();
        if (!validation.IsSuccess || validation.Value is null)
        {
            return validation;
        }

        return ServiceResult<PolicyConflictRegistryReportDto>.Ok(
            validation.Value with { IsPersistedInDatabase = true });
    }
}
