@echo off
REM Build PerfMonitor as a portable Windows executable.
REM Default output: self-contained folder at publish\ (AV-friendly).
REM For other variants (framework-dependent, single-file), use build.ps1.

pushd "%~dp0"

echo Restoring...
dotnet restore
if errorlevel 1 goto :fail

echo.
echo Running tests...
dotnet test --configuration Release --verbosity minimal
if errorlevel 1 goto :fail

echo.
echo Publishing self-contained folder (AV-friendly)...
dotnet publish src\PerfMonitor.App -c Release -r win-x64 --self-contained -o publish
if errorlevel 1 goto :fail

echo.
echo Built publish\PerfMonitor.exe (see folder for dependencies)
popd
exit /b 0

:fail
echo.
echo BUILD FAILED
popd
exit /b 1
