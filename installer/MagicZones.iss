; MagicZones - per-user installer (no admin rights), Inno Setup 6.
; Installs to %LOCALAPPDATA%\Programs\MagicZones, optional autostart via HKCU\...\Run.
; Build: first `.\build.ps1` (Release), then:
;   "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\MagicZones.iss
; (build.ps1 -Installer does both when ISCC is installed.)

#define AppExe "..\bin\Release\MagicZones.exe"
#if !FileExists(AppExe)
  #error "Compila prima in Release: bin\Release\MagicZones.exe non trovato"
#endif
; Keep the installer version in sync with the exe: ProductVersion = <Version> in MagicZones.csproj.
#define AppVersion GetStringFileInfo(AppExe, "ProductVersion")

[Setup]
AppId={{B7D3C1E4-5A2F-4C8B-9E61-0F4D2A7C9B15}
AppName=MagicZones
AppVersion={#AppVersion}
AppVerName=MagicZones {#AppVersion}
AppPublisher=lorenzo
VersionInfoVersion={#AppVersion}
VersionInfoProductName=MagicZones
VersionInfoDescription=Installazione di MagicZones
; Per-user install: {autopf} resolves to %LOCALAPPDATA%\Programs, nothing needs elevation.
PrivilegesRequired=lowest
DefaultDirName={autopf}\MagicZones
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\MagicZones.exe
SetupIconFile=..\assets\MagicZones.ico
OutputDir=..\dist
OutputBaseFilename=MagicZones-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Ask the running app to close before replacing/removing its files (same mutex as Program.cs).
AppMutex=Local\MagicZones.SingleInstance
CloseApplications=yes
; To sign later (recommended against false positives), define a SignTool in the IDE and uncomment:
; SignTool=mysigntool
; SignedUninstaller=yes

[Languages]
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "autostart"; Description: "Avvia MagicZones con Windows"; GroupDescription: "Opzioni:"

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\MagicZones.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MagicZones"; Filename: "{app}\MagicZones.exe"
Name: "{autoprograms}\Disinstalla MagicZones"; Filename: "{uninstallexe}"

[Registry]
; Autostart = HKCU Run value pointing at the installed exe. No scheduled task, no admin.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MagicZones"; \
  ValueData: """{app}\MagicZones.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MagicZones.exe"; Description: "Avvia MagicZones adesso"; Flags: nowait postinstall skipifsilent

[Code]
// Autostart may also have been switched on from the app's tray menu: remove it on uninstall
// in any case. User settings in %APPDATA%\MagicZones are left alone.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MagicZones');
end;
