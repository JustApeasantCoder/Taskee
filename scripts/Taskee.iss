#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef OutputDirPath
  #error OutputDirPath is required
#endif

[Setup]
AppId={{85458756-5947-4781-A37F-F4F650F2E4ED}
AppName=Taskee
AppVersion={#AppVersion}
AppPublisher=JustApeasantCoder
AppPublisherURL=https://github.com/JustApeasantCoder/Taskee
AppSupportURL=https://github.com/JustApeasantCoder/Taskee/issues
AppUpdatesURL=https://github.com/JustApeasantCoder/Taskee/releases
DefaultDirName={localappdata}\Programs\Taskee
DefaultGroupName=Taskee
DisableProgramGroupPage=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputDir={#OutputDirPath}
OutputBaseFilename=Taskee-{#AppVersion}-win-x64-setup
SetupIconFile=..\app\Taskee.App\Taskee.ico
UninstallDisplayIcon={app}\Taskee.exe
LicenseFile={#PackageDir}\LICENSE
InfoBeforeFile=installer-info.txt
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=Taskee.exe,TaskeeBridge.exe,Taskee.dll,Taskee.Core.dll
RestartApplications=no

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Excludes: "runtime\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Taskee"; Filename: "{app}\Taskee.exe"
Name: "{group}\Taskee User Guide"; Filename: "{app}\README.txt"
Name: "{autodesktop}\Taskee"; Filename: "{app}\Taskee.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Taskee.exe"; Description: "Launch Taskee"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Taskee', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\Taskee.exe') + '" --tray') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Taskee');
  end;
end;
