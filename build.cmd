@echo off
REM Build PerfMonitor as a single-file self-contained Windows executable.
REM Output: publish\PerfMonitor.exe

pushd "%~dp0"

echo Restoring...
dotnet restore
if errorlevel 1 goto :fail

echo.
echo Running tests...
dotnet test --configuration Release --verbosity minimal
if errorlevel 1 goto :fail

echo.
echo Publishing single-file exe...
dotnet publish src\PerfMonitor.App ^
    -c Release ^
    -r win-x64 ^
    --self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o publish
if errorlevel 1 goto :fail

echo.
echo Built publish\PerfMonitor.exe
popd
exit /b 0

:fail
echo.
echo BUILD FAILED
popd
exit /b 1
