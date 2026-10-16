; Only the packaging shell changes. Inno Setup supplies all wizard pages and controls.
#ifndef ProjectRoot
  #error ProjectRoot must be supplied by BuildClassicInstaller.ps1
#endif

[Setup]
AppId=PS_Converter
AppName=PS_Converter
AppVersion=1.0
AppPublisher=jotadev27
AppPublisherURL=https://github.com/jotadev27/PS_Converter
DefaultDirName={localappdata}\PS_Converter
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=yes
DirExistsWarning=no
UsePreviousAppDir=no
PrivilegesRequired=lowest
WizardStyle=classic
WizardResizable=no
WizardImageFile={#StageRoot}\wizard.bmp
WizardSmallImageFile={#StageRoot}\wizard-small.bmp
WizardImageStretch=yes
SetupIconFile={#ProjectRoot}\assets\app.ico
LicenseFile={#StageRoot}\license-terms.txt
OutputDir={#StageRoot}
OutputBaseFilename=PS_Converter-v1.0-setup
Compression=lzma2
SolidCompression=yes
Uninstallable=no
CreateUninstallRegKey=no
CloseApplications=no
RestartApplications=no
SetupLogging=no
VersionInfoVersion=1.0.0.0
VersionInfoProductName=PS_Converter
VersionInfoDescription=PS_Converter Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
#include StageRoot + "\payload.iss"

[Run]
Filename: "{app}\PS_converter.exe"; Description: "Launch PS_Converter"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
function DestinationError: String;
var
  Entry: TFindRec;
begin
  Result := '';
  { Preserve the previous installer's empty-folder policy, including silent installs. }
  if FindFirst(AddBackslash(ExpandConstant('{app}')) + '*', Entry) then
  begin
    try
      repeat
        if (Entry.Name <> '.') and (Entry.Name <> '..') then
        begin
          Result := 'Choose an empty folder. Existing files and settings will not be overwritten.';
          Break;
        end;
      until not FindNext(Entry);
    finally
      FindClose(Entry);
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Error: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    Error := DestinationError;
    Result := Error = '';
    if not Result then
      SuppressibleMsgBox(Error, mbError, MB_OK, IDOK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := DestinationError;
end;
