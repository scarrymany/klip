#define MyAppName "Клип"
#define MyAppNameEn "Klip"
#define MyAppPublisher "scarrymany"
#define MyAppURL "https://github.com/scarrymany/klip"
#define MyAppExeName "Klip.exe"

#ifndef MyAppVersion
  #define MyAppVersion "1.2.2"
#endif

#ifndef PublishDir
  #define PublishDir "..\dist\app"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

[Setup]
AppId={{6D1A8E4C-2F90-4B3A-9C17-E5D8A0B4F216}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright=Copyright (C) 2026 {#MyAppPublisher}
DefaultDirName={autopf}\Klip
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=Klip-Setup-{#MyAppVersion}
SetupIconFile=..\src\Klip\Assets\klip.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
ShowLanguageDialog=yes
UsePreviousLanguage=yes
CloseApplications=yes
RestartApplications=yes
AppMutex=Local\Klip.scarrymany.single

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\Klip.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "Klip.installed"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "Менеджер буфера обмена"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "Менеджер буфера обмена"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifnotsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\Klip"

[Code]
const
  LegacyMsiUpgradeCode = '{9C4E2B71-6A18-4F3D-8E05-B2D47C91A6F3}';
  LegacyMsiMaximumVersion = '1.2.1';
  ErrorSuccess = 0;
  ErrorNoMoreItems = 259;
  ErrorUnknownProduct = 1605;
  ErrorSuccessRebootRequired = 3010;

function MsiEnumRelatedProducts(
  UpgradeCode: string;
  Reserved: DWORD;
  ProductIndex: DWORD;
  ProductCode: string): UINT;
  external 'MsiEnumRelatedProductsW@msi.dll stdcall setuponly';

function MsiGetProductInfo(
  ProductCode: string;
  PropertyName: string;
  Value: string;
  var ValueSize: DWORD): UINT;
  external 'MsiGetProductInfoW@msi.dll stdcall setuponly';

var
  LegacyMsiRestartRequired: Boolean;
  LegacyMsiProductCodes: TStringList;

function FindLegacyMsiProducts(): String;
var
  ProductCode: string;
  ProductVersion: string;
  ProductIndex: DWORD;
  VersionSize: DWORD;
  EnumResult: UINT;
  InfoResult: UINT;
  InstalledVersion: Int64;
  SetupVersion: Int64;
  LegacyMaximumVersion: Int64;
  RawVersion: Int64;
begin
  Result := '';
  if not StrToVersion('{#MyAppVersion}', SetupVersion) then
  begin
    Result := 'Некорректная версия EXE-установщика Клип.';
    Exit;
  end;
  if not StrToVersion(LegacyMsiMaximumVersion, LegacyMaximumVersion) then
  begin
    Result := 'Некорректная максимальная версия старого MSI Клип.';
    Exit;
  end;

  if LegacyMsiProductCodes <> nil then
    LegacyMsiProductCodes.Free;
  LegacyMsiProductCodes := TStringList.Create;

  ProductIndex := 0;
  repeat
    SetLength(ProductCode, 39);
    EnumResult := MsiEnumRelatedProducts(
      LegacyMsiUpgradeCode,
      0,
      ProductIndex,
      ProductCode);
    if EnumResult = ErrorSuccess then
    begin
      SetLength(ProductCode, 38);

      VersionSize := 64;
      SetLength(ProductVersion, VersionSize);
      InfoResult := MsiGetProductInfo(
        ProductCode,
        'Version',
        ProductVersion,
        VersionSize);
      if InfoResult <> ErrorSuccess then
      begin
        Result := Format(
          'Не удалось проверить версию установленного MSI Клип (код %d).',
          [InfoResult]);
        Exit;
      end;
      SetLength(ProductVersion, VersionSize);
      RawVersion := StrToInt64Def(ProductVersion, -1);
      if RawVersion < 0 then
      begin
        Result := 'Установленная MSI-версия Клип имеет некорректный номер.';
        Exit;
      end;
      ProductVersion := Format(
        '%d.%d.%d',
        [RawVersion shr 24, (RawVersion shr 16) and $FF, RawVersion and $FFFF]);
      if not StrToVersion(ProductVersion, InstalledVersion) then
      begin
        Result := 'Не удалось распаковать версию установленного MSI Клип.';
        Exit;
      end;
      if ComparePackedVersion(InstalledVersion, SetupVersion) > 0 then
      begin
        Result := Format(
          'Уже установлена более новая MSI-версия Клип %s.',
          [ProductVersion]);
        Exit;
      end;
      if ComparePackedVersion(InstalledVersion, LegacyMaximumVersion) > 0 then
      begin
        Result := Format(
          'MSI-версию Клип %s нужно удалить через Параметры Windows перед установкой EXE.',
          [ProductVersion]);
        Exit;
      end;

      LegacyMsiProductCodes.Add(ProductCode);
      ProductIndex := ProductIndex + 1;
    end;
  until EnumResult <> ErrorSuccess;

  if EnumResult <> ErrorNoMoreItems then
  begin
    Result := Format(
      'Не удалось найти старые MSI-версии Клип (код %d).',
      [EnumResult]);
  end;
end;

function RemoveLegacyMsiProducts(): String;
var
  I: Integer;
  ExitCode: Integer;
begin
  Result := '';
  if LegacyMsiProductCodes = nil then
    Exit;

  for I := 0 to LegacyMsiProductCodes.Count - 1 do
  begin
    if not Exec(
      ExpandConstant('{sys}\msiexec.exe'),
      '/x "' + LegacyMsiProductCodes[I] + '" /qn /norestart',
      '',
      SW_HIDE,
      ewWaitUntilTerminated,
      ExitCode) then
    begin
      Result := 'Не удалось запустить удаление старой MSI-версии Клип.';
      Exit;
    end;

    if (ExitCode <> ErrorSuccess) and
       (ExitCode <> ErrorUnknownProduct) and
       (ExitCode <> ErrorSuccessRebootRequired) then
    begin
      Result := Format(
        'Не удалось удалить старую MSI-версию Клип (код %d).',
        [ExitCode]);
      Exit;
    end;

    if ExitCode = ErrorSuccessRebootRequired then
      LegacyMsiRestartRequired := True;
  end;

  if LegacyMsiProductCodes.Count > 0 then
  begin
    if DirExists(ExpandConstant('{pf32}\Klip')) and
       not DelTree(ExpandConstant('{pf32}\Klip'), True, True, True) then
    begin
      Result := 'Не удалось удалить файлы старой MSI-версии Клип.';
      Exit;
    end;
    if DirExists(ExpandConstant('{commonprograms}\Klip')) and
       not DelTree(ExpandConstant('{commonprograms}\Klip'), True, True, True) then
    begin
      Result := 'Не удалось удалить ярлык старой MSI-версии Клип.';
      Exit;
    end;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := FindLegacyMsiProducts();
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  MigrationError: String;
begin
  if CurStep = ssPostInstall then
  begin
    MigrationError := RemoveLegacyMsiProducts();
    if MigrationError <> '' then
      RaiseException(MigrationError);
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := LegacyMsiRestartRequired;
end;

procedure DeinitializeSetup();
begin
  if LegacyMsiProductCodes <> nil then
    LegacyMsiProductCodes.Free;
end;
