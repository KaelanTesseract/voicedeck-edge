; Voicedeck Edge – Windows installer (Inno Setup 6)
; Build: iscc /DAppVersion=0.9.0 installer\VoicedeckEdge.iss
; Expects artifacts\hub\VoicedeckEdgeHub.exe (dotnet publish -c Release) and artifacts\VoicedeckEdge.icuewidget.
; No admin rights: installs per user to %LOCALAPPDATA%\Programs\VoicedeckEdge.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif

[Setup]
AppId={{6F3B2C1E-8D4A-4E5B-9C7D-2A1B3C4D5E6F}
AppName=Voicedeck Edge
AppVersion={#AppVersion}
AppVerName=Voicedeck Edge {#AppVersion}
AppPublisher=KaelanTesseract
AppPublisherURL=https://github.com/KaelanTesseract/voicedeck-edge
AppSupportURL=https://github.com/KaelanTesseract/voicedeck-edge/issues
DefaultDirName={localappdata}\Programs\VoicedeckEdge
PrivilegesRequired=lowest
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=VoicedeckEdge-Setup-{#AppVersion}
SetupIconFile=..\hub\Resources\app.ico
UninstallDisplayIcon={app}\VoicedeckEdgeHub.exe
UninstallDisplayName=Voicedeck Edge
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
de.AutostartTask=Voicedeck Edge Hub mit Windows starten (empfohlen)
en.AutostartTask=Start Voicedeck Edge Hub with Windows (recommended)
de.LaunchHub=Voicedeck Edge Hub jetzt starten und einrichten
en.LaunchHub=Start Voicedeck Edge Hub now and set it up
de.DeleteDataQuestion=Auch alle Voicedeck-Edge-Daten löschen?%n%nDas meldet Voicedeck Edge bei Discord ab und entfernt Einstellungen, gekoppelte Widgets und Logs. Bei „Nein“ bleiben sie für eine spätere Neuinstallation erhalten.
en.DeleteDataQuestion=Also delete all Voicedeck Edge data?%n%nThis signs Voicedeck Edge out of Discord and removes settings, paired widgets and logs. Choose "No" to keep them for a later reinstall.

[Tasks]
Name: "autostart"; Description: "{cm:AutostartTask}"

[Files]
Source: "..\artifacts\hub\VoicedeckEdgeHub.exe"; DestDir: "{app}"; Flags: ignoreversion
; The setup assistant offers this file to iCUE ("Add widget to iCUE").
Source: "..\artifacts\VoicedeckEdge.icuewidget"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Voicedeck Edge Hub"; Filename: "{app}\VoicedeckEdgeHub.exe"

[Registry]
; Same value name the hub's tray menu ("Start with Windows") uses, so both stay in sync.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VoicedeckEdgeHub"; ValueData: """{app}\VoicedeckEdgeHub.exe"" --autostart"; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\VoicedeckEdgeHub.exe"; Description: "{cm:LaunchHub}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Folders the hub creates; only removed when empty (data is deleted on request, see below).
Type: dirifempty; Name: "{localappdata}\VoicedeckEdge\logs"
Type: dirifempty; Name: "{localappdata}\VoicedeckEdge"
Type: dirifempty; Name: "{userappdata}\VoicedeckEdge"

[Code]
procedure StopHub;
var
  ResultCode: Integer;
begin
  { The hub is a tray app without a window, so Restart Manager cannot close it politely. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM VoicedeckEdgeHub.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopHub;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopHub;
    if (not UninstallSilent) and
       (MsgBox(CustomMessage('DeleteDataQuestion'), mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES) then
      Exec(ExpandConstant('{app}\VoicedeckEdgeHub.exe'), '--delete-all-data', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    { Autostart may also have been switched on later from the tray menu. }
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'VoicedeckEdgeHub');
  end;
end;
