; Inno Setup script for Easy Windows & Menus.
; Built by tools\build_installer.ps1, which publishes the self-contained exe
; first and then invokes ISCC.exe with /DMyAppVersion=<VERSION file content>.
; Not meant to be compiled by hand with a stale MyAppVersion default, but one
; is still defined below so opening this file in the Inno Setup IDE works.

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0.0"
#endif

#define MyAppName "Easy Windows & Menus"
#define MyAppExeName "EasyWinMenu.App.exe"
#define MyAppPublisher "Alexandre Chagas Sousa"
#define MyPublishDir "..\bin\Release\net10.0-windows\win-x64\publish"

[Setup]
; Fixed GUID: identifies upgrades of the same product across versions. Never regenerate this.
AppId={{C9832DF6-615D-457C-A598-869C2033FFEC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\EasyWinMenu
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install under %LocalAppData% — no admin prompt, matches how the
; app already keeps its own settings under %AppData%\EasyWinMenu.
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=EasyWinMenuSetup-{#MyAppVersion}
OutputDir=..\..\..\dist
Compression=lzma2
SolidCompression=yes
SetupIconFile=..\Assets\EasyWin.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The self-contained single-file publish output: the exe plus whatever satellite
; files single-file publish still leaves next to it (there are normally none).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

; Settings and pinned groups live under %AppData%\EasyWinMenu, deliberately
; untouched here — uninstalling removes the program, not the user's groups.
