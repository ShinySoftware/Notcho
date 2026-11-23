; The Windows installer. Built by the release workflow, once per architecture:
;
;   iscc /DAppVersion=1.2.0 /DFileVersion=1.2.0 /DArch=x64 /DSourceDir=<publish dir> /DOutputDir=<dir> Notcho.iss
;
; Per-user, into %LOCALAPPDATA%\Programs\Notcho, so nobody needs to be an administrator to install
; it, and because the first launch turns on open at login by writing this exe's path into the HKCU Run
; key: the path has to stay put across upgrades.

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef SourceDir
  #error Pass /DSourceDir=<publish output>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

#define AppExe "Notcho.Windows.exe"

[Setup]
; Never change this: it is how an upgrade finds the copy it is replacing.
AppId={{04EE38DC-7C1D-4449-9C64-B366E05CAE22}
AppName=Notcho
AppVersion={#AppVersion}
AppVerName=Notcho {#AppVersion}
AppPublisher=ShinySoft Technologies
AppPublisherURL=https://github.com/ShinySoftware/Notcho
AppSupportURL=https://github.com/ShinySoftware/Notcho/issues
AppUpdatesURL=https://github.com/ShinySoftware/Notcho/releases
VersionInfoVersion={#FileVersion}
PrivilegesRequired=lowest
DefaultDirName={autopf}\Notcho
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName=Notcho
OutputDir={#OutputDir}
OutputBaseFilename=Notcho-{#AppVersion}-windows-{#Arch}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The notch keeps running with no window of its own to close (it quits from Settings), so a polite
; Restart Manager close may not take; force it rather than leave the old files held open.
CloseApplications=force
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Notcho"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\Notcho"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

; Upgrading from when the app was called Shiny Notch: same AppId, so this installs over it, but its exe had
; another name. Remove it so it can't be started again, along with its Start menu and desktop shortcuts.
[InstallDelete]
Type: files; Name: "{app}\ShinyNotch.Windows.exe"
Type: files; Name: "{app}\ShinyNotch.Windows.dll"
Type: files; Name: "{autoprograms}\Shiny Notch.lnk"
Type: files; Name: "{autodesktop}\Shiny Notch.lnk"

[Registry]
; Shiny Notch's open at login entry pointed at the exe removed above.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "net.shiny.notch"; ValueType: none; Flags: deletevalue
; Not written here - the app writes it (Shiny.Core's startup installer, named by the Identifier in
; NotchoHosting) on first launch and from Settings. Only removed on uninstall, so an uninstalled app
; is not started at the next sign-in.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "net.shiny.notcho"; ValueType: none; Flags: uninsdeletevalue

[Run]
; skipifsilent: there's no in-app updater on Windows, so a silent install is someone deploying it, who
; decides when it starts. Add the launch back for silent runs if the app ever updates itself this way.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,Notcho}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; The notch is always running, which would hold its files open.
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "StopNotcho"

; Settings, notes and photos in the user's profile are left alone: an uninstall to reinstall should not
; take them with it.

[Code]
// A running Shiny Notch holds its exe open, and InstallDelete can't remove an exe that's running.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ShinyNotch.Windows.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
