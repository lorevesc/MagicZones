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
VersionInfoDescription=MagicZones Setup
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
; Setup language follows the Windows display language (Italian, otherwise English), no dialog.
ShowLanguageDialog=no
; Ask the running app to close before replacing/removing its files (same mutex as Program.cs).
AppMutex=Local\MagicZones.SingleInstance
CloseApplications=yes
; To sign later (recommended against false positives), define a SignTool in the IDE and uncomment:
; SignTool=mysigntool
; SignedUninstaller=yes

[Languages]
; First entry = fallback when the Windows language is neither of these.
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "it"; MessagesFile: "compiler:Languages\Italian.isl"

[CustomMessages]
en.AutostartTask=Start MagicZones with Windows
it.AutostartTask=Avvia MagicZones con Windows
en.OptionsGroup=Options:
it.OptionsGroup=Opzioni:
en.RunNow=Start MagicZones now
it.RunNow=Avvia MagicZones adesso

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:OptionsGroup}"

[Files]
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\bin\Release\MagicZones.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\MagicZones"; Filename: "{app}\MagicZones.exe"
Name: "{autoprograms}\{cm:UninstallProgram,MagicZones}"; Filename: "{uninstallexe}"

[Registry]
; Autostart = HKCU Run value pointing at the installed exe. No scheduled task, no admin.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MagicZones"; \
  ValueData: """{app}\MagicZones.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\MagicZones.exe"; Description: "{cm:RunNow}"; Flags: nowait postinstall skipifsilent

[Code]
// Autostart may also have been switched on from the app's tray menu: remove it on uninstall
// in any case. User settings in %APPDATA%\MagicZones are left alone.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MagicZones');
end;
