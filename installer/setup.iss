[Setup]
AppId={{B8E7C8A0-1234-5678-9ABC-DEF012345678}
AppName=ClutterFlock
AppVersion=1.0.0
AppVerName=ClutterFlock 1.0.0
AppPublisher=ClutterFlock Team
AppPublisherURL=https://github.com/Borschtsch/ClutterFlock
AppSupportURL=https://github.com/Borschtsch/ClutterFlock/issues
AppUpdatesURL=https://github.com/Borschtsch/ClutterFlock/releases
DefaultDirName={autopf}\ClutterFlock
DisableProgramGroupPage=yes
OutputDir=output
OutputBaseFilename=ClutterFlock-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\ClutterFlock.exe

#ifndef SourceDir
#define SourceDir "files"
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "quicklaunchicon"; Description: "{cm:CreateQuickLaunchIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked; OnlyBelowVersion: 6.1

[Files]
Source: "{#SourceDir}\ClutterFlock.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\ClutterFlock"; Filename: "{app}\ClutterFlock.exe"; Comment: "Duplicate folder analysis tool"
Name: "{autodesktop}\ClutterFlock"; Filename: "{app}\ClutterFlock.exe"; Tasks: desktopicon; Comment: "Duplicate folder analysis tool"
Name: "{userappdata}\Microsoft\Internet Explorer\Quick Launch\ClutterFlock"; Filename: "{app}\ClutterFlock.exe"; Tasks: quicklaunchicon

[Run]
Filename: "{app}\ClutterFlock.exe"; Description: "{cm:LaunchProgram,ClutterFlock}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"