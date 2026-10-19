# EXTERNAL VERIFICATION ONLY. Run in a different verification repository/workspace.
# Requires Windows PowerShell 7 (pwsh), Git and .NET SDK 10; never uses a real personnel database.
param(
    [Parameter(Mandatory=$true)][string]$RepositoryRoot,
    [Parameter(Mandatory=$true)][string]$ResultsDirectory,
    [string]$ExpectedCommit = '200397747641b9553a733d9ba432a2d5b75f8ded'
)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$results = [System.IO.Path]::GetFullPath($ResultsDirectory)
[System.IO.Directory]::CreateDirectory($results) | Out-Null

try {
    $repo = (Resolve-Path $RepositoryRoot).Path
    $actual = (& git -C $repo rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Repository must be an actual git checkout.' }
    if ($actual -ne $ExpectedCommit) { throw "Commit mismatch. Expected $ExpectedCommit, got $actual" }
    Set-Content -Path (Join-Path $results 'checked_commit.txt') -Value $actual
    $env:AlTamamRepoRoot = $repo
    $env:ALTAMAM_RESULTS_JSON = Join-Path $results 'csharp_results.json'
    $runner = Join-Path $dir 'CSharpRunner/AlTamam.ExternalChecks.csproj'
    $solution = Join-Path $repo 'AlTamam.slnx'

    Write-Output "Checked commit: $actual"
    & dotnet --info *>&1 | Out-File -FilePath (Join-Path $results 'dotnet_info.txt') -Encoding utf8
    if ($LASTEXITCODE -ne 0) { throw 'dotnet --info failed' }
    & dotnet restore $solution *>&1 | Tee-Object -FilePath (Join-Path $results 'restore.log')
    if ($LASTEXITCODE -ne 0) { throw 'Production restore failed' }
    & dotnet build $solution -c Release --no-restore -warnaserror *>&1 | Tee-Object -FilePath (Join-Path $results 'production_build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Production build failed' }
    & dotnet run --project $runner -c Release *>&1 | Tee-Object -FilePath (Join-Path $results 'external_checks.log')
    if ($LASTEXITCODE -ne 0) { throw 'External C# runtime verification failed' }
    Write-Output 'All executed external verification checks reported PASS.'
} catch {
    $err = $_.Exception.Message
    Set-Content -Path (Join-Path $results 'FAILED.txt') -Value $err
    Write-Error $err
    exit 1
}
