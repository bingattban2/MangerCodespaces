using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlTamam.Application.Abstractions;
using AlTamam.Contracts.Bridge;
using AlTamam.Contracts.Events;
using AlTamam.Contracts.Traceability;
using AlTamam.Domain.Abstractions;
using AlTamam.Domain.ValueObjects;

namespace AlTamam.Application.Traceability;

/// <summary>
/// Canonical requirement traceability service for step "01_مطابقة المتطلبات بالمصدر".
/// Maps every requirement in الفكره.txt and approved user overrides to SQL tables,
/// application services, bridge actions, WebUI screen routes, error codes, invalidation events,
/// and external verification checks, while clearly distinguishing current foundation code from planned work.
/// </summary>
public sealed class RequirementTraceabilityService
{
    private readonly ISystemClock _clock;
    private readonly ISchemaCatalogProbe? _schemaProbe;

    public RequirementTraceabilityService(ISystemClock clock, ISchemaCatalogProbe? schemaProbe = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _schemaProbe = schemaProbe;
    }

    public static readonly IReadOnlyList<string> CanonicalTables = new[]
    {
        "SchemaVersions",
        "Persons",
        "PersonIdentityHistory",
        "Sites",
        "SiteNameHistory",
        "PersonAssignments",
        "AttendanceStatusDefinitions",
        "AttendanceStatusNameHistory",
        "AttendanceRecords",
        "AttendanceRevisions",
        "PersonnelMovements",
        "Users",
        "UserRoleHistory",
        "UserSessions",
        "FailedLoginEvents",
        "AuditEvents",
        "AuditLogPresentationEdits",
        "CustomFieldDefinitions",
        "CustomFieldValues",
        "PeriodClosures",
        "AttendanceDrafts",
        "AutoAttendanceRuns",
        "BackupHistory",
        "ImportBatches",
        "ImportIssues",
        "AppSettings"
    };

    public static readonly IReadOnlyList<string> CanonicalTriggers = new[]
    {
        "TR_PersonAssignments_ValidateInsert",
        "TR_PersonAssignments_ValidateUpdate"
    };

    public static readonly IReadOnlyList<string> CanonicalServices = new[]
    {
        "AuthenticationService",
        "DashboardService",
        "AttendanceService",
        "MonthlyGridService",
        "PersonRosterService",
        "MovementService",
        "ReportingService",
        "ConfigurationService",
        "ExcelSheetsService",
        "UserAuditService",
        "SettingsBackupService"
    };

    public static readonly IReadOnlyList<string> CanonicalActions = new[]
    {
        // AuthenticationService (7)
        "GetProvisioningState", "CompleteProvisioning", "AuthenticateLocalUser", "GetCurrentSession",
        "GetNavigationPermissions", "Logout", "GetAllowedRoles",
        // DashboardService (4)
        "GetDashboardSummary", "QueryDailyRoster", "QueryUnprepared", "GetAutoAttendanceRun",
        // AttendanceService (9)
        "GetActiveAttendanceStatuses", "PreviewAttendanceChange", "SubmitAttendance",
        "ReviseTodayAttendance", "SaveAttendanceDraft", "ListAttendanceDrafts", "DeleteAttendanceDraft",
                "PreviewClosedDayCorrection", "CommitClosedDayCorrection",
        // MonthlyGridService (3)
        "GetAvailableArchivePeriods", "QueryMonthlyGrid", "GetMonthlyCellDetails",
        // PersonRosterService (7)
        "SearchPersons", "GetPersonSummary", "QueryPersons", "CreatePerson", "PreviewPersonIdentityUpdate",
        "UpdatePersonIdentity", "PreviewArchivePersons", "ArchivePersons",
        // MovementService (11)
        "QueryActivePersonsBySite", "PreviewInternalTransfer", "CommitInternalTransfer",
        "PreviewExternalTransfer", "CommitExternalTransfer", "QueryMovementArchive",
        "GetMovementDetails", "PreviewSeparation", "CommitSeparation",
        "QuerySeparationArchive", "GetSeparationDetails",
        // ReportingService (6)
        "QueryPersonAttendanceTimeline", "QueryPersonAttendanceTotals", "BuildPersonLifetimeReport",
        "QueryPersonOperationsTimeline", "QueryPersonMovements", "ExportPersonLifetimeExcel",
        // ConfigurationService (12)
        "QueryAttendanceStatuses", "CreateAttendanceStatus", "UpdateAttendanceStatus",
        "DeactivateOrDeleteStatus", "QuerySites", "CreateSite", "RenameSite",
        "RemoveSiteAndReassignCurrentPersons", "PreviewSiteRemoval", "GetCustomFieldDefinitions", "CreateCustomFieldDefinition",
        "UpdateCustomFieldMetadata", "DeactivateCustomField",
        // ExcelSheetsService (11)
        "SelectImportWorkbook", "AnalyzeImport", "PreviewImportPlan", "CommitImport",
        "QueryImportOutcome", "ExportImportIssues", "GetExportablePeriods", "PreviewMonthlyExport",
        "ExportMonthlyWorkbookSet", "GetExportProgress", "CancelExport",
        // UserAuditService (10)
        "ListUserAccounts", "SearchActivePersonsForAccount", "CreateUserAccount",
        "ChangeUserPassword", "DeactivateUserAccount", "QueryAuditLog", "GetAuditEntryDetails",
        "UpdateAuditPresentation", "DeleteAuditPresentation", "QueryAuditPresentationChanges",
        // SettingsBackupService (13)
        "GetSettingsOverview", "CreateVerifiedBackup", "VerifyBackup", "InspectBackup",
        "PreviewRestore", "RestoreDatabaseAtomically", "GetBackupSchedule", "UpdateBackupSchedule",
        "RunDatabaseIntegrityCheck", "PreviewFullReset", "CommitFullReset", "SetTheme", "GetApplicationVersion"
    };

    public static readonly IReadOnlyList<string> CanonicalScreens = new[]
    {
        "Dashboard", "UnpreparedPersonnel", "SelectStatusAndConfirm",
        "MonthlyAttendance", "SelectArchive",
        "Administration", "PersonnelManagement",
        "Attendance", "ConfirmAttendance",
        "EditPersonnelNames", "EditPersonDetails", "RemovePersonnel",
        "Transfers", "InternalTransfer", "ConfirmTransfer", "ExternalTransfer", "ConfirmExternalTransfer", "TransferArchive",
        "ReportsAndDismissal", "Reports", "Dismissal", "DismissPerson", "ConfirmDismissal", "DismissedPersonnelArchive",
        "PersonnelReport", "ExportPersonReport",
        "StatusesAndSites", "Statuses", "AddStatus", "EditStatus", "DeleteStatus",
        "Sites", "AddSite", "EditSite", "DeleteSite",
        "Rosters", "Import", "SelectFile", "PreviewAndValidate", "ImportResults",
        "Export", "PreviewAndConfirmExport", "ExportProgressAndResult",
        "RosterLayout", "AddOrEditColumn",
        "Users", "AddUser", "ChangePassword", "DeleteAccount",
        "AuditLog", "EditAuditEntry", "DeleteAuditEntry", "ModifiedDeletedAuditEntries",
        "Settings", "CreateBackup", "RestoreBackup", "ScheduledBackups", "ResetApplicationData",
        "AboutApplication", "Logout", "AppearanceAndDiagnostics",
        "Login", "FirstRunSetup", "PersonPickerAndSearch", "ConfirmationAndWarning",
        "DraftsAndTemporarySave", "ConflictsAndNotifications", "ClosedDayExceptionalCorrection", "EightPmScheduler"
    };

    public static IReadOnlyList<CanonicalRequirementTraceDto> GetCanonicalRequirementsCatalog() => new[]
    {
        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-01-BOOTSTRAP-AUTH",
            SourcePassageArabic: "نظام الدخول والصلاحيات (الإدارة العامة، الإدارة العادية، مشرف) وحماية قاعدة البيانات المحلية بدون اتصال سحابي.",
            ApprovedDecisionArabic: "حظر أي حساب افتراضي أو كلمة مرور مضمنة؛ تنفيذ التهيئة الأولى (FirstRunSetup) لإنشاء أول حساب إدارة عامة، وتشفير SQLCipher بمفتاح 32 بايت، وتجزئة Argon2id، والتحقق من عدم تراجع ساعة النظام.",
            ImplementationPhase: "03",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.Enums.UserRole",
                "AlTamam.Domain.Policies.PasswordPolicyValidator",
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy",
                "AlTamam.Infrastructure.Security.PasswordSecurity (Argon2id)",
                "AlTamam.Infrastructure.Security.DatabaseKeyGenerator",
                "AlTamam.Infrastructure.Persistence.AlTamamDbContext (SQLCipher)",
                "V001_InitialSchema.sql (Users, UserSessions, FailedLoginEvents, SchemaVersions)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "AuthenticationService implementation (GetProvisioningState, AuthenticateLocalUser, GetCurrentSession, GetNavigationPermissions, Logout, GetAllowedRoles)",
                "Windows DPAPI key protection store",
                "FirstRunSetup and Login WebView2 bridge wiring"
            },
            ExpectedTables: new[] { "SchemaVersions", "Users", "UserSessions", "FailedLoginEvents", "AppSettings", "AuditEvents" },
            OwningServices: new[] { "AuthenticationService" },
            BridgeActions: new[]
            {
                "GetProvisioningState", "CompleteProvisioning", "AuthenticateLocalUser", "GetCurrentSession",
                "GetNavigationPermissions", "Logout", "GetAllowedRoles"
            },
            ScreenRouteKeys: new[] { "FirstRunSetup", "Login", "Logout" },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.StorageBusy,
                BridgeErrorCodes.ClockRollbackSuspected
            },
            InvalidationEvents: new[] { UiInvalidationEvents.SessionEnded },
            EffectiveDateAndAuditInvariants: new[]
            {
                "عدم شحن أي مستخدم افتراضي في V001_InitialSchema.sql (PreSeededUsersCount == 0).",
                "رفض تسجيل الدخول عند رصد تراجع التاريخ/الوقت عن LastObservedUtcTimestamp (ClockRollbackSuspected).",
                "تسجيل محاولات الدخول الفاشلة في FailedLoginEvents وإصدار قيد AuditEvents عند التهيئة والدخول والخروج."
            },
            ExternalVerificationSteps: new[]
            {
                "فحص قاعدة البيانات الأولية والتأكد من أن جدول Users فارغ تماماً.",
                "التحقق من رفض كلمات المرور الأقل من 10 خانات أو الخالية من 3 أحرف إنجليزية و3 أرقام ورمز خاص.",
                "اختبار التهيئة الأولى ثم تسجيل الدخول وإبطال الجلسة عند Logout."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-02-DASHBOARD-SUMMARY",
            SourcePassageArabic: "أولاً: الشاشة الرئيسية — عرض إجمالي القوة وبطاقات الحالات وبطاقة غير المحضرين ونسبة اكتمال التحضير وجدول القوة اليومي مع الفلترة بحسب الموقع والحالة.",
            ApprovedDecisionArabic: "الاحتفاظ ببطاقات العدادات والشرائح اللونية؛ عرض الحالات النشطة المعتمدة فقط دون بيانات وهمية قبل الربط؛ تحديث العدادات فور تغير التحضير أو القوة.",
            ImplementationPhase: "06",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Contracts.Dtos.DailyAttendanceDto",
                "AlTamam.Contracts.Dtos.StatusDto",
                "V001_InitialSchema.sql (Persons, Sites, PersonAssignments, AttendanceStatusDefinitions, AttendanceRecords)",
                "src/AlTamam.WebUI/index.html & screens/Dashboard/index.html"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "DashboardService implementation (GetDashboardSummary, QueryDailyRoster, QueryUnprepared, GetAutoAttendanceRun)",
                "Interactive card & chip filtering via WebView2 bridge"
            },
            ExpectedTables: new[]
            {
                "Persons", "Sites", "PersonAssignments", "AttendanceStatusDefinitions",
                "AttendanceRecords", "AutoAttendanceRuns", "CustomFieldDefinitions", "CustomFieldValues"
            },
            OwningServices: new[] { "DashboardService" },
            BridgeActions: new[] { "GetDashboardSummary", "QueryDailyRoster", "QueryUnprepared", "GetAutoAttendanceRun" },
            ScreenRouteKeys: new[] { "Dashboard", "UnpreparedPersonnel", "SelectStatusAndConfirm" },
            ErrorCodes: new[] { BridgeErrorCodes.SessionExpired, BridgeErrorCodes.ValidationError },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.AttendanceCommitted,
                UiInvalidationEvents.AttendanceRevised,
                UiInvalidationEvents.PersonnelMoved,
                UiInvalidationEvents.PersonnelArchived,
                UiInvalidationEvents.StatusDefinitionCreated,
                UiInvalidationEvents.StatusDefinitionChanged
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "حساب القوة اليومية للموقع بناءً على PersonAssignments السارية في CurrentBusinessDate.",
                "استثناء الأفراد المنقولين داخلياً في نفس اليوم من عدّاد القابليـن للتحضير (IsMovementExempt = true)."
            },
            ExternalVerificationSteps: new[]
            {
                "مطابقة مجموع عدادات الحالات + غير المحضرين مع إجمالي القوة الفعالة للموقع المختار.",
                "التحقق من انتقال المستخدم من بطاقة غير المحضرين إلى شاشة UnpreparedPersonnel ثم SelectStatusAndConfirm."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-03-DAILY-ATTENDANCE-AND-DRAFTS",
            SourcePassageArabic: "شاشة الإدارة > إدارة القوة > التمام: تحضير الأفراد ليوم العمل الحالي (فردي أو جماعي)، تعديل تحضير اليوم، والحفظ المؤقت للمسودات.",
            ApprovedDecisionArabic: "منع التحضير في يوم النقل الداخلي (TransferDayAttendanceForbidden) ومنع التحضير للأيام المغلقة السابقة (HistoricDayLocked) إلا عبر التصحيح الاستثنائي المقيّد لمدير النظام العام.",
            ImplementationPhase: "06",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Application.Rules.AttendanceDayPolicy",
                "AlTamam.Domain.Entities.AttendanceRecord",
                "AlTamam.Domain.Entities.AttendanceRevision",
                "AlTamam.Domain.Entities.AttendanceDraft",
                "AlTamam.Domain.Entities.PeriodClosure",
                "V001_InitialSchema.sql (AttendanceRecords, AttendanceRevisions, AttendanceDrafts, PeriodClosures)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "AttendanceService implementation (GetActiveAttendanceStatuses, PreviewAttendanceChange, SubmitAttendance, ReviseTodayAttendance, SaveAttendanceDraft, ListAttendanceDrafts, DeleteAttendanceDraft)"
            },
            ExpectedTables: new[]
            {
                "AttendanceRecords", "AttendanceRevisions", "AttendanceDrafts",
                "PeriodClosures", "PersonAssignments", "PersonnelMovements",
                "AttendanceStatusDefinitions", "AuditEvents"
            },
            OwningServices: new[] { "AttendanceService" },
            BridgeActions: new[]
            {
                "GetActiveAttendanceStatuses", "PreviewAttendanceChange", "SubmitAttendance",
                "ReviseTodayAttendance", "SaveAttendanceDraft", "ListAttendanceDrafts", "DeleteAttendanceDraft",
                "PreviewClosedDayCorrection", "CommitClosedDayCorrection"
            },
            ScreenRouteKeys: new[]
            {
                "Attendance", "ConfirmAttendance", "DraftsAndTemporarySave", "ClosedDayExceptionalCorrection"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.AlreadyPrepared,
                BridgeErrorCodes.TransferDayAttendanceForbidden,
                BridgeErrorCodes.HistoricDayLocked,
                BridgeErrorCodes.CustomStatusInactive,
                BridgeErrorCodes.ConflictStale,
                BridgeErrorCodes.StorageBusy,
                BridgeErrorCodes.MissingData
            },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.AttendanceCommitted,
                UiInvalidationEvents.AttendanceRevised
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "سجل تحضير واحد فقط لكل فرد في اليوم UNIQUE(PersonId, AttendanceLocalDate).",
                "حفظ لقطة الحالة والموقع (StatusNameSnapshot, StatusSymbolSnapshot, StatusColorSnapshot, SiteNameSnapshot) وقت الاعتماد.",
                "تسجيل كل تعديل لاحق لنفس اليوم في AttendanceRevisions وAuditEvents داخل نفس المعاملة."
            },
            ExternalVerificationSteps: new[]
            {
                "اختبار رفض تحضير فرد له حركة نقل داخلي في نفس اليوم بكود TransferDayAttendanceForbidden.",
                "اختبار تعديل حالة فرد محضّر مسبقاً والتحقق من إنشاء سجل في AttendanceRevisions."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-04-AUTO-20H-SCHEDULER",
            SourcePassageArabic: "استكمال تحضير غير المحضرين تلقائياً عند الساعة الثامنة مساءً (20:00) بنسخ حالة يوم أمس.",
            ApprovedDecisionArabic: "تنفيذ الترحيل التلقائي الساعة 20:00 محلياً + تشغيل تعويضي (Catch-Up) عند فتح البرنامج إذا فات موعد الثامنة مساءً، مع استثناء من ليس له حالة أمس ومن نُقل داخلياً في اليوم نفسه.",
            ImplementationPhase: "07",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.Entities.AutoAttendanceRun",
                "AlTamam.Domain.Enums.AttendanceSource (Auto20hCarryForward, SystemCatchUp20h)",
                "AlTamam.Domain.ValueObjects.UserId.SystemActor (USR-SYSTEM)",
                "V001_InitialSchema.sql (AutoAttendanceRuns)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "EightPmAutoAttendanceScheduler background/startup runner",
                "DashboardService.GetAutoAttendanceRun query integration"
            },
            ExpectedTables: new[] { "AutoAttendanceRuns", "AttendanceRecords", "PersonnelMovements", "PersonAssignments", "AuditEvents" },
            OwningServices: new[] { "DashboardService", "AttendanceService" },
            BridgeActions: new[] { "GetAutoAttendanceRun" },
            ScreenRouteKeys: new[] { "EightPmScheduler" },
            ErrorCodes: new[] { BridgeErrorCodes.SessionExpired, BridgeErrorCodes.StorageBusy },
            InvalidationEvents: new[] { UiInvalidationEvents.AttendanceCommitted },
            EffectiveDateAndAuditInvariants: new[]
            {
                "عدم تكرار التشغيل لنفس اليوم بفضل UNIQUE(BusinessDate) ومفتاح JobKey (AUTO20H-YYYY-MM-DD).",
                "تنفيذ النسخ بواسطة USR-SYSTEM مع توثيق الأعداد (CopiedCount, SkippedNoYesterdayCount, SkippedTransferExemptCount)."
            },
            ExternalVerificationSteps: new[]
            {
                "محاكاة تجاوز الساعة 20:00 عبر ISystemClock والتحقق من ترحيل حالات الأمس لغير المحضرين فقط.",
                "التحقق من عدم شمول الأفراد المنقولين داخلياً في نفس اليوم بالترحيل التلقائي."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-05-MONTHLY-ATTENDANCE-ARCHIVE",
            SourcePassageArabic: "ثانياً: التمام الشهري — جدول شهري للأيام (1..31) مع تثبيت الأعمدة الأربعة الأولى (الرقم، الرقم (ع)، الاسم، الموقع) وفلترة المواقع واختيار الأرشيف.",
            ApprovedDecisionArabic: "عرض السجلات التاريخية من اللقطات المحفوظة وقت حدوثها دون تأثر بتغيير اسم الفرد أو الموقع أو الحالة لاحقاً، وعرض خلايا النقل الداخلي والخارجي والفصل بدقة.",
            ImplementationPhase: "08",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.Enums.MonthlyCellKind",
                "AlTamam.Contracts.Dtos.MonthlyCellDto",
                "src/AlTamam.WebUI/assets/css/altamam.css (sticky-col-1..4)",
                "screens/MonthlyAttendance/index.html & screens/MonthlyAttendance/SelectArchive/index.html"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "MonthlyGridService implementation (GetAvailableArchivePeriods, QueryMonthlyGrid, GetMonthlyCellDetails)"
            },
            ExpectedTables: new[]
            {
                "AttendanceRecords", "PersonnelMovements", "PersonAssignments",
                "PersonIdentityHistory", "SiteNameHistory", "AttendanceStatusNameHistory"
            },
            OwningServices: new[] { "MonthlyGridService" },
            BridgeActions: new[] { "GetAvailableArchivePeriods", "QueryMonthlyGrid", "GetMonthlyCellDetails" },
            ScreenRouteKeys: new[] { "MonthlyAttendance", "SelectArchive" },
            ErrorCodes: new[] { BridgeErrorCodes.SessionExpired, BridgeErrorCodes.ValidationError, BridgeErrorCodes.MissingData },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.AttendanceCommitted,
                UiInvalidationEvents.AttendanceRevised,
                UiInvalidationEvents.PersonnelMoved
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "قراءة خلايا الشهر من اللقطات التاريخية المؤرخة (Snapshots) وليس من الأسماء الحالية المتغيرة.",
                "إظهار الفرد المنقول داخلياً في الموقع القديم حتى تاريخ النقل وفي الموقع الجديد من تاريخ النقل فصاعداً."
            },
            ExternalVerificationSteps: new[]
            {
                "تغيير اسم موقع أو حالة في شهر لاحق والتحقق من بقاء عرض الشهر السابق بالاسم القديم في التمام الشهري.",
                "فحص خلايا يوم النقل الداخلي للتأكد من ظهور إشارة النقل بدلاً من حالة تحضير."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-06-PERSONNEL-IDENTITY-AND-ARCHIVE",
            SourcePassageArabic: "شاشة الإدارة > إدارة القوة > تعديل الأسماء (تعديل الاسم أو الرقم العسكري، أو حذف أسماء من القوة) مع البحث الموحد.",
            ApprovedDecisionArabic: "ربط جميع الجداول بـ PersonId غير القابل للتغيير؛ حفظ تاريخ الهوية في PersonIdentityHistory؛ حصر حذف/أرشفة الأسماء من القوة بالإدارة العامة والإدارة العادية فقط مع بقاء الأرشيف التاريخي.",
            ImplementationPhase: "04-05",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.ValueObjects.PersonId",
                "AlTamam.Domain.Entities.Person & PersonIdentityHistory",
                "AlTamam.Domain.Policies.ArabicTextNormalizer",
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanArchivePersonsFromActiveForce",
                "V001_InitialSchema.sql (Persons, PersonIdentityHistory, IX_Persons_Active_ServiceNumberA)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "PersonRosterService implementation (SearchPersons, GetPersonSummary, QueryPersons, PreviewPersonIdentityUpdate, UpdatePersonIdentity, PreviewArchivePersons, ArchivePersons)"
            },
            ExpectedTables: new[] { "Persons", "PersonIdentityHistory", "CustomFieldValues", "AuditEvents" },
            OwningServices: new[] { "PersonRosterService" },
            BridgeActions: new[]
            {
                "SearchPersons", "GetPersonSummary", "QueryPersons", "CreatePerson",
                "PreviewPersonIdentityUpdate", "UpdatePersonIdentity",
                "PreviewArchivePersons", "ArchivePersons"
            },
            ScreenRouteKeys: new[]
            {
                "Administration", "PersonnelManagement", "EditPersonnelNames",
                "EditPersonDetails", "RemovePersonnel", "PersonPickerAndSearch",
                "ConfirmationAndWarning", "ConflictsAndNotifications"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.MissingData,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.DuplicateServiceNumber,
                BridgeErrorCodes.ConflictStale
            },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.PersonIdentityUpdated,
                UiInvalidationEvents.PersonnelArchived
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "منع تكرار الرقم (ع) بين الأفراد النشطين عبر الفهرس الفريد IX_Persons_Active_ServiceNumberA بعد تطبيع الأرقام.",
                "عند تعديل الاسم أو الرقم (ع) يُغلق سجل PersonIdentityHistory السابق ويُفتح سجل جديد بتاريخ السريان دون مساس بـ PersonId."
            },
            ExternalVerificationSteps: new[]
            {
                "البحث عن فرد بأحرف عربية مختلفة الهمزات أو بأرقام هندية/عربية والتحقق من تطابق النتائج.",
                "التحقق من رفض تنفيذ ArchivePersons عند استدعائه بدور Supervisor بكود Unauthorized."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-07-MOVEMENTS-AND-SEPARATION",
            SourcePassageArabic: "شاشة الإدارة > إدارة القوة > التنقلات (النقل الداخلي، النقل الخارجي، الأرشيف) والفصل (فصل فرد، أرشيف المفصولين).",
            ApprovedDecisionArabic: "النقل الداخلي يغلق فترة الموقع السابق ويفتح فترة الموقع الجديد اعتباراً من EffectiveLocalDate ويمنع تحضير الفرد في يوم النقل؛ النقل الخارجي والفصل يغيران OrganizationalStatus ويغلقان الفترة النشطة مع حفظ الأرشيف.",
            ImplementationPhase: "08",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Application.Rules.AssignmentPeriodPolicy",
                "AlTamam.Application.Rules.AttendanceDayPolicy",
                "AlTamam.Domain.Entities.PersonnelMovement & PersonAssignment",
                "V001_InitialSchema.sql (PersonnelMovements, PersonAssignments, TR_PersonAssignments_ValidateInsert/Update)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "MovementService implementation (11 actions for internal transfer, external transfer, separation, and archives)"
            },
            ExpectedTables: new[] { "PersonnelMovements", "PersonAssignments", "Persons", "Sites", "AuditEvents" },
            OwningServices: new[] { "MovementService" },
            BridgeActions: new[]
            {
                "QueryActivePersonsBySite", "PreviewInternalTransfer", "CommitInternalTransfer",
                "PreviewExternalTransfer", "CommitExternalTransfer", "QueryMovementArchive",
                "GetMovementDetails", "PreviewSeparation", "CommitSeparation",
                "QuerySeparationArchive", "GetSeparationDetails"
            },
            ScreenRouteKeys: new[]
            {
                "Transfers", "InternalTransfer", "ConfirmTransfer",
                "ExternalTransfer", "ConfirmExternalTransfer", "TransferArchive",
                "Dismissal", "DismissPerson", "ConfirmDismissal", "DismissedPersonnelArchive"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.MissingData,
                BridgeErrorCodes.InvalidTransferSites,
                BridgeErrorCodes.OverlappingAssignments,
                BridgeErrorCodes.ConflictStale
            },
            InvalidationEvents: new[] { UiInvalidationEvents.PersonnelMoved },
            EffectiveDateAndAuditInvariants: new[]
            {
                "فترات انتساب المواقع نصف مفتوحة [ValidFromLocalDate, ValidToLocalDate) ومحمية بـ AssignmentPeriodPolicy وTriggers قاعدة البيانات.",
                "منع النقل الداخلي إلى نفس الموقع الحالي (InvalidTransferSites)."
            },
            ExternalVerificationSteps: new[]
            {
                "تنفيذ نقل داخلي والتحقق من إغلاق PersonAssignment السابق وفتح الجديد بنفس التاريخ بدون تداخل.",
                "التحقق من ظهور الحركة في TransferArchive وDismissedPersonnelArchive مع اللقطات التاريخية."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-08-REPORTS-AND-PERSON-LIFETIME",
            SourcePassageArabic: "شاشة الإدارة > إدارة القوة > التقرير (والتقارير والفصل > التقارير): عرض السجل الشامل للفرد منذ تسجيله وإجمالي الأيام المعتمدة لكل حالة مستخدمة وتنقلاته وتصديره إلى Excel.",
            ApprovedDecisionArabic: "إظهار الحالات التي استُخدمت فعلياً للفرد فقط مع مجموع أيامها المعتمدة (UsedStatusCountDto) وعدد مرات النقل الداخلي والخط الزمني للعمليات.",
            ImplementationPhase: "09",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Contracts.Dtos.LifetimeSummaryDto & UsedStatusCountDto",
                "screens/Administration/PersonnelManagement/PersonnelReport/index.html",
                "screens/Administration/PersonnelManagement/ReportsAndDismissal/Reports/index.html"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "ReportingService implementation (QueryPersonAttendanceTimeline, QueryPersonAttendanceTotals, BuildPersonLifetimeReport, QueryPersonOperationsTimeline, QueryPersonMovements, ExportPersonLifetimeExcel)"
            },
            ExpectedTables: new[]
            {
                "Persons", "PersonIdentityHistory", "AttendanceRecords",
                "PersonnelMovements", "PersonAssignments", "AuditEvents"
            },
            OwningServices: new[] { "ReportingService" },
            BridgeActions: new[]
            {
                "QueryPersonAttendanceTimeline", "QueryPersonAttendanceTotals", "BuildPersonLifetimeReport",
                "QueryPersonOperationsTimeline", "QueryPersonMovements", "ExportPersonLifetimeExcel"
            },
            ScreenRouteKeys: new[]
            {
                "ReportsAndDismissal", "Reports", "PersonnelReport", "ExportPersonReport"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.MissingData,
                BridgeErrorCodes.ExcelExportFailed,
                BridgeErrorCodes.InsufficientDiskSpace
            },
            InvalidationEvents: Array.Empty<string>(),
            EffectiveDateAndAuditInvariants: new[]
            {
                "حساب إجماليات الحالات من سجلات AttendanceRecords المعتمدة فقط للفرد عبرPersonId.",
                "إرفاق HistoryQueryToken للتحقق من اتساق التقرير عند التصدير."
            },
            ExternalVerificationSteps: new[]
            {
                "التحقق من أن UsedStatusCounts يعرض فقط الحالات التي رُصدت للفرد (> 0) دون الحالات غير المستخدمة.",
                "تصدير تقرير الفرد والتحقق من صحة ملف Excel الناتج."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-09-STATUSES-SITES-AND-COLUMNS",
            SourcePassageArabic: "شاشة الإدارة > الحالات والمواقع (إضافة/تعديل/حذف حالة أو موقع) والكشوفات > تعديل الهيكل للكشف (الأعمدة المخصصة).",
            ApprovedDecisionArabic: "حماية الموقع النظامي «غير محدد» (SITE-UNSPECIFIED) والأعمدة الأربعة النظامية؛ عند حذف موقع يتم نقل أفراده الحاليين إلى «غير محدد» أو موقع بديل اعتباراً من تاريخ الحذف دون مساس بالسجل التاريخي؛ حفظ تاريخ تغير أسماء وألوان الحالات والمواقع.",
            ImplementationPhase: "05 / 10",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.ValueObjects.SiteId.Unspecified (SITE-UNSPECIFIED)",
                "AlTamam.Domain.Entities.Site, SiteNameHistory, AttendanceStatusDefinition, AttendanceStatusNameHistory, CustomFieldDefinition, CustomFieldValue",
                "V001_InitialSchema.sql (Seeded SITE-UNSPECIFIED, 4 statuses, 4 protected columns)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "ConfigurationService implementation (12 actions for statuses, sites, and custom field definitions)"
            },
            ExpectedTables: new[]
            {
                "AttendanceStatusDefinitions", "AttendanceStatusNameHistory",
                "Sites", "SiteNameHistory", "PersonAssignments",
                "CustomFieldDefinitions", "CustomFieldValues", "AuditEvents"
            },
            OwningServices: new[] { "ConfigurationService" },
            BridgeActions: new[]
            {
                "QueryAttendanceStatuses", "CreateAttendanceStatus", "UpdateAttendanceStatus",
                "DeactivateOrDeleteStatus", "QuerySites", "CreateSite", "RenameSite",
                "RemoveSiteAndReassignCurrentPersons", "PreviewSiteRemoval", "GetCustomFieldDefinitions",
                "CreateCustomFieldDefinition", "UpdateCustomFieldMetadata", "DeactivateCustomField"
            },
            ScreenRouteKeys: new[]
            {
                "StatusesAndSites", "Statuses", "AddStatus", "EditStatus", "DeleteStatus",
                "Sites", "AddSite", "EditSite", "DeleteSite",
                "RosterLayout", "AddOrEditColumn"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.ConflictStale
            },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.SiteCreated,
                UiInvalidationEvents.SiteRenamed,
                UiInvalidationEvents.StatusDefinitionCreated,
                UiInvalidationEvents.StatusDefinitionChanged,
                UiInvalidationEvents.PersonnelMoved
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "منع حذف أو تعطيل الموقع النظامي SITE-UNSPECIFIED والأعمدة المحمية (IsSystemProtected = 1).",
                "تسجيل كل تعديل لاسم الموقع أو الحالة في SiteNameHistory / AttendanceStatusNameHistory."
            },
            ExternalVerificationSteps: new[]
            {
                "التحقق من وجود الموقع «غير محدد» والحالات الأربع والأعمدة الأربعة النظامية فور تهيئة قاعدة البيانات.",
                "اختبار حذف موقع يضم أفراداً والتحقق من إعادة تسكينهم في «غير محدد» دون تعديل سجلات الأشهر السابقة."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-10-EXCEL-IMPORT-AND-EXPORT",
            SourcePassageArabic: "شاشة الإدارة > الكشوفات > استيراد وتصدير ملفات Excel (استيراد أوراق العمل كمواقع وتصدير التمام الشهري).",
            ApprovedDecisionArabic: "معاينة وتحقق قبل الاعتماد (PreviewAndValidate)؛ إنشاء المواقع تلقائياً من أسماء أوراق العمل؛ عزل الصفوف المعيبة في ImportIssues مع إمكانية تصديرها؛ التحقق من بصمة الملف لمنعImportPlanStale.",
            ImplementationPhase: "10",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.ValueObjects.ImportBatchId & ReportJobId",
                "AlTamam.Domain.Entities.ImportBatch & ImportIssue",
                "V001_InitialSchema.sql (ImportBatches, ImportIssues)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "ExcelSheetsService implementation (11 actions for Excel import/export and progress tracking)"
            },
            ExpectedTables: new[]
            {
                "ImportBatches", "ImportIssues", "Persons", "PersonIdentityHistory",
                "Sites", "SiteNameHistory", "PersonAssignments", "CustomFieldValues", "AuditEvents"
            },
            OwningServices: new[] { "ExcelSheetsService" },
            BridgeActions: new[]
            {
                "SelectImportWorkbook", "AnalyzeImport", "PreviewImportPlan", "CommitImport",
                "QueryImportOutcome", "ExportImportIssues", "GetExportablePeriods",
                "PreviewMonthlyExport", "ExportMonthlyWorkbookSet", "GetExportProgress", "CancelExport"
            },
            ScreenRouteKeys: new[]
            {
                "Rosters", "Import", "SelectFile", "PreviewAndValidate", "ImportResults",
                "Export", "PreviewAndConfirmExport", "ExportProgressAndResult"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.InvalidWorkbook,
                BridgeErrorCodes.DuplicateServiceNumber,
                BridgeErrorCodes.ImportPlanStale,
                BridgeErrorCodes.StorageBusy,
                BridgeErrorCodes.MissingData,
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.ExcelExportFailed,
                BridgeErrorCodes.InsufficientDiskSpace
            },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.SiteCreated,
                UiInvalidationEvents.PersonnelMoved,
                UiInvalidationEvents.PersonIdentityUpdated
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "التحقق من تطابق SourceFileSha256 بين مرحلة المعاينة ومرحلة الاعتماد وإلا يُرفض بـ ImportPlanStale.",
                "تنفيذ CommitImport داخل معاملة ذرية واحدة تُنشئ المواقع والأفراد وانتساباتهم وتاريخ الهوية."
            },
            ExternalVerificationSteps: new[]
            {
                "استيراد مصنف متعدد الأوراق والتحقق من إنشاء المواقع وقبول الصفوف السليمة وتسجيل الصفوف المكررة في ImportIssues.",
                "تصدير التمام الشهري والتحقق من مطابقة الخلايا للبيانات التاريخية."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-11-USERS-AND-AUDIT-OVERLAY",
            SourcePassageArabic: "شاشة الإدارة > المستخدمين وسجل العمليات.",
            ApprovedDecisionArabic: "ربط الحسابات الجديدة بأفراد الكشف؛ عرض المنفذ بصيغة [اسم المستخدم] - [الاسم الكامل بحسب الكشف]؛ السماح لكل من الإدارة العامة والإدارة العادية بتعديل وحذف القيود الظاهرة في سجل العمليات ومراجعة سجل القيود المعدلة والمحذوفة، ومنع المشرف كلياً؛ أثناء الاستخدام المعتاد لا يُحذف AuditEvents الخام، والاستثناء الوحيد هو التصفير الشامل المعتمد الذي يمحو السجل الأصلي والطبقة الظاهرية والتاريخ كله.",
            ImplementationPhase: "03 / 11",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy (CanAccessUsersManagement, CanCreateAccountWithRole, CanModifyAccount, CanEditOrDeleteVisibleAuditLog, CanReviewAuditOverlayHistory)",
                "AlTamam.Domain.Entities.UserAccount, UserRoleHistory, AuditEvent.ProjectActorDisplay(), AuditLogPresentationEdit",
                "AlTamam.Contracts.Dtos.AuditPresentationDto",
                "V001_InitialSchema.sql (Users, UserRoleHistory, AuditEvents, AuditLogPresentationEdits)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "UserAuditService implementation (10 actions for user management and audit log presentation overlays)"
            },
            ExpectedTables: new[]
            {
                "Users", "UserRoleHistory", "Persons", "AuditEvents", "AuditLogPresentationEdits"
            },
            OwningServices: new[] { "UserAuditService" },
            BridgeActions: new[]
            {
                "ListUserAccounts", "SearchActivePersonsForAccount", "CreateUserAccount",
                "ChangeUserPassword", "DeactivateUserAccount", "QueryAuditLog",
                "GetAuditEntryDetails", "UpdateAuditPresentation", "DeleteAuditPresentation",
                "QueryAuditPresentationChanges"
            },
            ScreenRouteKeys: new[]
            {
                "Users", "AddUser", "ChangePassword", "DeleteAccount",
                "AuditLog", "EditAuditEntry", "DeleteAuditEntry", "ModifiedDeletedAuditEntries"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.DuplicateUsername,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.MissingData
            },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.UserAccountChanged,
                UiInvalidationEvents.VisibleAuditOverlayChanged
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "أثناء الاستخدام المعتاد الجدول الأساسي AuditEvents غير قابل للتعديل أو الحذف الفيزيائي ومرتبط بسلسلة تجزئة (EventHash / PriorEventHash)؛ التصفير الشامل المعتمد استثناء وحيد يمحو قاعدة البيانات بما فيها السجل الخام والتاريخ كله.",
                "التعديل أو الحذف الظاهري يُحفظ كطبقة فوقية في AuditLogPresentationEdits مع رقم إصدار OverlayVersion وسبب التعديل والمنفذ."
            },
            ExternalVerificationSteps: new[]
            {
                "التحقق من أن ProjectActorDisplay() يعرض [اسم المستخدم] - [الاسم الكامل بحسب الكشف] للحسابات المرتبطة.",
                "اختبار قدرة كل من GeneralAdministration وOrdinaryAdministration على تعديل/حذف قيد ظاهر في سجل العمليات ومنع Supervisor؛ منع تعديل أو مسح AuditEvents الخام أثناء التشغيل المعتاد، مع تأكيد إزالته مع طبقة العرض عند التصفير الشامل وحده."
            }),

        new CanonicalRequirementTraceDto(
            RequirementId: "REQ-12-SETTINGS-BACKUP-RESTORE-RESET",
            SourcePassageArabic: "رابعاً: الإعدادات — نسخة احتياطية، استيراد نسخة احتياطية، النسخ الاحتياطي التلقائي الساعة 16:00 على القرص E:\\، حذف جميع بيانات البرنامج وجميع نسخه الاحتياطية بعد التحذير وتأكيد كلمة المرور، بيانات البرنامج، المظهر وفحص السلامة.",
            ApprovedDecisionArabic: "فحص سلامة النسخ الاحتياطية عند إنشائها أو استعادتها (SHA-256 + PRAGMA integrity_check)؛ حذف بيانات البرنامج وجميع النسخ الاحتياطية المُدارة داخل مساراته المعتمدة حصري للإدارة العامة بعد تحذير يبين النطاق وإعادة تحقق كلمة المرور الحالية وتأكيد صريح؛ ويشمل الحذف جميع سجلات العمليات (AuditEvents وAuditLogPresentationEdits) والسجلات التاريخية للفرد والتحضير والنقل والأرشيف، دون استثناء لسجلات التدقيق؛ إنشاء نسخة احتياطية قبل الحذف اختياري وليس شرطاً؛ لا تُحذف ملفات أو نسخ احتياطية خارج المسارات المخصصة للبرنامج.",
            ImplementationPhase: "12 / 13",
            CurrentStage: RequirementImplementationStage.FoundationReadyServicePlanned,
            ImplementedArtifactsInCurrentCode: new[]
            {
                "AlTamam.Domain.Policies.RoleAuthorizationPolicy.CanExecuteFullDatabaseReset",
                "AlTamam.Domain.Entities.BackupRecord & AppSetting",
                "AlTamam.Infrastructure.Persistence.AlTamamDbContext.HasValidIntegrity()",
                "V001_InitialSchema.sql (BackupHistory, AppSettings seeded with AutoBackupTimeLocal=16:00 & PreferredBackupDirectory=E:\\AlTamamAlYawmi\\Backups)"
            },
            PlannedArtifactsInUpcomingPhases: new[]
            {
                "SettingsBackupService implementation (13 actions for backup, verification, atomic restore, 16:00 schedule, full reset, theme, and version)"
            },
            ExpectedTables: new[] { "BackupHistory", "AppSettings", "AuditEvents" },
            OwningServices: new[] { "SettingsBackupService" },
            BridgeActions: new[]
            {
                "GetSettingsOverview", "CreateVerifiedBackup", "VerifyBackup", "InspectBackup",
                "PreviewRestore", "RestoreDatabaseAtomically", "GetBackupSchedule",
                "UpdateBackupSchedule", "RunDatabaseIntegrityCheck", "PreviewFullReset",
                "CommitFullReset", "SetTheme", "GetApplicationVersion"
            },
            ScreenRouteKeys: new[]
            {
                "Settings", "CreateBackup", "RestoreBackup", "ScheduledBackups",
                "ResetApplicationData", "AboutApplication", "AppearanceAndDiagnostics"
            },
            ErrorCodes: new[]
            {
                BridgeErrorCodes.Unauthorized,
                BridgeErrorCodes.SessionExpired,
                BridgeErrorCodes.ValidationError,
                BridgeErrorCodes.BackupIntegrityFailed,
                BridgeErrorCodes.WrongRecoveryKey,
                BridgeErrorCodes.MissingEVolume,
                BridgeErrorCodes.InsufficientDiskSpace,
                BridgeErrorCodes.ScheduleUnavailable,
                BridgeErrorCodes.ResetConfirmationRequired,
                BridgeErrorCodes.StorageBusy
            },
            InvalidationEvents: new[]
            {
                UiInvalidationEvents.BackupRestored,
                UiInvalidationEvents.SessionEnded
            },
            EffectiveDateAndAuditInvariants: new[]
            {
                "رفض CommitFullReset لغير GeneralAdministration (Unauthorized)، أو عند عدم إقرار تحذير يشمل حذف البيانات والنسخ المُدارة وسجل العمليات الخام والتاريخ وعدم تأكيد الحذف (ResetConfirmationRequired)، أو عدم التحقق الآمن من كلمة المرور الحالية (Unauthorized)، بغض النظر عن وجود نسخة احتياطية حديثة. عند نجاح التصفير الشامل تُزال جميع بيانات البرنامج التاريخية (بما فيها AuditEvents وAuditLogPresentationEdits) ولا يُحتفظ بسجل تدقيق داخل قاعدة البيانات المحذوفة. لا يُسمح بحذف أي مسار خارج الجذور المخصصة للبرنامج؛ تُرفض محاولات اجتياز المسارات والروابط المعيدة للتوجيه.",
                "فحص PRAGMA integrity_check وSHA-256 قبل اعتماد أي نسخة احتياطية أو استعادتها."
            },
            ExternalVerificationSteps: new[]
            {
                "التحقق من إعدادات البذر الافتراضية (AutoBackupTimeLocal=16:00، PreferredBackupDirectory=E:\\AlTamamAlYawmi\\Backups).",
                "اختبار منع OrdinaryAdministration وSupervisor من تنفيذ PreviewFullReset/CommitFullReset، ورفض كلمة المرور غير المتحقق منها أو غياب التحذير أو التأكيد، وقبول الحذف دون نسخة احتياطية إلزامية. اختبر أثناء الاستخدام المعتاد منع الحذف الفيزيائي لـ AuditEvents مع السماح بتعديل العرض؛ وعند التصفير الشامل تحقق من اختفاء AuditEvents وAuditLogPresentationEdits وجميع تواريخ الهوية والتحضير والنقل والأرشيف، وحذف النسخ المُدارة داخل المسارات المعتمدة فقط، ورفض مسارات الخروج والروابط الرمزية، وبقاء نسخ الاختبار الموجودة خارج جذور البرنامج دون مساس."
            })
    };

    /// <summary>
    /// Validates static completeness and consistency of the canonical requirement traceability catalog
    /// against all 26 schema tables, 11 services, 90 bridge actions, 69 screen routes,
    /// 24 error codes, and 13 UI invalidation events.
    /// </summary>
    public ServiceResult<RequirementTraceabilityReportDto> BuildAndValidateStaticReport()
    {
        var requirements = GetCanonicalRequirementsCatalog();

        var mappedTables = requirements.SelectMany(r => r.ExpectedTables).ToHashSet(StringComparer.Ordinal);
        var mappedServices = requirements.SelectMany(r => r.OwningServices).ToHashSet(StringComparer.Ordinal);
        var mappedActions = requirements.SelectMany(r => r.BridgeActions).ToHashSet(StringComparer.Ordinal);
        var mappedScreens = requirements.SelectMany(r => r.ScreenRouteKeys).ToHashSet(StringComparer.Ordinal);
        var mappedErrorCodes = requirements.SelectMany(r => r.ErrorCodes).ToHashSet(StringComparer.Ordinal);
        var mappedEvents = requirements.SelectMany(r => r.InvalidationEvents).ToHashSet(StringComparer.Ordinal);

        var unmappedTables = CanonicalTables.Where(t => !mappedTables.Contains(t)).ToArray();
        var unmappedServices = CanonicalServices.Where(s => !mappedServices.Contains(s)).ToArray();
        var unmappedActions = CanonicalActions.Where(a => !mappedActions.Contains(a)).ToArray();
        var unmappedScreens = CanonicalScreens.Where(s => !mappedScreens.Contains(s)).ToArray();

        var unknownErrorCodes = mappedErrorCodes.Except(BridgeErrorCodes.AllCanonicalErrorCodes, StringComparer.Ordinal).ToArray();
        var unknownEvents = mappedEvents.Except(UiInvalidationEvents.AllCanonicalEvents, StringComparer.Ordinal).ToArray();

        bool isConsistent = unmappedTables.Length == 0
            && unmappedServices.Length == 0
            && unmappedActions.Length == 0
            && unmappedScreens.Length == 0
            && unknownErrorCodes.Length == 0
            && unknownEvents.Length == 0;

        var report = new RequirementTraceabilityReportDto(
            GeneratedAtUtc: _clock.UtcNow,
            IsCompleteAndConsistent: isConsistent,
            TotalRequirementsCount: requirements.Count,
            TotalCanonicalTablesCount: CanonicalTables.Count,
            TotalCanonicalServicesCount: CanonicalServices.Count,
            TotalCanonicalActionsCount: CanonicalActions.Count,
            TotalCanonicalScreensCount: CanonicalScreens.Count,
            Requirements: requirements,
            UnmappedTables: unmappedTables,
            UnmappedServices: unmappedServices,
            UnmappedActions: unmappedActions,
            UnmappedScreens: unmappedScreens,
            DatabaseSnapshot: null);

        if (!isConsistent)
        {
            return ServiceResult<RequirementTraceabilityReportDto>.Fail(
                BridgeErrorCodes.ValidationError,
                "مصفوفة مطابقة المتطلبات غير مكتملة أو تحتوي على عناصر غير مربوطة.",
                $"UnmappedTables={unmappedTables.Length}, UnmappedServices={unmappedServices.Length}, UnmappedActions={unmappedActions.Length}, UnmappedScreens={unmappedScreens.Length}");
        }

        return ServiceResult<RequirementTraceabilityReportDto>.Ok(report);
    }

    /// <summary>
    /// Validates both the static requirement traceability matrix and the live encrypted SQLite/SQLCipher database,
    /// optionally recording a tamper-evident audit event inside a write transaction.
    /// </summary>
    public async Task<ServiceResult<RequirementTraceabilityReportDto>> VerifyAgainstDatabaseAsync(
        bool recordAuditEvent = false,
        UserId? actorUserId = null,
        PersonId? actorPersonId = null,
        string? actorUsernameSnapshot = null,
        string? actorRosterFullNameSnapshot = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var staticResult = BuildAndValidateStaticReport();
        if (!staticResult.IsSuccess || staticResult.Value is null)
        {
            return staticResult;
        }

        if (_schemaProbe is null)
        {
            return ServiceResult<RequirementTraceabilityReportDto>.Fail(
                BridgeErrorCodes.MissingData,
                "لم يتم توفير خدمة فحص قاعدة البيانات (ISchemaCatalogProbe).");
        }

        var probeResult = await _schemaProbe.InspectSchemaAndSeedStateAsync(cancellationToken).ConfigureAwait(false);
        if (!probeResult.IsSuccess || probeResult.Value is null)
        {
            return ServiceResult<RequirementTraceabilityReportDto>.Fail(
                probeResult.Failure ?? new ServiceFailure(BridgeErrorCodes.BackupIntegrityFailed, "تعذر فحص مخطط قاعدة البيانات."));
        }

        var snapshot = probeResult.Value;
        bool dbHealthy = snapshot.EncryptionProviderActive
            && snapshot.IntegrityCheckPassed
            && snapshot.CurrentSchemaVersion == 1
            && snapshot.MissingTables.Count == 0
            && snapshot.MissingTriggers.Count == 0
            && snapshot.HasProtectedUnspecifiedSite
            && snapshot.SeededDefaultStatusesCount >= 4
            && snapshot.SeededSystemColumnsCount >= 4
            && snapshot.SeededAppSettingsCount >= 6;

        var combinedReport = staticResult.Value with
        {
            IsCompleteAndConsistent = staticResult.Value.IsCompleteAndConsistent && dbHealthy,
            DatabaseSnapshot = snapshot
        };

        if (!dbHealthy)
        {
            return ServiceResult<RequirementTraceabilityReportDto>.Fail(
                BridgeErrorCodes.BackupIntegrityFailed,
                "فشل التحقق من مطابقة مخطط قاعدة البيانات المشفرة أو بيانات التأسيس الثابتة.");
        }

        if (recordAuditEvent)
        {
            var effectiveActorId = actorUserId ?? UserId.SystemActor;
            var effectiveUsername = string.IsNullOrWhiteSpace(actorUsernameSnapshot) ? "SYSTEM" : actorUsernameSnapshot;
            var effectiveCorrelation = string.IsNullOrWhiteSpace(correlationId) ? $"TRACE-{Guid.NewGuid():N}" : correlationId;

            var auditResult = await _schemaProbe.RecordSchemaVerificationAuditAsync(
                effectiveActorId,
                actorPersonId,
                effectiveUsername,
                actorRosterFullNameSnapshot,
                effectiveCorrelation,
                cancellationToken).ConfigureAwait(false);

            if (!auditResult.IsSuccess)
            {
                return ServiceResult<RequirementTraceabilityReportDto>.Fail(
                    auditResult.Failure ?? new ServiceFailure(BridgeErrorCodes.StorageBusy, "تعذر تسجيل قيد التدقيق لفحص المطابقة."));
            }
        }

        return ServiceResult<RequirementTraceabilityReportDto>.Ok(combinedReport);
    }
}
