MASTER ENGINEERING PROMPT
COMPLETE SELF-CONTAINED FLUTTER/DART ANDROID APPLICATION SPECIFICATION
FULL FEATURES + EXACT DOCUMENTED UI DESIGN + SUPREME ADMINISTRATION
Revision: Self-contained, TEXT-ONLY implementation contract + mandatory importable Supabase SQL database installation package — no additional INPUT files required.

====================================================================
00. MISSION — THE ONE AND ONLY INPUT IS THIS TEXT DOCUMENT
====================================================================

Act as a Principal Flutter/Dart Engineer, Android Software Architect, Supabase/PostgreSQL Architect, Security Engineer, Arabic RTL UI Designer, Offline Synchronization Specialist, and Lead QA Engineer.

IMPORTANT INPUT RESTRICTION: THE ONLY FILE THAT THE USER WILL PROVIDE IS THIS TEXT DOCUMENT. THERE ARE NO PROJECT ARCHIVES, NO KOTLIN SOURCES, NO SAMPLE APK, NO SCREENSHOTS, NO SQL BACKUPS, AND NO DESIGN FILES TO UPLOAD. DO NOT ASK FOR THEM, WAIT FOR THEM, OR CONDITION DEVELOPMENT ON THEM. Do not tell the user to upload an old project. Create all source code, import-ready database SQL files (including short mobile SQL Editor batches), database migrations, server functions, and UI assets needed by the specification from scratch.

PRODUCT TO BUILD: A real, runnable Arabic-first Android application named "إدارة التمام اليومي" (working technical project identifier: manger), using Flutter/Dart, Supabase Authentication/PostgreSQL/RLS/Edge Functions, and offline SQLite caching. Implement all functions, screens, roles, interfaces, operations, constraints, reporting, import/export, security and synchronization described in THIS document. MANDATORY: first generate the COMPLETE IMPORTABLE SUPABASE DATABASE SQL PACKAGE described in Section 18A; it is the foundation for the entire application. The application is a personnel/site/daily-attendance/monthly-roster management system with the roles SUPER_ADMIN, ADMIN, SUPERVISOR.

THIS DOCUMENT IS THE COMPLETE AUTHORITATIVE PRODUCT REQUIREMENTS AND ENGINEERING CONTRACT. Sections below specify appearance, navigation, database structure, rules, status colors, attendance behavior, account provisioning, reports, permissions, deletion, backup, and project phases. Follow them directly; do not refer to an unavailable application/source as a prerequisite.

SOURCE OF TRUTH ORDER:
1. Explicit functional and security requirements in this single document.
2. UI palette, screen layouts and navigation blueprint described within this same document.
3. Database schema/contract and state transitions specified here.
4. Minimal documented engineering decisions only for genuinely unspecified low-level details.

WHAT "SAME APPLICATION" MEANS IN THIS TEXT-ONLY WORKFLOW: Implement every functional and visual requirement documented here as a coherent product. The known palette, typography, Arabic labels, navigation, statuses and business workflows are mandatory. Exact pixel-for-pixel reproduction of unavailable screenshots is impossible; do not falsely claim such a comparison was performed. Design unpictured details conservatively, consistently with the included design-system blueprint, without turning this into a different application.

BACKEND STATUS: The user has not supplied a Supabase export or live database access. Define a complete, NEW, deployable Supabase schema in numbered migration SQL and server-side functions. Do NOT assert the existence of historical database tables on some already running server. Where familiar `manger_*` names are listed below, treat them as NAMING CONTRACTS FOR THE NEW PROJECT, not proof of an already deployed schema.

CREDENTIALS: Configure Supabase URL and publishable/anon key using build-time environment configuration or user-editable local configuration placeholders. Do not hardcode real credentials or require the user to give service-role secrets to the Android app. Backend bootstrap credentials must be managed securely outside the Flutter client.

IMPLEMENTATION INTENT: Actually generate the complete Flutter/Dart application and Supabase SQL/Edge Functions, not a plan, pseudocode, placeholder UI or analysis-only response. Proceed phase by phase, with testable artifacts, and document genuine environment limitations instead of claiming unrun builds.

====================================================================
01. NON-NEGOTIABLE PROJECT INVARIANTS
====================================================================

1. Android client written using Flutter and Dart with sound null safety.
2. Exact Arabic-first terminology and genuine right-to-left layout.
3. Application display name MUST be "إدارة التمام اليومي".
4. Implement every home screen, layout, workflow, card, icon, font, dialog, tab, filter, and role-specific navigation described in this document and its detailed Section 25 screen blueprints.
5. Implement ADMIN (الإدارة العامة) and SUPERVISOR (مشرف) with all capabilities and restrictions defined in this document; do NOT implement any attendance-proof request.
6. Add SUPER_ADMIN (الإدارة العليا), inheriting ALL ADMIN capabilities and receiving exclusive additional powers.
7. Only ADMIN and SUPER_ADMIN may set, modify, or cancel the LEAVE attendance state for any individual, including supervisors. A supervisor may neither create nor modify nor remove LEAVE through any route, online or offline.
8. A SUPERVISOR can access the application only when their EFFECTIVE, SERVER-VERIFIED current-day attendance is PRESENT (موجود); all other states, including no valid state, deny entry. The effective state may originate from administrative attendance or an explicitly valid automatic attendance rule.
9. The removed request-for-attendance-confirmation workflow must not be accessible through login, notifications, APIs, or background jobs.
10. Personnel name/number edits, archiving, leave, operations logs, and account provisioning must be REAL persisted functions, not UI mockups.
11. Authorized business-data deletion by SUPER_ADMIN must act on the actual server, preserve schema/function/RLS integrity, and prevent deleted records from reappearing through offline sync.
12. A supervisor's operations-log visibility is LIMITED to (a) operations personally performed by that supervisor, and (b) operations performed by ADMIN or SUPER_ADMIN that pertain to that supervisor's currently authorized site(s). They must NOT see peer supervisors' operations merely because they share a site.
13. ADMIN must be able to read operations performed by ALL administrators and ALL supervisors. Only SUPER_ADMIN can delete removable operational logs.
14. No service-role key, server secret, production password, or developer signing private key inside the distributed APK or source control.
15. Never use silent fallback mock data in production.
16. Never report tests/builds as passing without actually running them.
17. No redundant historical code, speculative features, unnecessary dependencies, or unused assets.
18. All changes must preserve schema integrity, data isolation, role authorization, and correct historical reporting.

====================================================================
02. SELF-CONTAINED REQUIREMENTS INVENTORY AND TRACEABILITY
====================================================================

NO SOURCE ARCHIVE EXISTS IN THE IMPLEMENTATION ENVIRONMENT. DO NOT PERFORM SOURCE REVERSE-ENGINEERING AND DO NOT REQUIRE A ZIP FILE. Instead, use THIS TEXT FILE as the sole functional specification and create an exhaustive requirement-to-code implementation map before coding.

REQUIREMENTS TO INVENTORY:
A. Product identity: "إدارة التمام اليومي", Arabic UI, RTL, Tajawal typography, specified light/dark palettes, Android.
B. Login by username + password only, internal `username@adwas.com` auth identity, managed provisioning, device-bound supervisors.
C. Three roles: الإدارة العليا/SUPER_ADMIN, الإدارة العامة/ADMIN, مشرف/SUPERVISOR.
D. Home dashboard: role-specific data, date, attendance status counters, people listing, search/filter, sync state, notifications.
E. Daily attendance: individual/batch changes, date range, site scope, status codes, source of automatic PRESENT, admin-managed LEAVE, authorized windows.
F. Monthly rosters: day columns, custom columns, month calculations, status symbols/colors, export and monthly archiving.
G. Personnel/sites: creation, editing names/"الرقم (ع)", active/archived states, archive reason, restoration, internal transfer, custom fields, sites and غير محدد.
H. أسماء القوة: dedicated ADMIN personnel name/number editing and archiving screen.
I. الإجازات: dedicated ADMIN-only LEAVE status management screen for persons AND linked supervisors.
J. أرشيف التمام: preserved monthly historical snapshots, site/person archive/restore, filters, reports.
K. تقارير: individual/date-range/monthly records and PDF/XLSX reports with correct Arabic.
L. Import/export: XLSX/CSV, staging, previews, validation, atomic commit, batch progress.
M. Supervisor account management: shared admin/supreme provision, person/site link, device binding/reset, password changes, deactivation.
N. ADMIN account creation ONLY by SUPER_ADMIN; bootstrap of first SUPER_ADMIN outside APK.
O. One-way administrator-to-supervisor messaging/inbox/unread state and access filters.
P. سجل العمليات: real server-side audit business log with precise actor/target/time/action and role-scoped visibility.
Q. Supervisor PRESENT-only login based on authoritative Asia/Aden effective attendance; no attendance-proof request anywhere.
R. SUPER_ADMIN dashboard, privileged deletion engine, account admin, integrity/backup, archived and operational record deletion.
S. SQLite cache/outbox, paginated delta sync, deletion tombstones, conflict handling, background retries.
T. Settings, appearance, backups, restricted server restore, notifications, role-aware navigation.
U. Supabase PostgreSQL migrations, RLS, RPC/Edge Functions, security tests, Flutter tests, Android build.

CREATE THE FOLLOWING DOCUMENTS AS PART OF THE OUTPUT PROJECT, DERIVED ENTIRELY FROM THIS TEXT:
SPEC_REQUIREMENT_INVENTORY.md
SPEC_SCREEN_BLUEPRINT.md
SPEC_FEATURE_MATRIX.md
SPEC_PERMISSIONS_MATRIX.md
SPEC_DATABASE_SCHEMA.md
SPEC_RPC_CONTRACTS.md
SPEC_BUSINESS_RULES.md
SPEC_UI_DESIGN_TOKENS.md
SPEC_NAVIGATION_MAP.md
SPEC_GAPS_AND_DECISIONS.md
SPEC_TO_FLUTTER_TRACEABILITY.md

For every requirement record: requirement ID, responsible role, screen/action, data entities, validation rules, local cache operations, server operations, Flutter paths, automated test IDs, and verified implementation status. Use a stable ID such as ATT-001, AUTH-002, ADM-003, DEL-004. No feature can be marked complete while its button is nonfunctional or its server behavior is missing.

MANDATORY COMPLETE MODULES:
- "أسماء القوة": EDIT personnel names/numbers and ARCHIVE; do not leave it a placeholder.
- "الإجازات": ADMIN/SUPER_ADMIN control LEAVE for people/supervisors; supervisor cannot.
- "سجل العمليات": actually write, retrieve and filter operations; implement role-scoped privacy.
- "طلب إثبات حضور": DOES NOT EXIST and must not be added; replace it entirely with supervisor authorization checking.
- "الإدارة العليا": full inherited ADMIN rights plus role/account controls and safe hard deletion.

All documents must describe a PRODUCT THAT CAN BE IMPLEMENTED WITH THIS TEXT ALONE, not external sources.

====================================================================
03. TECHNOLOGY, BUILD, AND CODE ORGANIZATION
====================================================================

Use a stable, mutually compatible Flutter SDK, Dart SDK, Android Gradle Plugin, Gradle, and Kotlin plugin (only for unavoidable Android platform integration), and Android SDK versions. Pin dependencies with a lockfile and document actual versions.

Recommended stack:
- Flutter + Dart, Material 3, RTL.
- One consistent app-state solution: Riverpod OR BLoC.
- go_router or equivalently testable typed navigation.
- supabase_flutter, with strictly bounded trusted server methods.
- Drift/SQLite or equivalently typed, migratable local persistence.
- Secure client token/session storage using Android Keystore-backed mechanisms where supported.
- Android-compatible background task scheduling (acknowledging system restrictions).
- Platform file picker/saver; XLSX, CSV, and PDF packages actually tested with Arabic.
- Feature-oriented Clean Architecture: presentation, application/use cases, domain, data/adapters.

Recommended code layout:
lib/app/
lib/core/{constants,errors,auth,security,database,network,sync,theme,widgets,utils}/
lib/features/{authentication,dashboard,personnel,sites,attendance,leaves,monthly,archives,reports,supervisors,messaging,import_export,transfers,settings,operations_log,supreme_administration,backup_restore}/
assets/{fonts,icons,images}/
supabase/{migrations,functions,tests}/
test/
integration_test/
tool/

Avoid HTTP requests and raw SQL inside Flutter widgets. Put validation both in domain/use cases and authoritative backend rules. Use immutable stable entity IDs, controlled error types, sensible loading and cancellation states, and disposable listeners. Write idiomatic, maintainable Dart without dependence on external source code.

====================================================================
04. MANDATORY UI, ARABIC DESIGN, AND NAVIGATION
====================================================================

Use the complete UI token and screen specifications written in Sections 04 and 25 rather than generic Flutter defaults. Implement consistent navigation, card padding, elevations, typography, shapes, text overflow, status chips, input styles, dialogs and icons. Use legitimately obtained Tajawal fonts (Regular, Medium, Bold, ExtraBold) in the generated Flutter project; do not depend on a user-provided font file. Handle font weights accurately.

MANDATORY light-theme tokens:
Primary #195B82; OnPrimary #FFFFFF; PrimaryContainer #DCEFFB; OnPrimaryContainer #12374F;
Secondary #087E71; SecondaryContainer #D7F4EA; Tertiary #916322; TertiaryContainer #FFEDD2;
Error #B53E4D; Background #F5F7FB; OnBackground #192B3D; Surface #FFFFFF;
SurfaceVariant #EAF0F6; Outline #91A3B5; OutlineVariant #DDE5ED.

MANDATORY dark-theme tokens:
Primary #A7D7F9; OnPrimary #08324A; PrimaryContainer #234966; OnPrimaryContainer #D5ECFB;
Secondary #8EDCCB; SecondaryContainer #1D524A; Tertiary #EBC58A; TertiaryContainer #624821;
Error #FFAAB4; Background #0F1927; OnBackground #E7EFF8; Surface #182638;
SurfaceVariant #26384C; Outline #8196A9; OutlineVariant #33485D.

Attendance colors:
PRESENT / موجود: #21885A.
ABSENT / غياب: #CA4350.
LEAVE / إجازة: #3477CB.
PERMISSION / إذن: #BA8317.

MANDATORY shape radii (logical dp): extra-small 8, small 12, medium 16, large 22, extra-large 28.

RTL means actual layout-order conversion, not just right-aligned text. Preserve correct bidi presentation for YYYY-MM-DD dates, personnel identifiers, usernames, Latin filenames, and numerals. Ensure contrast in both themes. Do not change existing screen titles arbitrarily.

ADMIN bottom navigation MUST contain these five destinations, in RTL ordering: الرئيسية، التمام الشهري، أرشيف التمام، الإدارة، الإعدادات.
SUPERVISOR bottom navigation MUST contain: الرئيسية، التمام الشهري، أرشيف التمام، التقارير، الإعدادات.
SUPER_ADMIN inherits ADMIN navigation and receives a coherently integrated "الإدارة العليا" management entry; do not unnecessarily reorder the specified five destinations.

Screens MUST implement login, dashboard, quick/multi-person attendance, monthly view, archives, site/personnel management, supervisors, messaging, reporting, exports, settings, import, and the other explicitly specified modules, including "أسماء القوة", "الإجازات", "سجل العمليات", and "الإدارة العليا".

Golden/screenshot tests: capture and compare the newly generated UI against the normative screen blueprints, palette and component dimensions in this document across supported device sizes, system insets, light/dark modes and RTL. No historical app screenshots are supplied; do not claim comparison to unseen images or byte-for-byte visual parity.

====================================================================
05. ACCOUNTS AND THREE-ROLE AUTHORIZATION MODEL
====================================================================

Exactly three main roles:
SUPER_ADMIN = الإدارة العليا.
ADMIN = الإدارة العامة.
SUPERVISOR = مشرف.

SUPER_ADMIN inherits 100% of ADMIN's permissions and gains exclusive authority over privileged deletion, administrator account provisioning, role elevation, and other specified high-risk system management.

ADMIN performs all general administration functions specified here: adding/managing supervisors, personnel/sites, attendance/leave, reporting, archives, and administrative notifications. ADMIN cannot create SUPER_ADMIN, cannot elevate its own role, cannot purge operations logs, and cannot perform Supreme-only hard deletions.

SUPERVISOR retains site-bound legitimate workflows only, cannot administer roles, cannot adjust LEAVE, cannot see other supervisors' operation histories, cannot send administrative messages, and cannot permanently delete authoritative server records.

Centralize permissions in a clear permission catalog, e.g.:
view_all_sites, view_assigned_site, edit_personnel_identity, archive_personnel, restore_personnel,
manage_site, manage_attendance, set_leave, cancel_leave, configure_attendance_window,
manage_status_codes, manage_columns, manage_supervisors, create_administrators,
view_operations_all, view_operations_site_filtered, purge_operations,
manage_reports, export_reports, manage_imports, send_admin_messages,
view_supreme_dashboard, delete_business_data_permanently,
restore_deleted_data, manage_data_retention, manage_role_assignments.

The final matrix must cover every feature defined in this document: a permission's name is not evidence of implementation. Enforce in BOTH Flutter and the database/RPC authorization layer; never trust an unverified client-supplied role. SUPERVISOR's own-attendance gate is an additional precondition on top of role and site authorization.

====================================================================
06. USERNAME/PASSWORD AUTHENTICATION AND ACCOUNT PROVISIONING
====================================================================

Login UI displays ONLY "اسم المستخدم" and "كلمة المرور" as credentials; do not request email. Internally map the normalized username to the authentication identity username@adwas.com. Users must not need to enter, receive, or verify an email address. A trusted server-side provisioning process may create and confirm the internal auth email identity; no verification email or user activation link should be required for the intended managed-account workflow.

Mandatory username rules: lowercase first Latin letter [a-z], then lowercase Latin letters/digits/underscore [a-z0-9_], total length 3-32; normalize case and reject collisions. Mandatory managed-account password rules: 6-72 characters, at least three digits, at least one nonalphanumeric symbol, no whitespace. Apply server-side validation equally to every newly created account type where the rule is intended to be common, without weakening role-specific validation.

ACCOUNT PROVISIONING REQUIREMENTS:
1. ADMIN may create and manage SUPERVISOR accounts with the same existing conditions and workflow.
2. SUPER_ADMIN may create and manage SUPERVISOR accounts USING THE VERY SAME validation, uniqueness checks, Supabase authentication procedure, required person/site link, device policies, and audit behavior used when ADMIN creates a supervisor. Do not build an inconsistent duplicate path.
3. SUPER_ADMIN may ALSO create and manage ADMIN accounts. Reuse the common username/password normalization, uniqueness, enabled/disabled status, secure auth creation, rollback-on-failure, and auditing conditions applicable to managed accounts. ADMIN-specific person/site links must follow the true ADMIN role contract; do not wrongly force a SUPERVISOR-only site binding on administrators.
4. Only SUPER_ADMIN can create/elevate ADMIN roles or change sensitive administrative account permissions. ADMIN can never promote SUPERVISOR to ADMIN or itself to SUPER_ADMIN.
5. The initial SUPER_ADMIN must be securely provisioned via a trusted one-time backend bootstrap using secret injection or an administrative procedure. Do not embed credentials in code, logs, build files, or the APK.
6. At least one usable SUPER_ADMIN must always remain; reject actions that disable/delete/demote the last active Supreme Administrator.
7. Securely disable, reset credentials, and revoke sessions/devices when roles or account validity change.
8. Account management is distinct from personnel records: removing an auth account must never silently remove attendance/personnel history.
9. A shared server-side provisioning service should accept a strictly validated role allowlist and enforce the caller's role in the function itself.
10. Avoid partial accounts: create Auth and profile consistently with cleanup/compensation on failure. Verify final role, identity and enabled state before reporting success.
11. Use salted nonreversible password verification through the established authentication provider. Protect login from rapid guessing; never log passwords or tokens.

Implement secure supervisor-device binding, with device proof stored securely, trusted server claim/verification, authorized reset, and rejection of other unauthorized installations. Treat mobile identifiers cautiously: do not base security solely on spoofable Android identifiers.

====================================================================
07. SUPERVISOR LOGIN RULE — PRESENT-ONLY, NO ATTENDANCE REQUEST
====================================================================

THIS SECTION DEFINES THE ONLY VALID SUPERVISOR LOGIN AUTHORIZATION. NO ATTENDANCE-PROOF REQUEST SHALL BE CREATED.

Completely remove:
- "طلب إثبات حضور" and "إرسال طلب إثبات حضور للإدارة" UI buttons/text.
- Any requestDutyConfirmation implementation, attendance-request queue/message, request-specific endpoint, prompt, and related unused code.
- Any fallback allowing a supervisor to request access or mark their own presence for login purposes.

AUTHORITATIVE RULE:
A SUPERVISOR may enter and use the application ONLY when all are true:
- Authentication succeeds.
- Account is enabled, valid, and still has SUPERVISOR role.
- Supervisor is linked to the correct active personnel record and authorized site.
- Device binding passes where enabled.
- Today's EFFECTIVE attendance status, determined using the authoritative SERVER'S Asia/Aden date and THIS DOCUMENT'S attendance rules, is exactly PRESENT (موجود).

ALLOWED ATTENDANCE ORIGINS:
A. Explicitly recorded/approved by ADMIN or SUPER_ADMIN.
B. An enabled server-side AUTOMATIC attendance determination, where the person is active and on duty, the date is eligible, no explicit higher-priority status exists, and the configured rule yields PRESENT. See Section 26 for precedence.

IMPORTANT DISTINCTION: A missing explicit attendance row MUST NOT automatically mean PRESENT unless the server-enforced `automatic_presence_enabled` setting and the documented automatic attendance policy explicitly defines that effective state. Document the exact precedence between manual ADMIN marks and automatic results. An explicit ABSENT/LEAVE/PERMISSION must never be overridden by an automatic PRESENT fallback. An archived, inactive, or unlinked supervisor must not pass merely because of a generic default.

DENY SUPERVISOR ENTRY IF EFFECTIVE STATE IS:
- ABSENT / غياب.
- LEAVE / إجازة.
- PERMISSION / إذن.
- Any other custom status that does not explicitly resolve to the canonical PRESENT code.
- Missing, unknown, unverified, stale, or unavailable state.
- Inactive or unlinked personnel.

ON DENIAL:
- Do not route to protected dashboard or cache-based protected screens.
- Show a concise Arabic explanation stating the current status where safe.
- Do not show an attendance request button or any self-approval option.
- Advise that the administration must update attendance, or that an authorized automatic status update must occur.
- Do not leak unrelated user/site data.

SESSION CONTINUITY:
- Enforce the attendance gate on initial sign-in, restored sessions, app foreground return, date rollover, and prior to every sensitive supervisor RPC/write.
- If a supervisor's effective status changes from PRESENT to another state mid-session, immediately withdraw protected access on the next server-backed check or pushed invalidation, clear sensitive in-memory states, and deny writes. Do not rely on an outdated cached green status.
- If offline and server verification is required but cannot be completed, deny new privileged access; do not fabricate PRESENT. Respect explicit secure offline policy only if it can be proven without bypassing authorization.
- ADMIN and SUPER_ADMIN login is not automatically subject to SUPERVISOR duty gating unless the specified role policy explicitly requires it.

Server-side checks are mandatory. Changing a Flutter boolean or editing a local SQLite mark must never bypass this rule.

====================================================================
08. MODULE "أسماء القوة" — PERSONNEL NAMES, NUMBERS, AND ARCHIVING
====================================================================

THIS IS AN ADMINISTRATIVE SCREEN, NOT A SIMPLE READ-ONLY DIRECTORY.

Arabic screen title: "أسماء القوة".
Authorized editors: ADMIN and SUPER_ADMIN. SUPERVISOR has no access to this administrative editing screen unless the this applicationlication specifically provides a separate read-only roster view.

The screen must allow authorized administration to:
1. List individuals in stable deterministic ordering (by site then roster sequence / personnel number), with site, full name, and "الرقم (ع)".
2. Search by name or exact/partial personnel number, retaining leading zeros.
3. Filter by site and active/archived status.
4. Open an individual and edit the individual's name.
5. Edit the individual's personnel number "الرقم (ع)".
6. Validate required fields and prevent forbidden duplicate numbers according to authoritative business rules.
7. Save edits to authoritative server data and synchronize the correct local caches.
8. ARCHIVE a selected individual directly from this screen.
9. Before archiving, display an appropriate confirmation, effective date (when supported by the defined personnel lifecycle), and the reason field required by the specified archive workflow.
10. Use the archive business logic specified in this document so the individual moves out of active rosters without erasing historical monthly attendance or invalidating archived snapshots.
11. Display successful archive state and allow authorized restoration through the archive workflows defined here.
12. Preserve historical attribution and operation records (old name/number and new name/number where permitted), including actor and timestamp.
13. Allow defined ADMIN capabilities and equivalent SUPER_ADMIN capabilities without duplicated codepaths.

SPECIAL SAFETY REQUIREMENTS:
- Personnel identities use immutable person IDs; do NOT use mutable name or "الرقم (ع)" as relational primary key.
- Store "الرقم (ع)" as TEXT, not integer; protect leading zeros and Excel round trips.
- A name/number edit must update all current dependent displays without corrupting fixed historical monthly snapshots. Where history uses immutable snapshots, retain historical labels there deliberately; record that policy.
- Editing the number of a supervisor-linked person must not orphan the linked authentication account: links must use immutable person ID.
- Archiving a person who is an active supervisor must trigger the defined duty/account policy (typically deny supervisor protected access), without accidentally deleting the account or data.
- Distinguish "أرشفة" from "حذف نهائي". Ordinary ADMIN may archive according to this specification but cannot invoke new Supreme-only permanent deletion.
- Include single-record editing progress, server conflict/revision handling, field-level Arabic validation, unsaved-change protection, and duplicate-error handling.

SUPREME ADDITION: This module may expose a clearly separated "حذف نهائي" control ONLY to SUPER_ADMIN. Route it to the centralized protected deletion engine, never implement raw table DELETE from Flutter.

====================================================================
09. MODULE "الإجازات" — ADMIN-ONLY LEAVE STATE FOR EVERY INDIVIDUAL
====================================================================

Arabic screen title: "الإجازات".

Its core purpose is to SET, CHANGE, AND CANCEL attendance status LEAVE (إجازة) for individuals AND supervisors. Only administration may perform these actions.

Allowed to set/change/cancel LEAVE:
- ADMIN: YES.
- SUPER_ADMIN: YES, inheriting all ADMIN functionality.
- SUPERVISOR: NO, even for the supervisor's own attendance, even if the user can edit other attendance statuses at their site.

The screen must implement:
1. Search and selection of one or multiple individuals, including supervisor-linked individuals.
2. Site filtering according to administrative scope.
3. Selection of a single date or date range (where permitted by the defined attendance rules).
4. Setting LEAVE across permitted dates with full validation.
5. Modifying an existing leave selection/date range or cancelling a leave mark through authorized administrative actions.
6. Showing existing relevant marks and explaining what changes will be made before overwriting.
7. Saving as actual attendance events/records on Supabase, using authorized transactions or established RPCs, not a separate decorative leave status unrelated to attendance.
8. Updating current-day dashboard counts, monthly rosters, reports, archives where applicable, and synchronization events.
9. Requiring a confirmation for bulk changes or when replacing a status already recorded by another user.
10. Showing actor, subject, target date(s), prior status, new status, and timestamp in the operations log.
11. Handling past/future dates only as permitted by the defined employment and archive rules.
12. Supporting error recovery, revision conflicts, retries, and large-range batch limits.

STATES AND PERMISSION PRECEDENCE:
- PRESENT / موجود.
- ABSENT / غياب.
- LEAVE / إجازة.
- PERMISSION / إذن.
- Preserve specified custom statuses where permitted.

SUPERVISOR must be forbidden at BOTH server and client layers to:
- Apply LEAVE.
- Replace another state with LEAVE.
- Remove or convert an existing LEAVE to PRESENT/ABSENT/PERMISSION.
- Delete an approved LEAVE row through batch attendance, imports, offline writes, or alternate RPCs.
- Bypass the restriction by manipulating a request body.

If ADMIN changes a SUPERVISOR's current-day status to LEAVE, that supervisor must immediately lose protected login access and write authorization under Section 07. The same applies when state becomes ABSENT, PERMISSION, or another non-PRESENT state. When the administration later changes it to valid PRESENT, access may be regained after fresh authoritative revalidation. Do NOT give the supervisor any right to approve their own return.

Do not add a complex multi-stage leave approval, HR payroll calculation, or unrelated leave entitlement subsystem unless present in the requirements defined here or needed to implement the explicitly requested attendance-state change. The central requirement is ADMIN-OWNED LEAVE attendance control integrated with the specified attendance engine.

====================================================================
10. MODULE "سجل العمليات" — COMPLETE ACTOR/TARGET/TIME/ACTION HISTORY
====================================================================

Arabic screen title: "سجل العمليات".

This must be a REAL, PERSISTENT, SERVER-BASED operations register across all permitted activities by ADMIN, SUPER_ADMIN, and SUPERVISOR.

MINIMUM MANDATORY FIELDS OF EACH EVENT:
1. "نوع العملية" — what was done, using an action code plus clear Arabic label.
2. "على من تمت" — individual/person/site/account/record affected, identified by stable ID and a safe readable snapshot.
3. "متى تمت" — authoritative timestamp, rendered in the intended local timezone and readable format.
4. "من قام بها" — authenticated account/actor, including display name and role when authorized.

Additional recommended fields: event ID, site ID(s), module/entity, before/after summary with sensitive-data redaction, outcome (success/rejected where appropriate), correlation/request ID, device/session metadata where justified, source (automatic vs ADMIN vs SUPERVISOR), and server time.

Events to cover at minimum:
- Login/authorization events where appropriate and safe.
- Personnel name/number changes.
- Personnel and site creation/editing.
- Personnel archiving and restoration.
- Attendance mark creation/modification/cancellation.
- Administrative LEAVE changes.
- Internal transfers and site reassignment.
- Custom column and status-code administration.
- Supervisor/admin account provisioning, disable/enable, device reset, role changes.
- Roster imports and committed batch operations.
- Archive and monthly record operations.
- Message sending and controlled removal.
- Privileged Supreme deletions and backups/restores.
- Relevant automatic attendance/system activities with a distinct SYSTEM actor (not falsely attributed to a person).

VISIBILITY RULES — NON-NEGOTIABLE:
SUPER_ADMIN: Can view all relevant operations by all administrators, all supervisors, and the system. Can delete eligible visible business-log entries according to the special protected process, without erasing mandatory protected security metadata.
ADMIN: Can view ALL operations by ALL administrators (including Supreme Administrators where appropriate) AND ALL supervisors, across the entire organization. Can search/filter/export where existing policies allow. CANNOT delete the operational log.
SUPERVISOR: Can view ONLY:
  A. Operations personally performed by THAT SAME supervisor, subject to valid session and site security.
  B. Operations performed by ADMIN or SUPER_ADMIN that are directly related to the supervisor's authorized site. A site-related operation must have authoritative site association, not name-text matching.
  The supervisor must NOT see a different supervisor's operations merely because both work at the same site.
  The supervisor must NOT see administrative actions that are unrelated to the authorized site.
  The supervisor must NOT see global administrative/account security events without a relevant authorized site and allowed payload scope.
  The supervisor must NOT delete any operation log.

If an ADMIN operation affects multiple sites, expose only the site-relevant portion or an appropriately redacted site-scoped summary. Do not disclose unrelated target names, counts, or payload to a SUPERVISOR. If a supervisor is reassigned to a new site, use a clearly documented, server-enforced visibility rule: current authorized site(s) by default unless specified requirements demand a historic-site authorization policy. Do not allow a cached previous-site token to leak old records.

OPERATIONS LOG SCREEN MUST INCLUDE:
- Chronological list, newest first.
- Actor and actor role.
- Action type.
- Affected person/site/entity.
- Localized date/time.
- Search, date-range filter, action filter, actor filter, and site filter where relevant to role.
- Event detail panel with redacted before/after fields when permitted.
- Server pagination and stable ordering to support large volumes.
- Explicit empty/loading/error/retry states.
- Authorized export of visible operations if consistent with the defined permission matrix.

EVENT CORRECTNESS:
- Prefer append-only server events written in the SAME transactional path as the business mutation where practicable.
- Never trust a client to self-identify another actor; server derives authenticated actor.
- Write an event only when the described business operation actually succeeds, or clearly mark rejected attempts as separate security events.
- Ensure retries/idempotency do not generate misleading duplicates.
- For batch attendance/import, provide a meaningful batch event and a way to inspect affected records without exposing other sites.

SUPREME-ONLY LOG DELETION:
- SUPER_ADMIN may delete selected user-facing business-log records, or selected date ranges/categories, subject to confirmation, retention, and dependency rules.
- ADMIN and SUPERVISOR have no log deletion capability in UI, API, or RLS.
- Distinguish removable business operations history from a separate, minimally scoped protected security-audit journal. A Supreme purge may remove user-facing operational history but must not permit an undetectable erasure of evidence of privileged deletion, role elevation, or other high-risk operations.
- Keep protected journal data minimal, retention-governed, access-controlled, and nonduplicative; do not make it an infinite hidden copy of all personal data.
- Supabase synchronization and device caches must honor purged-log tombstones.

====================================================================
11. PERSONNEL, SITE, AND INDIVIDUAL IDENTITY ENGINE
====================================================================

Implement personnel records, names, personnel number (ع), active/archived flags, start/end dates, sites, internal identifiers, custom column values, and archived histories. Use immutable UUID/person IDs for relations. Do not join or delete by mutable name. Preserve "الرقم (ع)" as TEXT, with leading zeros and precise uniqueness/normalization policy specified in Section 26.

Replicate site/area lifecycle: create, rename, activate, retire/archive, restore, assign/reassign personnel, linked supervisors, filter rosters, preserve site identity across renames, and maintain the "غير محدد" unassigned-site concept.

Implement personnel import and roster editing, ADMIN-only modifications, mandatory archive reasons, archived-person restoration, retired-site restoration, and internal personnel transfer history.

An archived supervisor-linked person must not retain unrestricted supervisor access; role duty checks must detect personnel inactivity. A site retirement must not create orphan accounts or unassigned historical logs.

====================================================================
12. DAILY ATTENDANCE ENGINE AND DEFINED BUSINESS RULES
====================================================================

Implement the defined attendance business semantics: canonical codes, colors, quick-edit dialog, individual/multiple-person attendance, selected date/range, pending groups, atomic group apply, employment dates, archived-month restrictions, role-based status changes, site filters, cancel before commit, and admin-configurable attendance time windows including midnight crossing.

The canonical status values are PRESENT, ABSENT, LEAVE, PERMISSION, plus administrator-created custom status codes. The status colors are specified in Section 04. Do not treat display text as an authorization key; use stable status codes.

Only ADMIN/SUPER_ADMIN can set or alter LEAVE. SUPERVISOR may perform other as specified authorized attendance changes at assigned site only, within the time window, and only while their own effective state is PRESENT. A supervisor must never mark or alter their own attendance through a route that bypasses duty authorization, and cannot retroactively change an administrator-protected LEAVE through bulk updates.

Automatic PRESENT for an eligible date is an explicit server setting, not a client-side guess. The server computes the effective state according to the precedence policy in Section 26. Build a single authoritative effective-attendance resolver with precedence: explicit valid authorized marks first; automatic rules only when applicable; undefined otherwise. It must be consistent for dashboard, monthly roster, reporting, and SUPERVISOR gate. Never infer tomorrow's PRESENT from today's status, or mark a nonexistent eligible employment date as PRESENT.

Every write should be versioned, checked for scope and date, applied safely, audited, and synchronized. Recalculate affected counts and export/report summaries.

====================================================================
13. MONTHLY ROSTERS, CUSTOM COLUMNS, AND ARCHIVES
====================================================================

Reconstruct "التمام الشهري" in its exact table layout. Support 28/29/30/31-day months and leap years, selection/navigation, scroll behavior, site filters, personnel numbers/names, statuses, symbol/color cells, day columns, summary totals, export, and the consistent row/column ordering defined below.

Preserve required roster columns "الرقم", "الرقم (ع)", "الاسم", "الموقع" plus configurable roster custom fields, visibility/order, uniqueness constraints, value editing, and configurable validated maximum column count. Do not delete required core columns through a custom-column UI.

Reconstruct "أرشيف التمام" with historical monthly snapshots, year/month filters, site filters, archived personnel, archived sites, archive reason/history, archived report export, restoration, and versioned snapshot identity. Historical monthly snapshots must retain historical roster membership and historical meaning: never silently reconstruct a past snapshot from today's current roster.

ADMIN/SUPER_ADMIN have the archive/restore permissions defined in this document. Supreme-only hard deletion of historical artifacts uses the protected deletion engine, with clear differentiation between deleting an exported report file, a generated/materialized monthly snapshot, and underlying actual attendance rows.

====================================================================
14. REPORTS AND FILE OPERATIONS
====================================================================

Implement defined reports: select individuals, site filters, date start/end, person histories, daily/monthly status summaries, missing archived periods, monthly roster/period reports, and the maximum report date range of five calendar years. Preserve arithmetic and calendar semantics exactly. Use backend-enforced site scoping for supervisor reports.

Rebuild real Excel XLSX/CSV import; header/worksheet mapping; roster-number as text; duplicates; selected sheet preview; missing/unknown site behavior; staged server-side import jobs; batch progress; atomic commit and informative errors. Preserve the defined site-resolution workflow and the "غير محدد" fallback when applicable.

Rebuild export for XLSX, PDF, supported CSV formats, selected/all sites, grouped site files, monthly roster, and user-selected destination. Ensure Arabic fonts, RTL tables, numeric text fidelity, large exports, progress, cancellation and error recovery. Use Android-supported file storage APIs instead of assuming unrestricted external storage.

Do not claim an import is complete before authoritative server commit. Avoid partial upsert after failed batch. Tests must cover duplicate personnel, leading zeros, sheet selection, Arabic names, and Excel/CSV round trips.

====================================================================
15. SUPERVISOR ACCOUNTS, DEVICE MANAGEMENT, AND MESSAGES
====================================================================

Implement the following supervisor-management workflow for ADMIN and SUPER_ADMIN: create account, link to personnel record, assign site, enable/disable, reset password via trusted backend function, reset device binding, change site, remove supervisor privileges, and inspect account status. Validate site/person links atomically. SUPREME may additionally create/manage ADMIN accounts using the shared provisioning rules from Section 06.

Messaging must remain ONE-WAY:
- ADMIN and SUPER_ADMIN can send administrative messages/notifications to authorized supervisors.
- SUPERVISOR can read only personally addressed or site-addressed messages allowed by backend policy.
- SUPERVISOR cannot compose, reply, forward as new administrative sender, edit, or globally delete messages.
- If the user hides an inbox message, implement it only as a hide/read-state operation, not deletion of the shared server message.
- SUPER_ADMIN may delete eligible server messages/recipients through protected deletion service.

Support inbox, unread indicator, read/seen state, event time, appropriate recipient targeting, and role/site RLS. Supervisor attendance-gate failure must not create a login-access request message. Such a request workflow is explicitly removed.

====================================================================
16. SUPREME ADMINISTRATION — NEW HIGHER ADMINISTRATIVE LAYER
====================================================================

Create a real "الإدارة العليا" section visible and accessible ONLY to SUPER_ADMIN, designed in the mandatory palette/layout system in Sections 04 and 25. Include functional cards:
- إدارة حسابات المدراء والمشرفين.
- إدارة بيانات الأفراد.
- إدارة المواقع والمناطق.
- إدارة سجلات التحضير.
- إدارة السجلات الشهرية.
- إدارة الأرشيف.
- إدارة سجل العمليات.
- إدارة الإشعارات.
- إدارة البيانات المحذوفة.
- النسخ الاحتياطي والاستعادة.
- إدارة صلاحيات النظام.
- سلامة قاعدة البيانات والمزامنة.

SUPER_ADMIN can:
- Carry out EVERY ADMIN action defined here.
- Add new SUPERVISOR accounts through the exact existing supervisor-create service and constraints.
- Add new ADMIN accounts using the same common validation/provisioning security conditions with administrator-specific role requirements.
- Enable/disable, update and securely reset eligible administrator and supervisor accounts.
- Assign/reassign supported roles, but never allow unprivileged self-promotion.
- View all authorized data and all operations events.
- Permanently delete eligible business records described below, with complete dependency and synchronization safety.
- Delete eligible user-facing operations log entries.
- Handle allowed recovery/review of previously removed records.

SUPER_ADMIN authority is over BUSINESS DATA and ACCOUNT ADMINISTRATION, NOT uncontrolled schema destruction. It must not silently drop tables, constraints, RLS policies, functions, extensions, or migrations.

====================================================================
17. SECURE CENTRALIZED DELETION — SUPREME-ONLY
====================================================================

Create one backend-controlled protected deletion framework. Flutter screens NEVER send generic raw table DELETE SQL. Prefer narrowly scoped PostgreSQL RPC/Edge Functions with explicit allowlisted operation types and validated target IDs.

ALLOW, WHEN AUTHORIZED AND SAFE:
- Permanent deletion of selected personnel or selected groups.
- Permanent deletion of site/area records with a dependency plan.
- Deletion of selected daily attendance rows or date ranges.
- Deletion/reset of selected monthly records and eligible monthly snapshots.
- Deletion of selected archived personnel/site/month records.
- Deletion of archive reason/history business artifacts where permitted.
- Deletion of selected removable operations-log records/ranges/categories.
- Deletion of selected administrative messages and their recipient records.
- Removal of obsolete import staging jobs, eligible export metadata, custom column definitions/values.
- Account deletion/deactivation with proper separation from historical person records.
- Additional business entities actually introduced by the generated data model and validated dependency graph.

Distinguish FOUR separate operations:
A. Archiving/retiring (reversible business state).
B. Soft deletion (server record retained with explicit deleted state).
C. Hard deletion (actual authorized removal of active server rows, with precisely authorized dependent rows).
D. Local cache/data removal (device only).

MANDATORY SERVER DELETION WORKFLOW:
1. Authenticate current caller and verify active SUPER_ADMIN server-side.
2. Check recent login/step-up verification for high-risk actions.
3. Resolve immutable target IDs and current revisions.
4. Enumerate foreign-key/business dependencies and impacted counts; never use display names as deletion keys.
5. Display Arabic impact preview: requested targets, related data, affected personnel/sites/months, what will remain, and recovery availability.
6. Require explicit confirmation; use second explicit confirmation for large irreversible operations.
7. Check concurrent changes, idempotency key, protected system invariants (e.g., last active SUPER_ADMIN).
8. Create a verified recoverable affected-data snapshot where possible under a documented backup policy; do not promise universal reversibility.
9. Execute within a controlled database transaction; rollback fully if referential checks fail.
10. Write protected minimal audit metadata and affected-entity deletion events.
11. Publish reliable deletion tombstones/generation markers and bump server revisions.
12. Verify affected rows, preserve schema/functions/indexes/RLS, and return precise result.
13. Refresh all authorized clients and cached summary/report states.
14. Report successful deletion only after server acknowledgment, and report cache propagation separately.

NEVER use unrestricted TRUNCATE, DROP TABLE, DROP SCHEMA, blanket ON DELETE CASCADE, blanket service-role bypass, or arbitrary client-specified table/SQL name. Each cascade/reassignment must be explicitly reviewed. Never allow removal of another person's unrelated data just because a name/number is similar.

DELETE VS REPORT: Do not confuse deleting a monthly export artifact with deleting underlying attendance or archive snapshots. Do not silently convert an explicitly deleted attendance state into PRESENT due to an auto-default fallback: define deletion/reset semantics and preview the resulting effective attendance.

DELETE VS BACKUP: Hard deletion from active database does not automatically delete old offline/backup copies. Document retention, backup purge policies, and restore tombstone preservation. Restores must not silently resurrect permanently deleted records.

DELETE VS ACCOUNT: Removing an administrator or supervisor login should revoke credentials/role and sessions but must not erase linked personnel attendance/history unless separately authorized and explicitly confirmed.

====================================================================
18. SUPABASE TARGET DATABASE AND SERVER CONTRACT
====================================================================

Build a NEW Supabase PostgreSQL schema and local SQLite cache based on the data contract specified in THIS TEXT. Treat the following names as the TARGET STANDARD, not declarations of tables that already exist on a supplied server.

PRIMARY CLOUD TABLES (design details in Section 26):
- manger_profiles: Auth-linked users, immutable ID, username, role, person/site binding where needed, enabled flag, revision.
- manger_sites: stable site IDs, Arabic labels, active/retired state, metadata.
- manger_employees: immutable persons, personnel number (ع) as TEXT, name, site, dates, archive state.
- manger_attendance: explicit dated attendance records with unique person/day, status code, actor and revision.
- manger_archives: immutable month/site roster snapshots (JSONB or normalized child rows), version and creation metadata.
- manger_statuses: status definitions, visual symbols/colors, active flag and protected canonical codes.
- manger_settings: typed organization settings, attendance windows, automatic attendance policy, timezone.
- manger_column_definitions: roster columns/ordering/visibility/custom field metadata.
- manger_employee_custom_values: values for custom columns.
- manger_employee_archive_events: archive/restore reason and history.
- manger_change_log: monotonic sequence of authorized changes including durable deletion tombstones.
- manger_import_jobs, manger_import_rows: staged batched roster import/commit.
- manger_operations_log: removable user-facing business-operation history.
- manger_security_audit: restricted minimal, retention-governed privileged security events.
- manger_messages, manger_message_recipients: one-way messages and per-recipient read/hidden state.
- manger_supervisor_devices: protected device binding/proof and revocation.
- manger_transfers: personnel site transfer history.
- manger_delete_requests, manger_delete_tombstones: protected destructive operation requests and anti-resurrection guards.
- manger_backup_metadata: verified backup/restore context; do not store unencrypted secrets.

LOCAL SQLITE TABLES (OFFLINE CACHE, NOT THE SERVER AUTHORITY): persons, sites, explicit_marks, monthly_archives, status_codes, custom_columns, custom_values, inbox_events, sync_outbox, sync_cursor, sync_conflicts, sync_receipts, deletion_tombstones, app_meta. Cache data must be isolated by authenticated user/organization and purged safely on logout/role change. Configure local schema versioning starting from 1 for this NEW Flutter implementation; do NOT pretend a historical SQLite v16 database was supplied.

IMPLEMENT APPROPRIATE RPC/EDGE FUNCTION ENDPOINTS (exact signatures in Section 27):
- Auth/admin: manger_provision_account (Edge Function), manger_reset_managed_password (Edge Function), manger_reset_supervisor_device.
- Duty and attendance: manger_resolve_effective_attendance, manger_check_supervisor_access, manger_apply_attendance_event, manger_apply_attendance_batch, manger_apply_leave_batch.
- Personnel/sites: manger_update_personnel_identity, manger_archive_employee_with_reason, manger_restore_archived_employee, manger_activate_or_create_site, manger_retire_site_to_unassigned, manger_restore_retired_site, manger_transfer_employees_batch.
- Monthly/import: manger_create_monthly_snapshot, manger_get_monthly_snapshot, manger_commit_import.
- Messaging/logs: manger_send_supervisor_message, manger_list_messages, manger_hide_message, manger_list_operations.
- Supreme: manger_preview_deletion, manger_execute_deletion, manger_provision_admin, manger_manage_managed_account, manger_restore_eligible_deleted_business_data.
- Sync: manger_get_changes_since, manger_apply_sync_batch.

Each endpoint needs documented request and response fields, transactional behavior, authorization, revision checks, idempotency keys, structured errors, and tests. Where one common server service safely handles multiple actions, prefer one explicit allowlisted path over duplicated implementations. No generic arbitrary table/SQL endpoint is permitted.

DATABASE IMPLEMENTATION RULES:
- UUID keys, proper FK/unique/check constraints and appropriate indexes.
- Store personnel number as TEXT and preserve leading zeros.
- Maintain monotonically ordered change events independent of client timestamps.
- RLS enabled/forced where appropriate; supervisor site isolation must be validated server-side.
- Protected SECURITY DEFINER functions must explicitly verify role, identity, enabled status, site scope, allowed operation, input types, with fixed search_path and least privilege.
- Use trusted Auth identity, never a role string from a Flutter request.
- Do not expose service-role credentials in Flutter.
- Use additive versioned migrations, test on disposable staging database, document bootstrap/setup.
- Support delete tombstones/generation tracking to prevent offline resurrection even if a stale device reconnects after many days.
- Do not erase a system account's linked personnel history merely by deleting/revoking the login.

====================================================================
18A. MANDATORY SUPABASE DATABASE INSTALLATION PACKAGE — DATABASE FIRST
====================================================================

CRITICAL NEW DIRECTIVE: You must DESIGN, IMPLEMENT, OUTPUT, and TEST a COMPLETE SUPABASE DATABASE INSTALLATION PACKAGE, READY TO IMPORT INTO A FRESH SUPABASE PROJECT. This is a real engineering deliverable and the FOUNDATION of the server—not merely a prose database schema, table list, ER diagram, theoretical plan, or disconnected example queries.

INPUT RESTRICTION REMAINS UNCHANGED: The user provides ONLY THIS TEXT DOCUMENT. Never request an original Android project, SQL dump, screenshot, ZIP file, or previous database. Independently create all the actual SQL and required backend function source code from this specification. Do not ask the user to write SQL manually. If your execution environment cannot create downloadable output files, print every generated SQL batch as a COMPLETE independent SQL code block, one numbered batch at a time, without omissions or ellipses; the user must be able to copy/paste it into Supabase SQL Editor from an Android phone. Never replace executable SQL with a description.

WHY DATABASE FIRST: First establish the authoritative PostgreSQL entities, relationships, authorization contracts, and RPC API; then implement Flutter repositories/models against that VERIFIED CONTRACT. Avoid creating an app that later requires guessing about the server schema. Generate this database deliverable directly after the requirements inventory and BEFORE building data-dependent Flutter features. The user may import it into a new Supabase project separately.

A. REQUIRED ACTUAL FILES — GENERATE ALL OF THEM

1. `database/SUPABASE_DATABASE_BOOTSTRAP.sql`: one complete, correctly ordered, executable UTF-8 PostgreSQL SQL script for a NEW/EMPTY Supabase project, assembling all database-side schema, reference data, RLS, grants, triggers, views and PostgreSQL functions. It must contain real SQL statements, not placeholders like 'CREATE THE OTHER TABLES HERE'. It must not contain production credentials, user passwords or secret keys.

2. `database/sql_editor_batches/`: a series of short, separately executable SQL files named in strict execution order, `01_*.sql`, `02_*.sql`, etc. These are the MOBILE-FIRST alternative to the long bootstrap file, intended for copying one batch at a time into Supabase Dashboard > SQL Editor from an Android phone. Aim for no more than roughly 100-150 lines and 8-12 KB per batch; prefer smaller units for complex SQL. NEVER split a CREATE FUNCTION body, dollar-quoted block or other syntactically atomic command across files. Every batch must be independently executable once all preceding batches have succeeded. Do not invent a misleading 'one click' experience on a mobile browser.

3. `supabase/migrations/`: canonical chronological Supabase CLI migration `.sql` files containing the same logical database installation in version-controlled order, so future schema changes have a safe migration history.

4. `database/VERIFY_INSTALLATION.sql`: an executable SQL diagnostic/reporting script that checks the required tables, mandatory columns, constraints, foreign keys, RLS enabled status, critical RLS policy presence, grants, functions, indexes, reference seed data, configured business timezone, and a schema version marker. It should return intelligible PASS/FAIL rows and must not falsely claim end-to-end authorization success based only on existence checks.

5. `database/TEST_DATABASE_ROLES.sql` or `supabase/tests/`: executable role-isolation, permission and server-function tests using a controlled test environment and test identities. Include negative tests proving supervisors cannot call administrator/supreme methods, admins cannot invoke supreme-only deletion, role escalation fails, supervisor cross-site read/write fails, and protected leave restrictions work. Clearly distinguish real test execution from scripts that were only generated.

6. `database/SEED_REFERENCE_DATA.sql`: canonical initial data as part of the reproducible installation. Provide safe INSERT ... ON CONFLICT patterns where appropriate and avoid duplicate seed records. This contains ONLY system reference records, not fake production personnel or preconfigured credential-bearing accounts. Ensure seed content is not double-executed if already included in the bootstrap or migrations: label it as a source component, not a separate mandatory second import.

7. `database/DATABASE_ER.md`: complete schema and relationship documentation. For each table provide purpose, columns and data types, nullability, defaults, primary/foreign keys, unique/index constraints, role-level access, referenced procedures, and deletion policy.

8. `database/API_CONTRACT.md`: exact REST/RPC/Edge Function names, arguments, response structures, error codes, role checks, concurrency rules, and idempotency strategy; contracts must match the actual SQL/TypeScript implementations and Flutter client code.

9. `database/SETUP_SUPABASE_FROM_ANDROID_AR.md`: detailed Arabic mobile-friendly instructions for creating a new Supabase project, selecting SQL Editor, pasting/running ONE numbered batch at a time, checking success before the next, running validation queries, creating the first Auth user safely, provisioning SUPER_ADMIN, deploying required Edge Functions, obtaining project URL and publishable key, and connecting Flutter. Include exact paths and minimal steps. DO NOT ask the user to install a desktop IDE or CLI on a computer.

10. `database/INSTALL_CHECKLIST_AR.md`: a concise Arabic checklist with each batch's prerequisites, expected newly created objects, verification SQL, failure handling, and explicit completion sign-off. Keep installation and production data safety warnings clear.

11. `database/SCHEMA_VERSION.md` plus an actual database schema version/migration tracking mechanism that is compatible with the chosen deployment workflow. Generate reproducible upgrade scripts for future versions, not an ad hoc overwrite of production tables.

12. `supabase/functions/`: complete and deployable Deno TypeScript Edge Function source and deployment instructions for Auth Admin API operations that cannot be achieved merely by importing SQL; include managed account provisioning, credential reset, bootstrap authorization as needed, and proper server-side secret handling. These functions are ADDITIONAL BACKEND DELIVERABLES; do not suggest that a SQL script by itself installs Edge Functions.

B. TWO INSTALLATION PATHS — NEVER MIX ON THE SAME DATABASE

PATH A — MOBILE / SUPABASE SQL EDITOR (PREFERRED FOR THIS USER): The user creates a FRESH Supabase project and sequentially runs only the numbered files in `database/sql_editor_batches/`, verifying the outcome of each. A single `SUPABASE_DATABASE_BOOTSTRAP.sql` file must also be supplied as an optional full import for tools that can safely handle it; it is NOT an extra step after the numbered batches. Exactly ONE of the two SQL Editor installation forms is executed on any one empty project.

PATH B — DEVELOPMENT / SUPABASE CLI: Use the versioned `supabase/migrations/` files, validate them on a disposable local/staging database, and deploy them through the documented Supabase migration workflow. Once CLI migrations control a database, do NOT also edit its schema manually via the Dashboard SQL Editor; doing so can create migration-history drift. Likewise, do not push equivalent migrations after manually importing the bootstrap into the same database unless the baseline has first been explicitly reconciled and verified.

Generate both forms from ONE shared authoritative SQL design, and verify that they produce logically equivalent objects; do not maintain two divergent versions of the schema.

C. COMPLETE BUSINESS SCHEMA — NO MISSING FUNCTIONAL DEPENDENCIES

Build every table needed by the defined Android application's operations, using the cloud table inventory in Section 18 and the full independent data dictionary in Section 26. At minimum the database must structurally support all of the following:

- Three roles and account profiles linked to Supabase Auth (`SUPER_ADMIN`, `ADMIN`, `SUPERVISOR`); enabled/disabled state; immutable user/person/site IDs; securely controlled role changes.
- Sites/areas (including `غير محدد`), personnel identity, personnel number `الرقم (ع)` stored as TEXT, duplicate controls, personnel and site archival history, internal transfers.
- Original and custom attendance states, authoritative day-level marks, ADMIN-only leave updates, attendance windows, effective PRESENT automatic fallback when explicitly enabled, supervisor PRESENT-only access checks using `Asia/Aden`.
- Monthly rosters, immutable monthly archives/snapshots and metadata, historical employee/site membership, custom roster columns and values.
- Personnel roster imports with staging jobs, validation rows, commit state and idempotency; report/export metadata where needed.
- Supervisor device binding and administrative password/account management; one-way messages and recipient-specific read/hidden status.
- Full operations log (action type, target identity, authenticated actor, role, site scope, reliable server timestamp, before/after as permitted), with ADMIN global visibility, SUPERVISOR own+relevant ADMIN/SUPER_ADMIN events only, and SUPER_ADMIN-controlled eligible business-log purging.
- Minimal separate protected security-audit journal, retention constraints, deletion requests, revision checks, tombstones, monotonic change events, synchronization cursors/receipts/idempotency and anti-resurrection controls.
- Server settings, policy configuration, reference attendance status seeds, backup/restore metadata as needed.

For ALL tables define practical datatypes, required columns, timestamp strategy (timestamptz on the server), stable UUID identities, referential integrity, composite uniqueness for person/date attendance, active-state constraints, appropriate indexes, and explicit action-on-delete choices. Do not store passwords, refresh tokens, or service-role keys in application tables. Do not create a duplicate 'leave-only' datastore that is disconnected from authoritative attendance: LEAVE must be reflected through the attendance workflow.

D. PHYSICAL SQL COMPLETENESS AND OBJECT ORDER

Write actual executable statements in dependency order:

1. Required supported extensions / private schemas / version marker, only when necessary.
2. Core role and reference type definitions without tampering with Supabase-managed built-in schemas.
3. Main account, site, personnel, attendance, settings and status tables.
4. Monthly, archive, transfers, custom columns and import tables.
5. Messages, supervisor device bindings, operations logs and security audit tables.
6. Deletion control, durable tombstone, version/event synchronization tables.
7. Primary keys, foreign keys, check constraints, uniqueness constraints, indexes and reviewed relational policies.
8. Required helper functions, carefully scoped triggers and typed views.
9. Strict GRANT/REVOKE rules and RLS policies for all exposed tables and safe views.
10. Authoritative PostgreSQL RPC functions; use trusted caller identity and narrow APIs, not arbitrary dynamic SQL.
11. Idempotent system reference seeds; do not insert real users into the `auth.users` table directly.
12. Post-install verification statements and migration history checks.

Define exact parameter types and return structures for every PostgreSQL RPC in Section 27. If an RPC contract names a procedure, the final database or Edge Function package must actually implement it (not merely mention its name). Cross-check every function and table against Flutter call sites.

E. AUTHENTICATION, ROLE BOOTSTRAP AND SERVER OWNERSHIP

Supabase Authentication users are managed by Supabase Auth, not by manually inserting arbitrary rows into `auth.users` through schema scripts. The database SQL package creates application profile structures, constraints, guards, RLS and restricted linking logic. After SQL installation, the user securely creates the FIRST authentication user through Supabase Dashboard Auth controls or a trusted one-time server admin process; link the real Auth user UUID into a server-maintained profile and assign the initial SUPER_ADMIN role through a privileged ONE-TIME bootstrap path. The bootstrap mechanism must be explicit, tightly restricted, auditable, and disabled once completed. Never hardcode a first SUPER_ADMIN password, fabricate Auth identifiers, expose a publicly callable promotion endpoint, or store a service-role key in Flutter. Username/password is still the user-facing login, with internal `username@adwas.com` mapping as defined in Sections 06/27; no supervisor-facing email input or confirmation workflow.

ADMIN and SUPER_ADMIN account creation must work through proper trusted Auth Admin API operations (e.g. protected Edge Functions using secret credentials stored in Supabase secrets), with database profile consistency and role validation. SQL-only import does not create or deploy those Edge Functions, and the installation instructions must state this clearly.

F. PRODUCTION SECURITY REQUIREMENTS

Enable RLS on all exposed client-facing tables; also audit GRANT/REVOKE permissions, because RLS policies alone do not remove previously granted privileges. Grant only required SELECT/INSERT/UPDATE/DELETE/EXECUTE privileges. Supervisor site isolation must be enforced by SQL, not client filters. Critical mutations must be performed via tightly scoped RPCs/Edge Functions with server authorization and transaction control. SECURITY DEFINER helpers should live in an unexposed/private schema, with fixed empty `search_path`, schema-qualified object references, and narrowly granted EXECUTE access. Do not expose a generic definer function or SQL execution endpoint capable of bypassing the three-tier model. Do not allow users to change their own role through profiles. Protect the final active SUPER_ADMIN from accidental lockout/deletion.

For supervisor operations logs: enforce the exact filter predicate on the server, including the special exclusion of other supervisors' events at the same site. For leave: reject ALL supervisor creation/update/deletion of LEAVE, including bulk/offline/replay endpoints. For access: require the supervisor's authenticated, active, site-linked, server-verified current-day effective PRESENT for privileged entry and server mutations. For supreme deletion: transactional allowlisted operations, dependency previews, durable tombstones, and protected minimal audit data; never DROP/TRUNCATE application schema during operational deletion.

G. IMPORT/EXECUTION SAFETY AND FAILURE RECOVERY

All generated SQL must be syntactically valid for the target Supabase PostgreSQL environment and respect dependency order. Prefer safe, reviewable migration practices. Check `CREATE ... IF NOT EXISTS` or `ON CONFLICT` only where logically safe; do not use IF NOT EXISTS to conceal incompatible table definitions. Treat the bootstrap as intended for a FRESH empty project. Refuse destructive reinitialization on an already-populated project without a separate explicitly authorized migration plan and backup.

If a numbered SQL batch fails in SQL Editor: STOP. Record the error, do not execute later batches, fix that batch's root cause, determine whether earlier statements in that batch committed, and provide a corrected idempotent-or-explicit-recovery version. Do not blindly rerun a partly executed DDL batch or issue reset/drop commands. Make each batch reasonably atomic where supported and document transaction boundaries.

H. REAL VERIFICATION — BEFORE APP INTEGRATION

Validate on a CLEAN TEST Supabase/PostgreSQL environment when tools are available. Required checks:

- All expected tables/columns and relevant foreign keys/indexes exist, and seed defaults exist exactly once.
- `auth` schema remains managed by Supabase and no hardcoded user is inserted into Auth tables.
- RLS is enabled and grants are least-privileged; both positive and negative authorization tests pass.
- SUPERVISOR cannot view another site, manipulate protected leave, send messages or call supreme deletion; role self-elevation fails.
- SUPERVISOR sees only their own operations and site-related ADMIN/SUPER_ADMIN events, never other supervisors' events merely because of site.
- ADMIN can create supervisors but cannot create admins or invoke supreme-only deletion; SUPER_ADMIN can securely provision both.
- Supervisor LOGIN/DUTY gate rejects non-PRESENT and accepts verified PRESENT, with `Asia/Aden` rollover and valid automatic attendance behavior.
- The delete engine preserves references and suppresses offline resurrection via durable tombstones.
- Every API function invoked by Flutter exists with compatible arguments, return types and permissions.
- One short SQL Editor batch sequence creates a logically equivalent database to the canonical CLI migration sequence.

Do not call generated test cases 'passed' unless executed. Where backend access or Postgres tools are unavailable, provide runnable scripts and explicitly label environment-level validation as NOT VERIFIED.

I. REQUIRED DEVELOPMENT AND DELIVERY GATE

NEW MANDATORY STAGE `PHASE 00A — DATABASE INSTALLATION PACKAGE`: Immediately after Phase 00 requirement mapping, produce ALL database artifacts in subsections A–H (especially runnable `.sql` files and short SQL Editor batches), the database dictionary/ERD, role security policy matrix, initialization/bootstrap instructions, and installation checks. Test the SQL as far as the available tooling permits. Report any unverified database constraints. Only then implement the Flutter data/model and server integration against the stable contract. Do not defer writing the real SQL until the final Android release phase.

After generating the package, give a clear Arabic note to the user: "ابدأ من مشروع Supabase جديد، ونفّذ ملفات SQL المرقّمة بالتسلسل، ولا تشغّل الملف الموحّد بعد تشغيل الدُفعات. ثم أنشئ أول حساب Auth للإدارة العليا بالطريقة الآمنة، وانشر Edge Functions، وشغّل فحص التحقق." This note describes steps, not a claim that cloud installation happened automatically.

DEFINITION OF DONE FOR THE DATABASE PACKAGE: Users can create a FRESH Supabase project and install the full database schema using either the sequential mobile SQL Editor batches or the canonical migrations; all required backend SQL objects, role policies, reference seeds, and verification scripts are actually generated and self-consistent. Edge Functions and Auth user bootstrap are separate explicitly documented deployment steps. The completed Flutter app must use this exact generated schema, not another inferred schema.

====================================================================
19. RELIABLE OFFLINE CACHE AND SERVER SYNCHRONIZATION
====================================================================

Implement the following authoritative/offline data flow: local SQLite cache, staging of local changes in outbox, versioned event upload, server acknowledgments, cursor/delta download, conflict detection, refresh and background sync. Make online/offline transitions deterministic.

Implement:
- Full initial sync with pagination for ALL relevant rows; no silent truncation at arbitrary 1,000/5,000/20,000 row caps.
- Role-filtered server reads and local cache isolation.
- Durable outbox records with idempotency keys and acknowledgment status.
- Reliable conflict detection using record revision, identity, and server authority.
- Background sync with a refreshable valid session and Android scheduler restrictions.
- Manual sync and explanatory Arabic status/error feedback.
- Retry with backoff; avoid duplicate imports, attendance marks, audit events.
- Strong handling of logout/user switch, role/site change, device reset, password reset, and disabled accounts.

DELETION-RESURRECTION PREVENTION IS CRITICAL: after SUPER_ADMIN removes a record, old offline devices must NOT recreate it when pending writes reconnect. Use server tombstones/revisions/generation IDs, reject stale mutation replay, invalidate local caches, and test offline devices reconnecting days later. A newly created person reusing a displayed personnel number must have a NEW immutable ID, never be treated as a resurrection of an obsolete ID.

When a supervisor loses PRESENT state, invalidate their access even if cached screens exist. Never allow an offline outbox to upload supervisor attendance actions after duty eligibility was withdrawn; validate eligibility AT SERVER COMMIT TIME.

====================================================================
20. BACKUP, RESTORE, AND APP SETTINGS
====================================================================

Implement all settings and backups: account info, role/site, dark mode, attendance time window, manual backup export, approximate daily backup schedule at 22:00 Asia/Aden (subject to Android scheduling restrictions), application information/version, logout, and local-device data reset.

Do not guarantee exact Android scheduled execution where OS policies make it inexact. Support safe backup writes, interruption recovery, format/version validation, and user-visible success/failure.

DO NOT perform unsafe client-side restore directly against authoritative server records. Provide a dedicated server-controlled restore procedure, with authorization, backup format/version validation, impact preview, synchronization quiescence/reconciliation, referential integrity verification, protected audit event, and tombstone-aware recovery to prevent unwanted resurrection.

Clear and separate:
- "حذف بيانات هذا الجهاز" = local cache/credentials only.
- "حذف بيانات من السيرفر" = SUPER_ADMIN's validated destructive operation.

Do not show the latter to ordinary ADMIN or SUPERVISOR.

====================================================================
21. PERFORMANCE, ACCESSIBILITY, AND RESILIENCE
====================================================================

Keep interaction smooth for large personnel rosters, multi-year attendance histories, monthly grids, bulk imports, and archival reports. Use lazily built lists, efficient virtualization, paginated queries, suitable SQL indexes, bounded parsing batches, cancelable long operations, small state-observer scopes, deterministic cache invalidation and validated date arithmetic.

Handle loading/empty/error states for every data screen. Preserve unsaved edits and notify before losing them. Provide accessible labels, readable font sizing, screen reader semantics, and adequate contrast without changing the mandatory design identity defined here. Maintain proper Arabic error messages without exposing passwords, access tokens, raw backend secrets, or sensitive SQL diagnostics. Keep the app functional on supported Android API levels and release configuration.

====================================================================
22. COMPLETE AUTOMATED AND MANUAL TEST PROGRAM
====================================================================

UNIT TESTS:
- Username normalization/uniqueness, password policy, account role hierarchy.
- Supervisor role duty resolution, automatic/manual attendance precedence, Asia/Aden rollover.
- Non-PRESENT supervisor login denial for ABSENT, LEAVE, PERMISSION, custom state, null/unverified state, archived/inactive/unlinked person.
- Login denial despite old cached PRESENT, unauthorized role, disabled account, rejected device binding.
- ADMIN/SUPER_ADMIN LEAVE creation/change/cancel; SUPERVISOR unable to create, cancel or override LEAVE even through raw server requests and queued offline events.
- Personnel full-name and personnel-number edit with leading zeros/duplicates, linked supervisor stable IDs, archives with reason, historical snapshots.
- Date-range, leap year, archive, employment eligibility and report math.
- Audit actor/target/time/action mapping, multi-site redaction, supervisor self-and-admin-site scope, peer-supervisor exclusion.
- Identical supervisor provisioning validation for ADMIN and SUPER_ADMIN; admin provisioning restricted to SUPER_ADMIN; no orphan Auth/profile accounts.

WIDGET/GOLDEN TESTS:
- Defined login, ADMIN dashboard, SUPERVISOR dashboard, monthly roster, archive, reports, messages, settings.
- Functional "أسماء القوة", "الإجازات", "سجل العمليات", and "الإدارة العليا" screens.
- Remove EVERY login attendance-proof request button.
- Arabic RTL, documented exact colors, Tajawal typography, documented card/layout spacing, dark/light modes, site filters, dialogs.
- SUPERVISOR must not see LEAVE editing controls, Supreme controls, or peers' audit logs.

INTEGRATION/SECURITY TESTS (WITH TEST SUPABASE ENVIRONMENT):
- Successful ADMIN/SUPER_ADMIN creation of supervisor accounts with identical constraints.
- Successful SUPER_ADMIN creation of ADMIN account; ADMIN promotion/account-create blocked.
- Session expiry, account disable, role changes, last SUPER_ADMIN invariant.
- SUPERVISOR whose state changes to LEAVE is ejected/blocked from protected app functions without self-service request.
- Supervisor duty check at API write, restored session, midnight rollover, and reconnect.
- Exact operations visibility rules for ADMIN and SUPERVISOR, including peer actor and multi-site data redaction.
- Defined import commit, archive creation/restore, PDF/XLSX exports, messaging, outbox sync.
- Privileged deletion: authorize, preview, confirm, rollback, verify foreign keys, preserve schema/indexes/functions/RLS, propagate tombstones.
- Reconnect stale offline devices with deleted-record pending edits; verify no resurrection.
- Purged business log hidden from UI but protected minimal security journal stays valid under retention rules.
- Ensure inability to access unrelated site data by forging Flutter requests, role fields, record IDs, JWT metadata, or direct PostgREST calls.

TEST EVIDENCE MUST INCLUDE command, environment, exit status, and summary. A written specification is NOT a successful APK build. An emulator-only UI preview is NOT a backend authorization test. Where backend connectivity is missing, mark those checks BLOCKED and do not claim full completion.

====================================================================
23. EXACT IMPLEMENTATION PHASES AND QUALITY GATES
====================================================================

Develop in testable, buildable phases. After each phase: flutter analyze, applicable tests, Android debug build where feasible, database migration/test verification when backend-related, artifact and changelog. Resolve failures BEFORE advancing. Deliver a complete updated source archive at each buildable milestone if tools permit; do not provide only patches.

PHASE 00 — Extract COMPLETE requirements from THIS TEXT, create screen/role/data/rule maps, design tokens and requirement-to-code traceability. No external archive inspection.
PHASE 00A — DATABASE FIRST: generate complete importable Supabase SQL bootstrap, ordered mobile SQL Editor batches, canonical migrations, RLS/grants, seed data, database-side RPCs, ERD, first SUPER_ADMIN bootstrap instructions, Edge Function source, integrity/security tests, and Arabic mobile installation instructions. Validate executable SQL before defining Flutter integration; never require the user to upload any other project.
PHASE 01 — Flutter foundation, Android build files, package setup, lint, routing, state management, test scaffold.
PHASE 02 — Specified Arabic UI theme, Tajawal fonts, icons, components, RTL behavior, golden-test baseline.
PHASE 03 — Domain models, typed SQLite schema/migrations, repositories, stable identifiers, local unit tests.
PHASE 04 — Integrate and verify the SQL schema/RLS/RPC contracts ALREADY GENERATED IN PHASE 00A; exercise a reproducible dev test environment and correct any confirmed issues through a new versioned migration, without inventing a separate schema.
PHASE 05 — Authentication, internal username@adwas.com mapping, session/device controls, all three roles.
PHASE 06 — Specified dashboard, navigation, site filters, counts, notification indicator.
PHASE 07 — Personnel/sites/roster editing, documented archive/restore, "أسماء القوة" complete with name and (ع) editing.
PHASE 08 — Attendance engine, defined validation, batch edits, custom statuses, time windows.
PHASE 09 — "الإجازات" administrative LEAVE editing for individuals and supervisors, complete server enforcement.
PHASE 10 — Remove attendance-proof requests and implement authoritative SUPERVISOR PRESENT-only login gate plus mid-session revocation.
PHASE 11 — Monthly roster, custom columns, historical snapshots, site/member date calculations.
PHASE 12 — Archives, person/site history, restoration, historical report consistency.
PHASE 13 — Reports and real XLSX/CSV/PDF import/export, staged import commits, batch progress.
PHASE 14 — Supervisor management, device binding, one-way admin notifications and read/hide behavior.
PHASE 15 — Full "سجل العمليات" backend, actor/target/time/action display, role/site filtered visibility, redaction.
PHASE 16 — SUPER_ADMIN account management: identical supervisor creation constraints, new ADMIN creation, hierarchy and self-elevation protection.
PHASE 17 — Supreme dashboard and protected permanent-deletion service; dependency preview, transactions, audit, tombstones, recovery policies.
PHASE 18 — Offline-first outbox/delta sync with large dataset pagination, rev conflicts, deletion protection, authenticated background tasks.
PHASE 19 — Backup/restore, settings, system status, integrity verification.
PHASE 20 — Full regression/security/visual/accessibility/performance test suite; fix all found defects.
PHASE 21 — Complete Android APK generation, release-signing guidance, database deployment plan, final documents and acceptance report.

Each phase must state: in-scope files, specification requirement IDs, implemented acceptance criteria, security implications, tests executed, failures, produced artifacts, and whether it is allowed to proceed. Never mark a phase complete merely because code files exist.

====================================================================
24. FINAL FUNCTIONAL PARITY MATRIX AND ACCEPTANCE
====================================================================

Create and maintain a complete table:
Requirement ID/feature | Specified screen and UI | Flutter implementation | Server/local contract | Roles | Automated tests | Verified status | Engineering decision.

MANDATORY FINAL ACCEPTANCE:
- Every screen, workflow, menu item, action, and business rule specified in this TEXT is accounted for.
- Light/dark palette, text hierarchy, Arabic RTL, card styling, navigation, reports, and monthly tables match the documented visual blueprints and are validated by golden/widget tests.
- All document-defined functionality is implemented with no nonfunctional placeholders in the deliverable.
- "أسماء القوة": ADMIN can edit names/numbers and archive people; SUPER_ADMIN inherits this.
- "الإجازات": ADMIN/SUPER_ADMIN exclusively control LEAVE for ordinary individuals AND supervisors; SUPERVISOR cannot alter it by UI, cached events, or API.
- "سجل العمليات": complete who/what/whom/when history; ADMIN sees all administrator/supervisor operations; SUPERVISOR sees only their OWN operations plus site-relevant ADMIN/SUPER_ADMIN operations; SUPER_ADMIN only can purge eligible business logs.
- "طلب إثبات حضور": ABSENT FROM CODE AND INTERFACE; SUPERVISOR logs in only with server-verified effective PRESENT generated by authorized admin or valid automatic attendance.
- SUPER_ADMIN can create SUPERVISOR and ADMIN accounts with consistent established validation, database, auth, and audit processes.
- ADMIN can still create SUPERVISOR but cannot create ADMIN or SUPER_ADMIN.
- Only SUPER_ADMIN may invoke exclusive irreversible business-data deletion functions; all dependencies and sync tombstones preserved.
- A COMPLETE importable new-project Supabase database package exists, with actual schema-creating SQL, mobile-size ordered SQL Editor batches, canonical migration files, installation validation queries, and documented Auth/Edge Function deployment separation; Flutter precisely matches its installed contract.
- Supabase role isolation and protected RPCs tested, not just client controls.
- SQLite and Supabase sync protects historical and deleted records across offline devices.
- Real XLSX/CSV/PDF and archived/monthly reporting produce correct Arabic outputs.
- All tests/builds listed with accurate verification evidence and environment.

Deliverable archive must include full Flutter source, pubspec.yaml and lockfile, assets, relevant Android configuration, fully commented Supabase migration files, required Edge Functions, test suites, developer README, specification-to-Flutter traceability, permission matrix, schema/ER documentation, run/build/setup instructions, and any test-generated artifacts that can legitimately be shared. No secret keys or private credentials.


====================================================================
25. NORMATIVE SCREEN-BY-SCREEN UI BLUEPRINT (NO IMAGES NEEDED)
====================================================================

THIS SECTION IS PART OF THE PRODUCT CONTRACT, NOT AN OPTIONAL REFERENCE. Implement every described screen and its real navigation and persistence operations. Use Flutter Material 3, right-to-left ordering throughout, light/dark colors from Section 04, and Tajawal. All screens include proper top system-inset handling, keyboard avoidance, scrolling, safe areas, and accessible semantic labels. The specified application visuals are NOT provided; this blueprint defines the new implementation target.

GLOBAL COMPONENT STANDARDS:
- Main content: full screen width, centered content when tablet width is very large, responsive body margin 12-16dp on phones, 20-24dp on tablets.
- Background #F5F7FB (light) or #0F1927 (dark); primary surface cards #FFFFFF / #182638.
- Rounded main cards: 16dp radius, internal padding generally 14-16dp, vertical gap 10-12dp; dialogs 22dp radius.
- Primary actions: filled Primary color, white/light contrast text, rounded 12dp; destructive actions use Error color with clear Arabic text; secondary actions outlined or tonal.
- Text hierarchy: page title 22-24sp Bold, section title 17-19sp Bold, card title 15-17sp Bold, body 14-16sp, captions 12-13sp, all using Tajawal; support system accessibility scaling without overlap.
- Toolbar: Arabic page title, contextual back arrow, role-appropriate actions; Back navigates correctly, preserving or warning about unsaved data.
- Lists: full-width tappable rows with leading (RTL right) circular icon, title, subtitle, state chip, trailing (RTL left) relevant action.
- Inputs: filled/outlined text fields with Arabic floating label, in-field error, clear action where appropriate; numeric personnel ID display must preserve leading zeros.
- Date pickers: Arabic labels and consistent Gregorian dates; use Asia/Aden for attendance business dates.
- Search/filter: search field across card width, optional horizontally scrollable status/site chips, visually indicated active filter.
- Lists and table loading: skeleton/progress indicator; empty and error states include actual retry/help; long data is paged/virtualized.
- Confirmations: record count, affected target, summary, cancel and confirm actions; never visually privilege a destructive action over cancel.
- Icon sets: use a coherent Material Icons family; choose semantically obvious icons (person, groups, event-available, shield, history, site/map, description, notifications, upload/download, archive, delete, settings) and apply fixed sizes 20-24dp in regular actions.
- Role-specific route authorization and display must be driven by effective verified permissions, never untrusted local flags.

LOGIN SCREEN:
- Uncluttered centered column with application identity "إدارة التمام اليومي" and a coherent attendance/personnel symbol (use a self-generated SVG or built-in icon, no provided image assumed).
- Main rounded card: title "تسجيل الدخول", field "اسم المستخدم", field "كلمة المرور" with visibility control, button "دخول" with spinner while submitting.
- Do NOT show an email field, an "إثبات حضور" button, a "طلب حضور" link, an unapproved self-registration screen, or an attendance workaround.
- Show Arabic field validation and authentication errors below the relevant controls; for blocked SUPERVISOR show an attendance-specific non-sensitive Arabic reason and a logout/back action.
- Persist valid sessions securely and recheck role/duty on restoration before protected navigation.

HOME — ADMIN AND SUPER_ADMIN:
- App bar "الرئيسية", current Gregorian date and localized day, notifications/unread icon, sync status action.
- Summary area with compact stat cards: إجمالي الأفراد, موجود, غياب, إجازة, إذن; if custom statuses exist, an expandable list/summary displays them.
- Site selection with "جميع المواقع" for admin and clear active site; search by "الاسم أو الرقم (ع)".
- Roster list with index, personnel number, full name, site, effective daily status chip and authorized quick attendance editing.
- Cards/tables adapt on narrow phones; avoid horizontal body overflow.
- SUPER_ADMIN sees a link/card into "الإدارة العليا" through الإدارة screen; do not change ordinary five-item bottom bar solely for this role.

HOME — SUPERVISOR:
- After server-verified PRESENT gate, use the same visual language but restrict site to assigned site; never offer a global site selector.
- Display own site's daily summary and individuals; allow only in-window, in-scope non-LEAVE attendance changes where authorized.
- When duty is revoked, replace the protected screen with the access-denied screen and discard sensitive state.

BOTTOM NAVIGATION (RIGHT TO LEFT AS LOGICAL RTL ORDER):
- ADMIN and SUPER_ADMIN: الرئيسية | التمام الشهري | أرشيف التمام | الإدارة | الإعدادات.
- SUPERVISOR: الرئيسية | التمام الشهري | أرشيف التمام | التقارير | الإعدادات.
- Use labelled icons and selected-state Primary colors; persist per-tab navigation appropriately; implement system back behavior.

ADMINISTRATION HUB "الإدارة":
- Use 2-column responsive tile/cards on phones, with Arabic icon, bold title, one-line description, consistent padding and spacing.
- Required entries: تعديل الكشوفات, تعديل القوة, أسماء القوة, الإجازات, إدارة الحالات والمواقع, إدارة المشرفين, النقل الداخلي, إرسال الإشعارات, سجل العمليات, التقارير, الاستيراد والتصدير, إعدادات التحضير.
- SUPER_ADMIN ONLY: additional "الإدارة العليا" card, same visual identity but a distinct shield icon.
- Every tile must navigate to a functioning module.

"أسماء القوة":
- Toolbar and search; site filter and segmented active/archived filter.
- Row shows personnel name, (ع), site and current archive status.
- ADMIN/SUPER_ADMIN edit sheet: fields الاسم and الرقم (ع), validate, Save/Cancel; show field conflict resolution.
- An "أرشفة" action asks for a reason and explicit confirmation, then calls protected standard archive RPC; archived entries can be restored by authorized admins through archive page.
- SUPER_ADMIN can also invoke a separate red "حذف نهائي" action routed to deletion preview/confirmation; do not confuse with archive.

"الإجازات":
- Date or date-range controls at top; site filter and personnel search; include linked supervisors in the same list.
- Each row shows person name, number, site, current status, current-day leave context if selected.
- ADMIN/SUPER_ADMIN actions: "تحديد إجازة", "تعديل الإجازة", "إلغاء الإجازة", select multiple people/dates, preview changes, confirm; history display.
- SUPERVISOR cannot navigate here or call the backing RPC.
- If a linked supervisor is changed away from PRESENT for today, the subsequent session/duty checks must revoke protected access.

DAILY ATTENDANCE EDITOR:
- Reachable from dashboard person row or bulk-selection toolbar. Show person(s), target date(s), four canonical status choices with color/symbol, status comment if the model supports it, review panel and commit action.
- Support group queue for bulk operations and an easy remove-group action before commit.
- LEAVE choice visible only to authorized ADMIN/SUPER_ADMIN, and no route can override protected leave with a supervisor-generated change.

"التمام الشهري":
- Toolbar, month/year selector, site filter (role dependent), export action, person search.
- Horizontal day-table with frozen/sticky identity columns where feasible: الرقم | الرقم (ع) | الاسم | الموقع, and one column per calendar date 1..N; status cells show abbreviated symbols and required colors.
- Support horizontal table scrolling independently of vertical roster scrolling, proper header alignment, row virtualization for large lists, totals, custom column toggles and stable font sizing.
- Month summary and archive/snapshot state clearly labelled; choose historical snapshot when present.

"أرشيف التمام":
- Year selector, months list, site filter, archived snapshots list with date, creator, count and status; archived personnel/sites subviews and restoration actions.
- On selecting a snapshot, open read-only historical monthly table preserving historical labels/members/statuses; exports available according to role.
- ADMIN/SUPER_ADMIN restore permitted records through explicit confirmation; only SUPER_ADMIN can permanently purge eligible archive data.

"التقارير":
- Select site, one or many individuals, start/end dates; validate max five calendar years and reasonable limits.
- Preview/report page shows per-status counts, selected person/date range, monthly groupings, historical and missing-period indicators.
- Export actions Excel/PDF with progress, cancellation, and Android file-save dialog.
- Supervisor sees only authorized site data; ADMIN/SUPER_ADMIN can view all sites.

"تعديل الكشوفات" / "تعديل القوة":
- CRUD on legitimate configurable columns and roster display fields, required core-column protection, person/site edit forms, real persistence and operation events.
- Support reorder/hide/show custom columns within configured maximum count; prohibit duplicate IDs/titles and invalid data types.

"إدارة الحالات والمواقع":
- Site list with counts, create/rename/retire/restore, unassigned "غير محدد" handling; custom status management separated from site management as suitable.
- Canonical PRESENT/ABSENT/LEAVE/PERMISSION cannot be accidentally redefined as a different permission meaning.

"إدارة المشرفين":
- List supervisor accounts with username, linked person, site and enabled/device state.
- Form: username, password, linked person, assigned site; fields validated server-side; create, disable, reset password/device, reassign, remove supervisor role.
- ADMIN and SUPER_ADMIN share exactly the same supervisor-create backend procedure and constraints.

"النقل الداخلي":
- Select one/many eligible individuals, source and destination site, effective date where applicable, confirm, persist and show history; keep archived site/history semantics.

"إرسال الإشعارات" and notifications inbox:
- Admin compose: title, body, recipient(s)/site, send with validation and success state.
- Supervisor inbox: unread badge/list, open/read/recipient-local hide only; no reply/compose/send globally.
- Supremes can remove selected server messages only through Supreme deletion engine.

"سجل العمليات":
- Newest-first cards/rows with four conspicuous fields: "نوع العملية", "على من تمت", "متى تمت", "من قام بها".
- Filters: time range, action, site and permitted actor; detail modal with authorized old/new values; paginate long results.
- ADMIN views all; SUPERVISOR only personal operations plus site-relevant ADMIN/SUPER_ADMIN operations (not peer supervisor events); only SUPER_ADMIN has red remove/purge controls.

"الإعدادات":
- Account name/role/site, theme, sync/last sync, attendance window configuration (admin only), automatic presence policy configuration (admin only), backups, user/device data reset, app information/version, logout.
- Clearly distinguish device-only deletion from protected server deletion.

"الإدارة العليا":
- Shield-themed but same palette and card system; grouped cards for admin/supervisor accounts, personnel/sites, daily/monthly marks, archives, operations logs, messages, deleted data, backups/restore, permissions, integrity/sync.
- Account editor: create ADMIN and SUPERVISOR, enable/disable, manage roles per server hierarchy, reset credentials securely, last SUPER_ADMIN protection.
- Deletion manager: select exact records and scope; server-produced dependency/count preview; explicit typed confirmation for broad irreversible action; submit with step-up verification; show transactional result and cache update.
- Never expose SQL editor or table DROP/TRUNCATE commands to application users.

====================================================================
26. FULL INDEPENDENT DATA DICTIONARY AND OPERATIONAL SEMANTICS
====================================================================

Implement a deployable consistent schema; names below are the authoritative NEW project contract. Expand only if required by relationships, indexes, and workflows. Include CREATE TABLE and RLS/GRANT migrations, triggers, schema diagram, and matching typed Dart models; do not require a schema export from the user.

COMMON COLUMNS: Each persistent business table uses stable UUID primary key where logical; created_at timestamptz DEFAULT now(), updated_at timestamptz where needed, revision bigint NOT NULL DEFAULT 1; nonnullable references and proper unique/check constraints. Business date is DATE interpreted in Asia/Aden; record timestamps use timestamptz UTC internally and format into Asia/Aden for user view.

manger_profiles:
- user_id uuid PRIMARY KEY references auth.users(id) with deliberate deletion strategy.
- username text unique normalized; role text checked IN ('SUPER_ADMIN','ADMIN','SUPERVISOR'); enabled boolean.
- person_id uuid nullable FK to manger_employees (mandatory for SUPERVISOR); site_id uuid nullable FK to manger_sites (mandatory for SUPERVISOR); created_by uuid nullable; last_role_change_at timestamptz, revision.
- ADMIN and SUPER_ADMIN may be organization-wide; do not force a supervisor-specific person/site identity for them.

manger_sites: id uuid, name text unique among active normalized sites, is_active, archived_at, archive_reason, revision. Seed the special "غير محدد" site (stable ID and protected business role) and prevent its accidental unsafe removal.

manger_employees: id uuid, personnel_number text, full_name text, site_id uuid, roster_order integer, start_date date, end_date date nullable, archived_at timestamptz nullable, archive_reason text nullable, created_by uuid, revision. Enforce sensible nonblank name, canonical number uniqueness among appropriate live population, valid date range, text preservation of leading zeros; document policy for archived reuse.

manger_attendance: id uuid, person_id uuid, attendance_date date, status_code text FK/status validity, recorded_by uuid nullable for system actors, source text ('ADMIN','SUPER_ADMIN','SUPERVISOR','AUTOMATIC','IMPORT'), reason text nullable, revision; unique (person_id, attendance_date) for explicit authoritative marks. Do not automatically materialize a row for every eligible date unless business policy requires it.

manger_statuses: code text primary key, arabic_label text, short_symbol text, color_hex text, is_core boolean, enabled boolean, allows_supervisor_write boolean, sort_order; seed PRESENT/موجود/م/#21885A, ABSENT/غياب/غ/#CA4350, LEAVE/إجازة/ج/#3477CB, PERMISSION/إذن/إ/#BA8317. LEAVE has allows_supervisor_write=false; protect core semantics from deletion and privilege escalation through custom codes.

manger_settings: setting_key text primary key, value_json jsonb, updated_by uuid, updated_at. Required keys: attendance_window_enabled, attendance_window_start, attendance_window_end, timezone='Asia/Aden', automatic_presence_enabled, automatic_presence_rule, daily_backup_enabled, data_retention_policy, max_custom_columns. Default automatic_presence_enabled should be an EXPLICIT administrator-controlled policy setting, not a client-inferred truth. If enabled, all input conditions below are enforced at server evaluation time.

manger_archives: id uuid, archive_month date (first date of month), site_id uuid, snapshot_version int, immutable_snapshot jsonb or normalized member/marks children, created_by, created_at, content_checksum, revision; unique version key, never overwrite immutable historical version without dedicated new snapshot record.

manger_column_definitions: id uuid, column_key text unique, arabic_title text, column_type ('text','number','date','boolean'), display_order int, is_required boolean, is_visible boolean, enabled boolean, created_by. Protect mandatory الرقم, الرقم (ع), الاسم, الموقع.

manger_employee_custom_values: person_id uuid, column_id uuid, value_json jsonb, revision; PRIMARY KEY(person_id, column_id).

manger_employee_archive_events: id uuid, person_id uuid nullable when history retention rules demand denormalized tombstones, event_type ('ARCHIVE','RESTORE'), reason text, site_id, actor_id, happened_at, safe_label_snapshot; any hard deletion must follow explicit dependency policy.

manger_transfers: id uuid, person_id, from_site_id, to_site_id, effective_date date, actor_id, happened_at, reason; represent batch correlation ID for multi-person transfer and preserve site change history.

manger_messages: id uuid, sender_id, title text, body text, created_at, revision, deleted_at; manger_message_recipients: id uuid, message_id, supervisor_user_id, target_site_id where appropriate, read_at nullable, hidden_at nullable; unique delivery key to prevent duplicates. Message visibility MUST be recipient scoped.

manger_operations_log: id uuid, actor_id nullable for SYSTEM, actor_role, action_code, target_type, target_id nullable, target_label_snapshot, happened_at, site_ids uuid[] or site-membership relation, safe_summary jsonb, outcome, request_id text unique where idempotency requires. Logs written from validated server mutation path; personally performed SUPERVISOR events remain attributable.

manger_security_audit: id uuid, actor_id, security_action, target_ref_hash / minimal safe identifier, happened_at, outcome, request_id; restricted to elevated audit readers, retention based on legal and product policy; not ordinary removable operations history.

manger_supervisor_devices: id uuid, supervisor_user_id, device_public_key_or_installation_proof, bound_at, revoked_at, revision. Use strong challenge/response if feasible, protect device secret in Android Keystore, and let an authorized admin reset it.

manger_import_jobs: id uuid, actor_id, created_at, state ('STAGING','VALIDATED','COMMITTED','ABORTED'), expected_rows, processed_rows, commit_key unique, details jsonb. manger_import_rows: id uuid, job_id, source_row int, raw_data jsonb, normalized_data jsonb, validation_error jsonb, final_person_id nullable; unique(job_id,source_row).

manger_change_log: sequence bigint GENERATED ALWAYS AS IDENTITY UNIQUE, id uuid, entity_type, entity_id uuid, change_kind ('UPSERT','DELETE','REASSIGN','ROLE_CHANGE'), entity_revision bigint, scope_site_ids, emitted_at, minimal_payload jsonb, deletion_generation bigint nullable, actor_id. The server uses this authoritative monotonically increasing sequence for paged delta sync and cursor-based resumption.

manger_delete_tombstones: id uuid, entity_type text, entity_id uuid, deletion_generation bigint, deleted_at timestamptz, deleted_by uuid, request_id uuid, minimum_retention_until timestamptz. Ensure old offline outbox replays cannot resurrect removed immutable IDs, even after cache refresh or backup restoration.

manger_delete_requests: id uuid, actor_id uuid, scope jsonb, preview_hash text, preview_created_at, confirmed_at nullable, finished_at nullable, result_json jsonb, status, idempotency_key text unique, revision_check jsonb, step_up_verified_at. Request preview must expire after material dependency changes.

manger_backup_metadata: id uuid, creator_id, created_at, covered_sequence bigint, storage_pointer text, checksum text, restore_state text, restored_at nullable, encryption_info jsonb; no plaintext backup credentials.

LOCAL DATABASE: offline counterparts use a typed SQLite schema including the locally authorized subset of people, sites, marks, snapshots, custom values and messages; have sync_outbox(event UUID, authenticated user ID, entity, immutable target ID, base revision, payload, state, retry_count, timestamps), sync_cursors(user_id, sequence), sync_conflicts, sync_receipts, tombstones. Ensure transactional migration and per-identity separation.

EFFECTIVE ATTENDANCE POLICY:
1. Compute server-local business_date = current timestamp AT TIME ZONE 'Asia/Aden' cast to date.
2. Verify person exists, is active, eligible between start_date/end_date, belongs to an active authorized site where applicable, and has not been archived/retired.
3. If an explicit authorized mark exists for person and date, return it. Explicit ABSENT, LEAVE, PERMISSION and non-PRESENT custom codes OVERRIDE any automatic PRESENCE.
4. Else if automatic_presence_enabled AND the person is eligible AND the current configured automatic attendance rule permits this date, server may resolve effective PRESENT with origin AUTOMATIC. A mere missing row is never independent proof.
5. Otherwise return UNKNOWN/UNRECORDED and deny SUPERVISOR protected entry.
6. Return canonical status code, origin, business_date, revision/policy generation and check time; do not permit Flutter to compute its own login permission.
7. An admin mark can override a valid automatic value, but the supervisor cannot alter their own duty status or administrative LEAVE.
8. On role/site/person/attendance/policy change, invalidate access as soon as server checks or push events identify revocation; all sensitive writes revalidate server-side.

AUDIT SCOPE POLICY:
- SUPER_ADMIN and ADMIN can view all ordinary operations logs across the organization.
- SUPERVISOR query returns only (actor_id=auth.uid()) OR (actor_role IN ADMIN/SUPER_ADMIN AND relevant authorized site membership). Exclude peer supervisor activities even same site.
- For multi-site administrative actions, return a site-redacted view/materialized safe summary, never full target lists for other sites.
- No role except SUPER_ADMIN may purge removable business operation rows; protected security events cannot be erased through ordinary app UI.

ACCOUNT RULES:
- ADMIN creates SUPERVISOR via same shared server provisioning as SUPER_ADMIN.
- SUPER_ADMIN creates ADMIN and SUPERVISOR; only trusted bootstrap creates first SUPER_ADMIN; changing account role to SUPER_ADMIN requires authorized explicit procedure and last-super-admin invariant.
- All usernames are 3-32 chars matching ^[a-z][a-z0-9_]{2,31}$; internal auth identifier is username@adwas.com; only username and password shown in UI.
- Managed-password rule: 6-72 chars, at least three digits, at least one symbol, no whitespace; do not expose password hashes/cleartext in logs or database public tables.
- For a SUPERVISOR, validate linked ACTIVE person/site identity before provisioning; ADMIN creation follows administrator role requirements, not supervisor site binding.

HISTORICAL SNAPSHOT CONTRACT:
- Snapshot member entries retain immutable person ID and historical name, personnel number, site and effective dated status at capture. Changing a current name or archiving the person never modifies old snapshot labels without an explicitly versioned correction.
- Month coverage and leap-year logic consistent; raw current attendance and immutable historical snapshot are distinct types.
- Supreme deletion previews indicate whether underlying marks, snapshots, exports, or all explicitly selected target categories will be deleted.

====================================================================
27. SERVER API/RPC CONTRACTS AND SUPABASE SECURITY IMPLEMENTATION
====================================================================

Produce actual implemented code and integration tests for each endpoint below. Use PostgreSQL RPC for transactional database actions, Edge Functions for interactions needing service-role Auth Admin API. Do not allow the client to self-elevate roles, choose arbitrary target tables, or write security audit actors.

1. `manger_provision_account` (Edge Function; POST): body {username,password,target_role,person_id?,site_id?,idempotency_key}. Verify authenticated ADMIN for SUPERVISOR only, SUPER_ADMIN for ADMIN/SUPERVISOR; use Auth Admin API securely, then insert validated profile, compensate partially created account if profile fails. Response account id, username, role, enabled, linked person/site and success, never password.
2. `manger_reset_managed_password` (Edge Function): {user_id,new_password,idempotency_key}; caller ADMIN for SUPERVISOR, SUPER_ADMIN for ADMIN/SUPERVISOR; revalidate role, account enabled and password policy.
3. `manger_manage_managed_account` (trusted RPC/Edge): {user_id,operation,role_if_change?,site_id?,person_id?,revision,idempotency_key}; allowlist enable/disable/change_site/reset_device/revoke/role_change and enforce hierarchy/last-super-admin invariant.
4. `manger_resolve_effective_attendance` (RPC): {person_id, business_date?}; server validates caller/scope and returns {canonical_status,origin,eligible,date,revision,policy_generation,checked_at}.
5. `manger_check_supervisor_access` (RPC): no caller-chosen supervisor ID; resolve auth.uid() -> active profile/person/site/device -> today's authoritative status; return allowed:boolean,reason_code,server_date,check_generation. Any sensitive SUPERVISOR write independently calls equivalent server authorization logic.
6. `manger_apply_attendance_event` and `manger_apply_attendance_batch`: {targets:[{person_id,date,status_code,expected_revision}],idempotency_key}. Validate role, site, duty gate, edit window, archive dates, employment dates, LEAVE protection; commit attendance + operations log + change log transactionally. Bulk operations are atomic per clearly specified batch policy.
7. `manger_apply_leave_batch`: {targets,leave_or_cancel,reason?,expected_revisions,idempotency_key}. Only ADMIN/SUPER_ADMIN. Reject supervisor even for self or via direct PostgREST.
8. `manger_update_personnel_identity`: {person_id,new_full_name,new_personnel_number,expected_revision,request_id}. Only ADMIN/SUPER_ADMIN; enforce uniqueness, update current labels without rewriting historical snapshots, log both safe old/new values.
9. `manger_archive_employee_with_reason`, `manger_restore_archived_employee`: secure archive/restore for ADMIN/SUPER_ADMIN; preserve history, invalidate linked SUPERVISOR duty access when inactive.
10. `manger_activate_or_create_site`, `manger_retire_site_to_unassigned`, `manger_restore_retired_site`, `manger_transfer_employees_batch`: validate site and personnel IDs, update references and track transfers with audit/change events.
11. `manger_create_monthly_snapshot`, `manger_get_monthly_snapshot`: fixed historical roster membership and status; authorize site/month; immutable versioned storage and deterministic export content.
12. `manger_begin_import`, `manger_stage_import_batch`, `manger_validate_import`, `manger_commit_import`: ADMIN/SUPER_ADMIN only; staged, duplicate-safe, atomic commit, detailed error counts, idempotent retry.
13. `manger_send_supervisor_message`, `manger_list_messages`, `manger_hide_message`: one-way administrator send, per-recipient inbox and read/hide states, site scope/redaction; supervisor cannot compose/send/reply.
14. `manger_list_operations`: bounded server pagination with role-filtered safe response; do not expose sensitive full event JSON to unauthorized roles.
15. `manger_preview_deletion`: {operation_type,target_ids?,scope,expected_revisions}; SUPER_ADMIN only; return {preview_id,preview_hash,expiry,entity_counts,deps,impact,recoverability,affected_site_count}, no server mutation.
16. `manger_execute_deletion`: {preview_id,preview_hash,typed_confirmation,idempotency_key,step_up_context}; SUPER_ADMIN only, recheck preview/dependencies/revisions, transactional authorized deletion + minimal audit + tombstones. Return exact deleted counts and failure reason. Never accept raw SQL/table names from Flutter.
17. `manger_get_changes_since`: {after_sequence,limit}; server returns authorized ordered change events, latest cursor, has_more; include deletions; no cross-role data leaks.
18. `manger_apply_sync_batch`: {events:[{event_id,entity,target_id,base_revision,kind,payload}]}; per-event authorization and idempotency, reject stale/deleted ID resurrection, conflict details, accepted server sequence.
19. `manger_backup_preview`, `manger_restore_eligible_deleted_business_data`: SUPER_ADMIN or applicable authorized role; preview verified backup, check constraints, preserve tombstones, controlled transactions and log actions.
20. `manger_reset_supervisor_device`: shared ADMIN/SUPER_ADMIN permission, with signed revocation and next-claim authorization.

SECURITY IMPLEMENTATION:
- Use RLS on ALL client-facing tables. Direct writes on critical tables (profiles, operations log, security audit, deletion requests/tombstones, duty-policy, attendance where bypass risk exists) are REVOKED or restricted to trusted functions.
- Do not rely on untrusted JWT user_metadata for roles. Source role from protected manger_profiles mapped to auth.uid(), and enforce enabled flag.
- SECURITY DEFINER functions declare a fixed `SET search_path` and schema-qualified references, validate auth.uid(), enforce least privilege, avoid raw dynamic SQL and cross-tenant row leakage.
- Function privileges, grants and RLS must be explicitly tested with actual ADMIN, SUPERVISOR and SUPER_ADMIN sessions in a test project.
- All mutating requests include idempotency IDs, expected revisions, structured results, and safe Arabic-mapped error codes; database errors should not leak secrets to users.
- Keep service-role keys ONLY on trusted Edge Functions or secure deployment environments.
- If setting up a fresh server, generate safe migration scripts, seed minimal reference statuses/settings/unassigned site, bootstrap instructions for the initial SUPER_ADMIN, and documented Supabase configuration; never require uploading an existing schema/project archive.

====================================================================
28. COMPLETE TEXT-ONLY DEVELOPMENT OUTPUT AND EXECUTION GUIDANCE
====================================================================

The user supplies ONLY THIS TEXT FILE. Do not ask for any other uploaded input, prior codebase, zip archive, APK, UI mockup, image, or file. Work within the AI coding environment to create all files from scratch.

Mandatory generated project outputs (as files in the created source tree; not as user input):
- `pubspec.yaml` and lockfile, clean Flutter `lib/` source, `android/` build configuration, Tajawal fonts acquired from an authorized source or legally referenced dependency, stable assets.
- Models, repositories, state providers, widgets, routes, screens, local persistence, secure-session storage, background sync, import/export functionality.
- COMPLETE SUPABASE INSTALLATION PACKAGE under `database/`: executable `SUPABASE_DATABASE_BOOTSTRAP.sql`, short ordered mobile `sql_editor_batches/*.sql`, `VERIFY_INSTALLATION.sql`, role tests, seed source, Arabic Android-phone SQL Editor instructions, first SUPER_ADMIN Auth bootstrap instructions, plus canonical `supabase/migrations/`, Edge Functions, authorization policies, constraints, procedures and tests. SQL import and Edge Function deployment are separately documented.
- `test/` and `integration_test/`, sample fake data exclusively for tests/dev with no demo data silently appearing in production.
- `README.md`, `SETUP_SUPABASE.md`, `DEPLOYMENT.md`, `PERMISSION_MATRIX.md`, `DATABASE_ER.md`, `FEATURE_MATRIX.md`, `TEST_REPORT.md`.
- Android debug APK if build tools available; release guidance and a way to configure secure signing WITHOUT source-committed passwords/private keys.

ZERO MISSING-PROJECT EXCUSES: This file describes the required product. The absence of Kotlin source/screenshots is NOT a reason to stop, ask for an archive, or downgrade the scope to a tutorial. The solution cannot promise unverifiable historical pixel parity, but it must implement the exact documented tokens and requirements faithfully.

NO FAKE COMPLETION: If the AI environment cannot run Flutter builds, Supabase integration tests or deploy server functions, report those as unverified, still generate complete source, SQL, tests and runnable commands. Never state "built successfully" without actual build evidence. If a required cloud credential is absent, scaffold configurable inputs; do not invent real project URL/keys or try to connect to an imaginary server.

SINGLE TEXT FILE EXECUTION ORDER:
A. Parse this entire text into requirement IDs and dependency graph.
B. Generate complete database SQL installation files FIRST (Section 18A / Phase 00A), including short Android-friendly SQL Editor batches and independently checked SQL schema/roles/tests/installation instructions.
C. Generate a complete Flutter file tree and environment/config templates; implement Flutter UI/domain logic, local SQLite, and integration with the exact generated Supabase contract.
D. Add tests for every role, site scope, leave restriction, supervisor attendance gate, operation-log visibility, deletion/collision/concurrency, and data sync.
E. Run analyzer, tests, migrations and APK build where tools exist; fix all reproducible failures.
F. Deliver code artifacts with an evidence-based implementation checklist, not just narration.

ACCEPTANCE PRIORITY: Security-critical role/attendance/deletion correctness, full documented user workflows and reliable server synchronization take precedence over cosmetic polish; both functional and visual requirements remain mandatory.

====================================================================
29. EXECUTION COMMAND
====================================================================

START NOW. Read ALL sections of THIS TEXT DOCUMENT and create the complete Flutter/Dart Android application FROM SCRATCH. There is no other source or input file. Do not request or wait for a project archive, Kotlin code, a screenshot, design attachment, or historical schema. Use the normative visual blueprint, three-role permissions, complete database dictionary, server contracts and phase schedule in this text as the only authoritative requirements.

First implement PHASE 00 as a specification-to-code requirements inventory (not source forensic analysis); immediately deliver PHASE 00A as the COMPLETE IMPORTABLE SUPABASE SQL DATABASE INSTALLATION PACKAGE (including mobile-sized numbered SQL scripts); then execute the remaining phases sequentially and produce real Flutter source files, Edge Functions, tests, documentation, and verifiable builds where your environment permits. Never substitute explanations or plans for implementation when code-generation tools are available.

SECURITY-CRITICAL ACCEPTANCE: (1) ADMIN-only LEAVE creation/change/cancel; (2) SUPERVISOR enters only with SERVER-VERIFIED effective PRESENT and has no attendance-proof request; (3) operations-log access policy: ADMIN all, SUPERVISOR self plus site-related ADMIN/SUPER_ADMIN only, SUPER_ADMIN protected purge; (4) SUPER_ADMIN can create both administrators and supervisors and safely delete authorized server business records without damage to tables/relationships or resurrection through offline sync.

FINISHED PRODUCT: "إدارة التمام اليومي" — a self-contained, production-oriented Flutter/Dart Android application meeting every functional, security, and documented visual requirement of this text alone.
