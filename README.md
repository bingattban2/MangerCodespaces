# التمام اليومي

Offline Windows personnel and attendance application. The four core layers (`Domain`, `Contracts`, `Application`, `Infrastructure`) remain independent of presentation. `AlTamam.Desktop` is the WPF/WebView2 host for the existing Arabic RTL local pages in `AlTamam.WebUI`.

The implemented authentication workflow provisions `adwas` with a user-chosen strong password, persists Argon2id credentials in SQLCipher, applies lockout, validates expiring/revocable sessions and enforces operation-specific account permissions. Acknowledgement of the recovery key is required before first-run enrollment. Ordinary and supervisory accounts must link to active roster members. Credentials and session tokens are excluded from account-list DTOs and audit descriptions.

The host maps only `https://manger.adwas.example/` to bundled resources. External navigation/resources, popups, downloads and host objects are blocked. The message dispatcher validates origin, request correlation, protocol version, exact payload shape and server-held session authority. Failed or rolled-back operations never become successful responses.

Registry screens support adding personnel, effective-dated identity updates, soft archival, site and status catalogs, and roster-linked account management. Mutations are session-checked, transactional and audited; destructive site changes require a current impact preview. Historical snapshots remain unchanged.

Current-day attendance supports authenticated single and atomic bulk confirmation, stale-preview rejection, immutable revision evidence, real dashboard counters, unprepared filters and user-owned drafts. The existing attendance and dashboard pages use persisted SQLCipher data.

Internal/external transfers and separation preserve historical snapshots and half-open assignments. Transfer-day attendance is excluded; linked accounts are revoked for external transfers and separation. The monthly grid uses bounded rows, frozen identity columns and historical cell details. The daily background worker performs current-day 20:00 processing and startup catch-up without rewriting previous days.

Person and force reports reconcile approved attendance days separately from internal transfers. Excel exports use historical snapshots, explicit text cells for user values, native file selection, cancellable background writing and staged file publication. All-sites monthly export creates a ZIP containing the overall workbook and one workbook per site. A changed report requires a new preview before export.

Excel import, custom columns, audit administration, settings and backup/restore workflows are not yet complete. Installer and Portable distribution remain gated on complete functionality and real Windows acceptance.

## Build and run

On Windows with .NET SDK 10 and Microsoft Edge WebView2 Runtime:

```powershell
dotnet restore AlTamam.slnx
dotnet build AlTamam.slnx -c Release --no-restore
.\src\AlTamam.Desktop\bin\Release\net10.0-windows\AlTamam.exe
```

The distributable runtime and installer are prepared only after all business workflows pass acceptance. Tests, synthetic records and acceptance evidence live outside the production tree.

## Data and key lifecycle

Data is stored under the current Windows user's `%LOCALAPPDATA%\AlTamamAlYawmi`, outside the installation or Portable folder. `WindowsDatabaseKeyStore` generates a random 256-bit installation key and protects it with current-user Windows DPAPI. Missing or damaged keys fail closed; the application never creates a replacement key for an existing database. Keep the recovery key shown during first-run setup in a separate safe location.

SQLCipher validates the supplied key against real encrypted pages before database setting changes. Legacy schema hardening creates a verified encrypted SQLite online recovery snapshot and records checksummed migration components; historical records and V001 SQL are retained.
