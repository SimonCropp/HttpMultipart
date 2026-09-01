<#
.SYNOPSIS
    Packs HttpMultipart and builds the Consume projects against the real .nupkg.

.DESCRIPTION
    The solution build never exercises the nuspec: Tests links the sources directly. This does, and it
    is the only thing that catches a file missing from the <files> allowlist, a broken contentFiles
    path, or a global using that does not reach a consumer.

    Restores into a private packages folder so the global cache cannot serve a stale copy of the same
    version number.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    # Passed on to pack, so a release build does not leave a second, differently versioned package in
    # nugets/ for the push step to find.
    [string] $Version
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$packages = Join-Path $root 'consume-packages'

Write-Host '==> pack' -ForegroundColor Cyan
$packArgs = @((Join-Path $root 'HttpMultipart'), '-c', $Configuration)
if ($Version) { $packArgs += "-p:Version=$Version" }
dotnet pack @packArgs
if ($LASTEXITCODE -ne 0) { throw 'pack failed' }

# Any earlier extraction of the same version would otherwise be reused instead of what pack just built.
if (Test-Path $packages) { Remove-Item $packages -Recurse -Force }

$failed = @()

foreach ($name in 'Consume', 'ConsumeNoImplicitUsings') {
    Write-Host "==> $name" -ForegroundColor Cyan
    dotnet build (Join-Path $root $name) -c $Configuration --packages $packages
    if ($LASTEXITCODE -ne 0) { $failed += $name }
}

# This one must fail, and for the stated reason.
Write-Host '==> ConsumeOldTfm (expected to fail)' -ForegroundColor Cyan
$output = dotnet build (Join-Path $root 'ConsumeOldTfm') -c $Configuration --packages $packages 2>&1 |
    Out-String
if ($LASTEXITCODE -eq 0) {
    $failed += 'ConsumeOldTfm built on net9.0; the target framework guard did not fire'
}
elseif ($output -notmatch 'HttpMultipart requires net10\.0 or later') {
    Write-Host $output
    $failed += 'ConsumeOldTfm failed, but not on the target framework guard'
}
else {
    Write-Host '    failed on the target framework guard, as intended' -ForegroundColor Green
}

if ($failed.Count -gt 0) {
    $failed | ForEach-Object { Write-Host "FAILED: $_" -ForegroundColor Red }
    exit 1
}

Write-Host 'package verified' -ForegroundColor Green
