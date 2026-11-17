; NOVA installer for Inno Setup 6.
; Place this file beside an "app" folder containing the extracted Windows build.
; This installer does NOT install .NET Desktop Runtime or WebView2 Runtime.
#define MyAppName "NOVA"
#define MyAppVersion "0.1.0"
#define MyAppExeName "Nova.App.exe"

[Setup]
AppId={{3B460A85-23A8-4C5E-A029-16F367BD4F5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=NOVA
DefaultDirName={localappdata}\Programs\NOVA
DefaultGroupName={#MyAppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=NOVA-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked

[Files]
Source: "app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Messages]
russian.WelcomeLabel2=Эта программа установит [name/ver] на ваш компьютер.%n%nПеред установкой полностью выйдите из NOVA через меню значка в трее.%n%nДля работы отдельно необходимы .NET 10 Desktop Runtime x64 и WebView2 Runtime. Установщик не устанавливает их автоматически.
english.WelcomeLabel2=This will install [name/ver] on your computer.%n%nBefore continuing, exit NOVA completely from the system tray.%n%n.NET 10 Desktop Runtime x64 and WebView2 Runtime must be installed separately. This setup does not install them.
