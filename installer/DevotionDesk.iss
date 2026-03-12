; DevotionDesk installer (Inno Setup)

#define MyAppName "DevotionDesk"
#define MyAppPublisher "Justin Conferido"
#define MyAppURL "https://github.com/Conferido47/DevotionDesk"
#define MyAppExeName "DevotionDesk.exe"

#define _EnvAppVersion GetEnv('APP_VERSION')
#define MyAppVersion (_EnvAppVersion == '' ? '0.1.0' : _EnvAppVersion)

#define _EnvPublishDir GetEnv('PUBLISH_DIR')
#define PublishDir (_EnvPublishDir == '' ? '..\\DevotionDesk\\bin\\Release\\net8.0-windows\\publish' : _EnvPublishDir)

[Setup]
AppId={{C8D73D83-7C17-4A06-8B94-24F4668B3B6A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\\{#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename={#MyAppName}-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\\{#MyAppExeName}
SetupIconFile={#SourcePath}\\..\\DevotionDesk\\Assets\\DevotionDesk.ico
WizardSmallImageFile={#SourcePath}\\assets\\DevotionDeskWizard.png

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"
Name: "{autodesktop}\\{#MyAppName}"; Filename: "{app}\\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
