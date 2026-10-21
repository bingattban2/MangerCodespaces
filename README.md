# التمام اليومي

Offline Windows personnel and attendance application. The four core layers (`Domain`, `Contracts`, `Application`, `Infrastructure`) remain independent of presentation. `AlTamam.Desktop` is the WPF/WebView2 host for the existing Arabic RTL local pages in `AlTamam.WebUI`.

The implemented authentication workflow provisions `adwas` with a user-chosen strong password, persists Argon2id credentials in SQLCipher, applies lockout, validates expiring/revocable sessions and enforces operation-specific account permissions. Acknowledgement of the recovery key is required before first-run enrollment. Ordinary and supervisory accounts must link to active roster members. Credentials and session tokens are excluded from account-list DTOs and audit descriptions.

The host maps only `https://manger.adwas.example/` to bundled resources. External navigation/resources, popups, downloads and host objects are blocked. The message dispatcher validates origin, request correlation, protocol version, exact payload shape and server-held session authority. Failed or rolled-back operations never become successful responses.

Registry screens support adding personnel, effective-dated identity updates, soft archival, site and status catalogs, and roster-linked account management. Mutations are session-checked, transactional and audited; destructive site changes require a current impact preview. Historical snapshots remain unchanged.

Attendance preparation, movements, reports, Excel workflows, scheduled operations and backup/restore screens are not yet complete. Installer and Portable distribution remain gated on complete functionality and real Windows acceptance.

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
