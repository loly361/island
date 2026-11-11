; Island installer (Inno Setup 6).
; Build: installer\build-installer.ps1 (passes /DAppVersion and /DPayloadDir).
; Manual: ISCC.exe /DAppVersion=1.1.0 /DPayloadDir=<publish dir> installer\Island.iss
;
; Contract with the in-app updater:
;   Island-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART [/NOLAUNCH]
;   - closes the running Island.exe from the target folder, replaces files;
;   - in silent mode starts the new Island.exe unless /NOLAUNCH is given.

#ifndef AppVersion
  #error AppVersion is not defined. Compile with /DAppVersion=x.y.z
#endif

#ifndef PayloadDir
  #define PayloadDir AddBackslash(SourcePath) + "payload"
#endif

#define AppName "Island"
#define AppExe "Island.exe"

[Setup]
; AppId must never change: it identifies the installed copy for upgrades and uninstall.
AppId={{AC7DE9DF-D68E-4599-85A6-968CC9FA163C}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=loly361
AppPublisherURL=https://github.com/loly361/island
AppSupportURL=https://github.com/loly361/island
AppUpdatesURL=https://github.com/loly361/island/releases
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Island
DisableProgramGroupPage=yes
DisableDirPage=auto
DisableReadyPage=no
SetupIconFile=..\src\Island\Assets\Island.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
Compression=lzma2
SolidCompression=yes
OutputDir=output
OutputBaseFilename=Island-Setup
WizardStyle=modern
; Restart Manager: close the running Island that locks files in {app}.
CloseApplications=force
CloseApplicationsFilter=*.exe,*.dll
; We start the new version ourselves (see [Run]), so no RM restart.
RestartApplications=no
ShowLanguageDialog=auto

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "french";  MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
russian.DotNetMissing=Для работы Island нужен .NET 9 Desktop Runtime (x64), но он не найден.%n%nОткрыть страницу загрузки .NET 9?
russian.DotNetContinue=Продолжить установку Island без .NET 9? Программа не запустится, пока .NET 9 Desktop Runtime не установлен.
french.DotNetMissing=Island nécessite .NET 9 Desktop Runtime (x64), qui est introuvable.%n%nOuvrir la page de téléchargement de .NET 9 ?
french.DotNetContinue=Continuer l'installation d'Island sans .NET 9 ? L'application ne démarrera pas tant que .NET 9 Desktop Runtime n'est pas installé.
english.DotNetMissing=Island needs .NET 9 Desktop Runtime (x64), but it was not found.%n%nOpen the .NET 9 download page?
english.DotNetContinue=Continue installing Island without .NET 9? The app will not start until .NET 9 Desktop Runtime is installed.
russian.LaunchIsland=Запустить Island
french.LaunchIsland=Lancer Island
english.LaunchIsland=Launch Island
russian.StartupGroup=Запуск:
french.StartupGroup=Démarrage :
english.StartupGroup=Startup:
russian.AutostartTask=Запускать вместе с Windows
french.AutostartTask=Lancer au démarrage de Windows
english.AutostartTask=Start with Windows

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "autostart"; Description: "{cm:AutostartTask}"; GroupDescription: "{cm:StartupGroup}"

[Registry]
; Same value as the app's tray menu writes (Autostart in SystemUtil.cs): the quoted exe path, no StartupApproved flag.
; Interactive install only: a silent (updater) install must not turn back on an autostart the user switched off in the tray.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Island"; ValueData: """{app}\{#AppExe}"""; Tasks: autostart; Check: AutostartAllowed

[Files]
Source: "{#PayloadDir}\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Check: IconsAllowed
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon; Check: IconsAllowed

[Run]
; Interactive install: checkbox on the last page.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchIsland}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
; Silent install (updater): start the new version automatically unless /NOLAUNCH.
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Flags: nowait; Check: ShouldAutoLaunch

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  DotNetUrl = 'https://dotnet.microsoft.com/download/dotnet/9.0';

function CmdLineHas(const Param: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Param) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function ShouldAutoLaunch: Boolean;
begin
  Result := WizardSilent and not CmdLineHas('/NOLAUNCH');
end;

function IconsAllowed: Boolean;
begin
  Result := not CmdLineHas('/NOICONS');
end;

{ The Run\Island value is written only by an interactive install; silent updates leave the user's choice alone. }
function AutostartAllowed: Boolean;
begin
  Result := not WizardSilent;
end;

function HasDesktopRuntime9: Boolean;
var
  Names: TArrayOfString;
  I: Integer;
  FindRec: TFindRec;
begin
  Result := False;
  if RegGetValueNames(HKLM64, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Names) then
    for I := 0 to GetArrayLength(Names) - 1 do
      if Copy(Names[I], 1, 2) = '9.' then
      begin
        Result := True;
        Exit;
      end;
  if FindFirst(ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\9.*'), FindRec) then
  begin
    try
      repeat
        if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if HasDesktopRuntime9 then
    Exit;
  { Silent (updater) mode: do not ask, just continue. }
  if WizardSilent then
    Exit;
  if MsgBox(CustomMessage('DotNetMissing'), mbConfirmation, MB_YESNO) = IDYES then
    ShellExec('open', DotNetUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  Result := MsgBox(CustomMessage('DotNetContinue'), mbConfirmation, MB_YESNO) = IDYES;
end;

{ Closes Island.exe processes started from exactly ExePath (other copies are left alone). }
function IslandProcessesAt(const ExePath: String; Kill: Boolean): Integer;
var
  Locator, Service, Procs, P: Variant;
  I: Integer;
  Path: String;
begin
  Result := 0;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Procs := Service.ExecQuery('SELECT ProcessId, ExecutablePath FROM Win32_Process WHERE Name = ''Island.exe''');
    for I := 0 to Procs.Count - 1 do
    begin
      P := Procs.ItemIndex(I);
      if VarIsNull(P.ExecutablePath) then
        Continue;
      Path := P.ExecutablePath;
      if CompareText(Path, ExePath) = 0 then
      begin
        Result := Result + 1;
        if Kill then
          P.Terminate();
      end;
    end;
  except
    Log('WMI process query failed: ' + GetExceptionMessage);
  end;
end;

procedure CloseIslandAt(const ExePath: String);
var
  Tries: Integer;
begin
  if IslandProcessesAt(ExePath, False) = 0 then
    Exit;
  Log('Island is running from ' + ExePath + ', terminating it');
  IslandProcessesAt(ExePath, True);
  { Wait up to 5 s for the process to exit and release its files. }
  for Tries := 1 to 25 do
  begin
    if IslandProcessesAt(ExePath, False) = 0 then
      Exit;
    Sleep(200);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  // Interactive mode relies on Restart Manager (it asks the user).
  // Silent mode (updater) also closes the copy in the install folder directly.
  if WizardSilent then
    CloseIslandAt(ExpandConstant('{app}\{#AppExe}'));
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Current: String;
begin
  { Interactive install with the autostart checkbox cleared: drop a stale Run\Island value that points to this copy. }
  if (CurStep = ssPostInstall) and (not WizardSilent) and (not WizardIsTaskSelected('autostart')) then
  begin
    if RegQueryStringValue(HKEY_CURRENT_USER, RunKey, 'Island', Current) then
      if CompareText(Current, '"' + ExpandConstant('{app}\{#AppExe}') + '"') = 0 then
        RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'Island');
  end;
end;

function InitializeUninstall: Boolean;
begin
  Result := True;
  CloseIslandAt(ExpandConstant('{app}\{#AppExe}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Value: String;
begin
  if CurUninstallStep <> usUninstall then
    Exit;
  { Remove autostart only if it points to this installed copy (written by setup or by the tray menu). }
  if RegQueryStringValue(HKCU, RunKey, 'Island', Value) then
    if Pos(Lowercase(ExpandConstant('{app}\{#AppExe}')), Lowercase(Value)) > 0 then
      RegDeleteValue(HKCU, RunKey, 'Island');
  { User data in %APPDATA%\Island is intentionally kept. }
end;
