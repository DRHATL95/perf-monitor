#Requires -Version 5.1
<#
.SYNOPSIS
  Build PerfMonitor as a single-file self-contained Windows executable.

.DESCRIPTION
  Publishes src/PerfMonitor.App into ./publish/PerfMonitor.exe.
  No .NET runtime install required on the target machine.

.EXAMPLE
  ./build.ps1
  ./build.ps1 -Configuration Debug
  ./build.ps1 -Output ./dist
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Output = 'publish',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

Push-Location $PSScriptRoot
try {
    Write-Host "Restoring..." -ForegroundColor Cyan
    dotnet restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed" }

    if (-not $SkipTests) {
        Write-Host "`nRunning tests..." -ForegroundColor Cyan
        dotnet test --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
    }

    Write-Host "`nPublishing single-file exe ($Configuration)..." -ForegroundColor Cyan
    dotnet publish src/PerfMonitor.App `
        -c $Configuration `
        -r win-x64 `
        --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $Output
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $exe = Join-Path $Output 'PerfMonitor.exe'
    if (Test-Path $exe) {
        $sizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 1)
        Write-Host ""
        Write-Host "[OK] Built $exe ($sizeMB MB)" -ForegroundColor Green
    } else {
        throw "Expected $exe was not produced."
    }
}
catch {
    Write-Host ""
    Write-Host "[FAIL] $_" -ForegroundColor Red
    exit 1
}
finally {
    Pop-Location
}
