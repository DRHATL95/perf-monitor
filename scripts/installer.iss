; PerfMonitor Inno Setup installer script.
; Compile with: iscc.exe /DMyAppVersion=0.1.3 scripts\installer.iss
; Output: <repo root>\PerfMonitor-v<ver>-setup.exe

#define MyAppName "PerfMonitor"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
; AppId is a stable GUID identifying this product across versions.
; DO NOT change it — that would strand existing installs as side-by-side
; installations instead of upgrading in place.
AppId={{F7C8A2B4-1D3E-4F5A-9C8B-2E5F8A3D9C1B}

AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=DRHATL95
AppPublisherURL=https://github.com/DRHATL95/perf-monitor
AppSupportURL=https://github.com/DRHATL95/perf-monitor/issues
AppUpdatesURL=https://github.com/DRHATL95/perf-monitor/releases
VersionInfoVersion={#MyAppVersion}

; Per-user install — no UAC elevation needed, follows the VS Code /
; GitHub Desktop pattern. Users can still point at Program Files
; during the install wizard if they prefer.
DefaultDirName={localappdata}\Programs\{#MyAppName}
DefaultGroupName={#MyAppName}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\PerfMonitor.exe

OutputDir=..\
OutputBaseFilename=PerfMonitor-v{#MyAppVersion}-setup
Compression=lzma2/max
SolidCompression=yes

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
MinVersion=10.0.18362

WizardStyle=modern
DisableProgramGroupPage=yes
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startupicon";  Description: "Launch PerfMonitor when Windows starts"; GroupDescription: "Launch behavior:"

[Files]
; Pull every file produced by `dotnet publish -o publish` — the launcher
; exe plus all runtime DLLs and assets. The path is relative to this .iss
; file, so it resolves to <repo root>\publish.
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\PerfMonitor.exe"
Name: "{autodesktop}\{#MyAppName}";  Filename: "{app}\PerfMonitor.exe"; Tasks: desktopicon
Name: "{userstartup}\{#MyAppName}";  Filename: "{app}\PerfMonitor.exe"; Tasks: startupicon

[Run]
Filename: "{app}\PerfMonitor.exe"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
