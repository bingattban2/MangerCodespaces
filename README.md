# Phase 00 / Step 01 — External verification workspace

This folder is **NOT production source**. It must not be copied to the public application repository or packaged into the Windows EXE/installer.

Inputs: original `MangerCodespaces-main.zip`, `AlTamam_Phase00_Step01_Traceability.csv`, and a separately checked out git revision `200397747641b9553a733d9ba432a2d5b75f8ded`.

## Local structural and plain-SQLite smoke, runnable on any Python 3.10+ machine

```bash
python verify_baseline.py --source /path/to/MangerCodespaces-main.zip --trace /path/to/AlTamam_Phase00_Step01_Traceability.csv --output verification_result.json
```

This validates actual SQL inserts/negatives/restarts in an isolated ordinary SQLite database, plus real source and traceability files. It does **not** prove SQLCipher, C# services or UI functionality.

## Windows .NET 10 / native SQLCipher verification, external-only

In Windows PowerShell, after checking out the exact pinned Git commit into a temporary folder:

```powershell
.\run_windows.ps1 -RepositoryRoot 'C:\Path\To\MangerCodespaces'
```

The script refuses a different commit and restores/builds the actual four C# source projects with warnings as errors. The external console runner then evaluates real Domain/Application C# policies and attempts actual SQLCipher V001 initialization, database verification, wrong-key rejection, rollback, persistence/restart. It uses **only fictional test identifiers** in an OS temp folder and deletes them on exit. It does not execute any code in GitHub Actions by itself and is **not yet executed/verified** in this local environment (no `dotnet` executable available). Compilation or execution failures must remain visible and block acceptance.

Caution: These tests still **do not** prove implemented end-to-end business APIs or WebView2 UI; those are owned by later phases. Full strict Step 00.01 gate remains BLOCKED until appropriate actual Windows runtime evidence is provided and accepted. Phase 00's one comprehensive suite must not be run before Phase 00 steps 01–03 and policy decisions are complete.
