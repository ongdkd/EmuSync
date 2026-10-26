#define AppVersion "1.4.1"

[Setup]
AppId={{B57009C6-6921-420A-A67A-B6C9844129CA}
AppName=EmuSync
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\EmuSync
DefaultGroupName=EmuSync
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=EmuSync-Setup-{#AppVersion}-x64
SetupIconFile=..\dist\Windows\emusync.ico
UninstallDisplayIcon={app}\EmuSync.App.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
RestartApplications=no

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "..\dist\Windows\*"; DestDir: "{app}"; Excludes: "*.WebView2,*.WebView2\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\EmuSync"; Filename: "{app}\EmuSync.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\EmuSync"; Filename: "{app}\EmuSync.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\EmuSync.App.exe"; Description: "Open EmuSync"; Flags: nowait postinstall skipifsilent
