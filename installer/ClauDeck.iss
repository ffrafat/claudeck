; ClauDeck for Windows: installer and uninstaller.
; Built by scripts\release.ps1, which passes the version and where the published app
; and the drawn wizard artwork are:
;   ISCC /DAppVersion=1.3.0 /DSourceDir=..\artifacts\publish /DBrandingDir=..\artifacts\branding ClauDeck.iss

#ifndef AppVersion
  #error Build with scripts\release.ps1, which supplies AppVersion, SourceDir and BrandingDir.
#endif

#define AppName "ClauDeck"
#define AppExe "ClauDeck.exe"
#define Publisher "Faisal F Rafat"
#define Site "https://ffrafat.github.io/claudeck/"
#define Repo "https://github.com/ffrafat/claudeck"

[Setup]
; Never change this: it is how Windows knows a new version is the same app.
AppId={{8C1F4E7B-3A2D-4F6B-9E51-C0DEC1A0D2E7}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppPublisherURL={#Site}
AppSupportURL={#Repo}/issues
AppUpdatesURL={#Repo}/releases
AppCopyright=© 2026 {#Publisher}
AppComments=Multiple Claude Desktops, in parallel
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} for Windows Setup
VersionInfoProductName={#AppName} for Windows

; Per user, so no administrator prompt: everything ClauDeck does is per user anyway.
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
; Fixed rather than chosen: Setup empties this folder before installing (see InstallDelete),
; which must never be a folder somebody picked that holds anything else.
DisableDirPage=yes
DisableReadyPage=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

WizardStyle=modern
WizardSizePercent=110
WizardImageFile={#BrandingDir}\wizard-164.bmp,{#BrandingDir}\wizard-246.bmp,{#BrandingDir}\wizard-328.bmp,{#BrandingDir}\wizard-410.bmp
WizardSmallImageFile={#BrandingDir}\small-55.bmp,{#BrandingDir}\small-83.bmp,{#BrandingDir}\small-110.bmp,{#BrandingDir}\small-138.bmp
WizardImageStretch=no
SetupIconFile=..\src\ClauDeck\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
ShowLanguageDialog=no

OutputDir=..\artifacts
OutputBaseFilename=ClauDeck-Setup-{#AppVersion}-x64
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
SetupWindowTitle=ClauDeck for Windows Setup
WelcomeLabel1=Welcome to ClauDeck for Windows
WelcomeLabel2=Run several Claude Desktop accounts side by side, each signed in for good, with live usage limits for every one of them.%n%nThis installs ClauDeck {#AppVersion} for your Windows account only. No administrator rights are needed, and your Claude app is left exactly as it is.
FinishedHeadingLabel=ClauDeck is ready
FinishedLabelNoIcons=ClauDeck is installed.
FinishedLabel=ClauDeck is installed. Find it in the Start menu.%n%nYour main Claude account shows up straight away. Add more from Add account in ClauDeck's sidebar.
ClickFinish=Click Finish to close Setup.
ConfirmUninstall=Remove ClauDeck from this PC?%n%nYour Claude app and your main Claude account are not touched.
UninstalledAll=ClauDeck has been removed. Thanks for trying it.

[Tasks]
Name: "desktopicon"; Description: "Put a ClauDeck shortcut on the desktop"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start ClauDeck with Windows, in the notification area"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
; A clean folder each time, so files from an older version are never loaded beside the new one.
Type: filesandordirs; Name: "{app}\*"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Multiple Claude Desktops, in parallel"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Multiple Claude Desktops, in parallel"; Tasks: desktopicon
Name: "{userstartup}\{#AppName}"; Filename: "{app}\{#AppExe}"; Parameters: "--tray"; Comment: "ClauDeck in the notification area"; Tasks: startup

[Run]
; Moves shortcuts made by earlier versions, under earlier names, over to this one.
Filename: "{app}\{#AppExe}"; Parameters: "--migrate"; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "Open ClauDeck now"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Takes away the shortcuts ClauDeck made for each account, which Setup did not make
; and so cannot know about; and the accounts' data, if the person chose that.
Filename: "{app}\{#AppExe}"; Parameters: "--uninstall {code:PurgeArgument}"; Flags: runhidden waituntilterminated; RunOnceId: "ClauDeckCleanup"

[Code]
var
  PurgeData: Boolean;

// The launcher may be sitting in the notification area. Only ClauDeck itself is
// stopped: the Claude windows it started are separate processes and stay open.
procedure StopClauDeck();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ClauDeck.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ClawDeck.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM ClaudeAccounts.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopClauDeck();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopClauDeck();
  // Kept unless asked, so reinstalling later brings every account back signed in.
  PurgeData := SuppressibleMsgBox(
    'Also delete the accounts you added in ClauDeck?' + #13#10#13#10 +
    'Yes deletes their Claude sign-ins and chats from this PC.' + #13#10 +
    'No keeps them, so reinstalling ClauDeck brings every account back signed in.' + #13#10#13#10 +
    'Your main Claude account is never touched either way.',
    mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
  Result := True;
end;

function PurgeArgument(Param: String): String;
begin
  if PurgeData then Result := '--purge' else Result := '';
end;
