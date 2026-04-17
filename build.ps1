#Requires -Version 5.1
<#
.SYNOPSIS
  Build PerfMonitor as a portable Windows executable.

.DESCRIPTION
  Default output: a self-contained FOLDER at ./publish/ with PerfMonitor.exe
  plus the .NET runtime as separate DLLs. The exe is a normal .NET launcher
  that antivirus does not flag.

  Use -SingleFile to produce a single ~160 MB self-extracting exe. This is
  smaller to distribute but some antivirus engines heuristically flag the
  self-extracting bundle pattern. Not recommended unless you can code-sign it.

  Use -FrameworkDependent for a tiny (~2 MB) exe that requires .NET 8
  Desktop Runtime installed on the target machine. AV-friendly and small.

.EXAMPLE
  ./build.ps1                         # default: multi-file self-contained (AV-safe)
  ./build.ps1 -FrameworkDependent     # tiny exe, needs .NET 8 runtime
  ./build.ps1 -SingleFile             # one-file bundle (may trigger AV)
  ./build.ps1 -SkipTests -Output dist
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Output = 'publish',
    [switch]$SkipTests,
    [switch]$SingleFile,
    [switch]$FrameworkDependent
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

    $publishArgs = @(
        'publish', 'src/PerfMonitor.App',
        '-c', $Configuration,
        '-r', 'win-x64',
        '-o', $Output
    )

    if ($FrameworkDependent) {
        Write-Host "`nPublishing framework-dependent exe..." -ForegroundColor Cyan
        $publishArgs += '--no-self-contained'
        $publishArgs += '-p:PublishSingleFile=true'
    }
    elseif ($SingleFile) {
        Write-Host "`nPublishing single-file self-contained exe (may trigger antivirus)..." -ForegroundColor Yellow
        $publishArgs += '--self-contained'
        $publishArgs += '-p:PublishSingleFile=true'
        $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
    }
    else {
        Write-Host "`nPublishing self-contained folder (AV-friendly)..." -ForegroundColor Cyan
        $publishArgs += '--self-contained'
    }

    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

    $exe = Join-Path $Output 'PerfMonitor.exe'
    if (Test-Path $exe) {
        $exeSizeMB = [math]::Round((Get-Item $exe).Length / 1MB, 2)
        $folderSizeMB = [math]::Round(((Get-ChildItem $Output -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 1)
        Write-Host ""
        Write-Host "[OK] Built $exe" -ForegroundColor Green
        Write-Host "     exe: $exeSizeMB MB, total folder: $folderSizeMB MB"
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
