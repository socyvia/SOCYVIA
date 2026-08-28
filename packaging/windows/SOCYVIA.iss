#define MyAppName "SOCYVIA"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SOCYVIA"
#define MyAppURL "https://socyvia.com"
#define MyAppExeName "SOCYVIA.exe"
#ifndef PayloadDir
  #define PayloadDir "..\..\artifacts\release-staging\win-x64"
#endif
#ifndef EngineOutputDir
  #define EngineOutputDir "..\..\artifacts\premium-installer"
#endif

[Setup]
AppId={{7A3791F3-F195-46C1-91EF-6682771461D6}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
UninstallDisplayName={#MyAppName}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={localappdata}\Programs\SOCYVIA
DefaultGroupName=SOCYVIA
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir={#EngineOutputDir}
OutputBaseFilename=SOCYVIA-1.0.0-Windows-x64-Engine
SetupIconFile=..\..\Assets\Branding\socyvia-mark.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=SOCYVIA Desktop Installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "Shortcuts:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SOCYVIA"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\SOCYVIA"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch SOCYVIA"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  UninstallKey: String;
  BrandedUninstaller: String;
begin
  if CurStep = ssPostInstall then
  begin
    UninstallKey := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{7A3791F3-F195-46C1-91EF-6682771461D6}_is1';
    BrandedUninstaller := '"' + ExpandConstant('{app}\SOCYVIA.Uninstall.exe') + '"';
    RegWriteStringValue(HKCU, UninstallKey, 'UninstallString', BrandedUninstaller);
    RegWriteStringValue(HKCU, UninstallKey, 'QuietUninstallString', BrandedUninstaller + ' /VERYSILENT');
  end;
end;
