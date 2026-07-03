; ============================================================================
; Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
; Proprietary rights reserved except as expressly licensed herein.
;
; DO NOT PANIC PORTFOLIO VISUALIZER
; This file is governed by the SANYALnet Labs Non-Commercial License in the
; root LICENSE file. Non-Commercial use is permitted; Commercial Use and use
; for AI/ML model training are prohibited unless separately authorized.
;
; Attribution is required: "Based on original work by Supratim Sanyal of
; SANYALnet Labs." See LICENSE for full terms, warranty disclaimer, termination,
; patent, trademark, and governing-law provisions.
; ============================================================================

#ifndef SourceRoot
  #error SourceRoot must be supplied by build/publish-inno-installer.ps1.
#endif

#ifndef OutputRoot
  #error OutputRoot must be supplied by build/publish-inno-installer.ps1.
#endif

#ifndef LicenseFile
  #error LicenseFile must be supplied by build/publish-inno-installer.ps1.
#endif

#ifndef AppVersion
  #error AppVersion must be supplied by build/publish-inno-installer.ps1.
#endif

#ifndef IconFile
  #define IconFile ""
#endif

#define AppPublisher "SANYALnet Labs"
#define AppAuthor "Supratim Sanyal"
#define AppName "DO NOT PANIC PORTFOLIO VISUALIZER"
#define AppFolderName "DoNotPanicPortfolioVisualizer"

[Setup]
AppId={{B0839D4C-1D29-4D9C-95E3-C88E4D8E37E5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER
AppSupportURL=https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER
AppUpdatesURL=https://github.com/tuklusan/DO-NOT-PANIC-PORTFOLIO-VISUALIZER
VersionInfoCompany={#AppPublisher}
VersionInfoCopyright=Copyright (c) 2026 {#AppAuthor} of {#AppPublisher}. Proprietary rights reserved except as expressly licensed herein.
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppPublisher}\{#AppFolderName}
DefaultGroupName={#AppPublisher}\{#AppName}
; Public installs are intentionally fixed under Program Files so elevated uninstall cleanup can be narrowly scoped and auditable.
DisableDirPage=yes
DisableProgramGroupPage=yes
LicenseFile={#LicenseFile}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputRoot}
OutputBaseFilename=DoNotPanicPortfolioVisualizerSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
UninstallDisplayIcon={app}\PortfolioSaver.Desktop.exe
#if IconFile != ""
SetupIconFile={#IconFile}
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; CR-133: the public installer creates a standard all-users desktop shortcut by default.
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#SourceRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DO NOT PANIC PORTFOLIO VISUALIZER"; Filename: "{app}\PortfolioSaver.Desktop.exe"; WorkingDir: "{app}"
Name: "{group}\Settings"; Filename: "{app}\PortfolioSaver.Config.exe"; WorkingDir: "{app}"
Name: "{group}\License"; Filename: "{app}\LICENSE"; WorkingDir: "{app}"
Name: "{autodesktop}\DO NOT PANIC PORTFOLIO VISUALIZER"; Filename: "{app}\PortfolioSaver.Desktop.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\PortfolioSaver.Desktop.exe"; Description: "Launch DO NOT PANIC PORTFOLIO VISUALIZER"; Flags: nowait postinstall skipifsilent unchecked

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\Installer\Cleanup-DoNotPanicPortfolioVisualizer.ps1"" -AllUsers"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "DoNotPanicPortfolioVisualizerCleanup"

[UninstallDelete]
Type: dirifempty; Name: "{app}"
Type: dirifempty; Name: "{autopf}\{#AppPublisher}"

[Code]
var
  AiChoicePage: TInputOptionWizardPage;
  AiSetupPage: TInputQueryWizardPage;
  AiChoiceLayoutNormalized: Boolean;
  AiSetupLayoutNormalized: Boolean;

function JsonEscape(Value: String): String;
var
  I: Integer;
  Sanitized: String;
begin
  Sanitized := '';
  for I := 1 to Length(Value) do
  begin
    if Ord(Value[I]) >= 32 then
      Sanitized := Sanitized + Value[I];
  end;
  Value := Sanitized;
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  Result := Value;
end;

function IsAiSetupRequested(): Boolean;
begin
  Result := Assigned(AiChoicePage) and AiChoicePage.Values[0];
end;

procedure NormalizeAiChoicePageLayout();
begin
  if AiChoiceLayoutNormalized then
    Exit;

  if Assigned(AiChoicePage) then
  begin
    AiChoicePage.CheckListBox.Top := AiChoicePage.CheckListBox.Top + ScaleY(6);
    AiChoicePage.CheckListBox.Width := AiChoicePage.SurfaceWidth - ScaleX(16);
  end;

  AiChoiceLayoutNormalized := True;
end;

procedure NormalizeAiSetupPageLayout();
var
  I: Integer;
  LabelWidth: Integer;
begin
  if AiSetupLayoutNormalized then
    Exit;

  if Assigned(AiSetupPage) then
  begin
    LabelWidth := ScaleX(112);
    for I := 0 to 2 do
    begin
      AiSetupPage.PromptLabels[I].Width := LabelWidth;
      AiSetupPage.Edits[I].Left := AiSetupPage.PromptLabels[I].Left + LabelWidth + ScaleX(8);
      AiSetupPage.Edits[I].Width := AiSetupPage.SurfaceWidth - AiSetupPage.Edits[I].Left;
    end;
  end;

  AiSetupLayoutNormalized := True;
end;

procedure InitializeWizard();
begin
  AiChoicePage := CreateInputOptionPage(
    wpSelectTasks,
    'Optional AI News Setup',
    'Configure optional AI-summarized financial news.',
    'RSS financial news is the default and does not require an API key. Select this option only if you want the installer to save AI access settings now.',
    False,
    False);
  AiChoicePage.Add('Configure AI-summarized financial news now');
  AiChoicePage.Values[0] := False;

  AiSetupPage := CreateInputQueryPage(
    AiChoicePage.ID,
    'AI News Access',
    'Enter optional AI API access details.',
    'Example free personal setup: visit openrouter.ai, sign up for a free Individual account, create an API key, and paste it here. You may also change the endpoint and model for another compatible AI engine.');
  AiSetupPage.Add('AI API key:', True);
  AiSetupPage.Add('Endpoint URL:', False);
  AiSetupPage.Add('Model ID:', False);
  AiSetupPage.Values[1] := 'https://openrouter.ai/api/v1';
  AiSetupPage.Values[2] := 'openrouter/free';
  AiChoiceLayoutNormalized := False;
  AiSetupLayoutNormalized := False;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (Assigned(AiChoicePage)) and (CurPageID = AiChoicePage.ID) then
    NormalizeAiChoicePageLayout();
  if (Assigned(AiSetupPage)) and (CurPageID = AiSetupPage.ID) then
    NormalizeAiSetupPageLayout();
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = AiSetupPage.ID then
  begin
    if IsAiSetupRequested() then
    begin
      if (Trim(AiSetupPage.Values[0]) = '') or
         (Trim(AiSetupPage.Values[1]) = '') or
         (Trim(AiSetupPage.Values[2]) = '') then
      begin
        MsgBox('Enter AI API key, endpoint URL, and model ID, or go back and clear the AI setup option.', mbError, MB_OK);
        Result := False;
      end;
    end;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if Assigned(AiSetupPage) and (PageID = AiSetupPage.ID) then
    Result := not IsAiSetupRequested();
end;

procedure SaveOptionalAiSettings();
var
  SettingsDir: String;
  ImportPath: String;
  MergeScriptPath: String;
  Json: String;
  MergeScript: String;
  ResultCode: Integer;
begin
  if not IsAiSetupRequested() then
    Exit;

  SettingsDir := ExpandConstant('{localappdata}\DoNotPanicPortfolioVisualizer');
  ForceDirectories(SettingsDir);
  ImportPath := AddBackslash(ExpandConstant('{tmp}')) + 'installer-ai-settings.json';
  MergeScriptPath := AddBackslash(ExpandConstant('{tmp}')) + 'Merge-DoNotPanicAiSettings.ps1';
  Json :=
    '{'#13#10 +
    '  "NewsScrollerMode": 0,'#13#10 +
    '  "AiApiKey": "' + JsonEscape(Trim(AiSetupPage.Values[0])) + '",'#13#10 +
    '  "AiEndpointUrl": "' + JsonEscape(Trim(AiSetupPage.Values[1])) + '",'#13#10 +
    '  "AiModelId": "' + JsonEscape(Trim(AiSetupPage.Values[2])) + '",'#13#10 +
    '  "NewsRefreshMinutes": 30'#13#10 +
    '}'#13#10;
  SaveStringToFile(ImportPath, Json, False);

  MergeScript :=
    '$ErrorActionPreference = ''Stop'''#13#10 +
    '$root = Join-Path $env:LOCALAPPDATA ''DoNotPanicPortfolioVisualizer'''#13#10 +
    '$settingsPath = Join-Path $root ''settings.json'''#13#10 +
    '$secretsPath = Join-Path $root ''provider-secrets.json'''#13#10 +
    '$importPath = ''' + ImportPath + ''''#13#10 +
    '$incoming = Get-Content -Raw -LiteralPath $importPath | ConvertFrom-Json'#13#10 +
    'if (Test-Path -LiteralPath $settingsPath) {'#13#10 +
    '  try { $settings = Get-Content -Raw -LiteralPath $settingsPath | ConvertFrom-Json } catch { $settings = [pscustomobject]@{} }'#13#10 +
    '} else {'#13#10 +
    '  $settings = [pscustomobject]@{}'#13#10 +
    '}'#13#10 +
    'function Set-SettingValue([string]$Name, $Value) {'#13#10 +
    '  if ($settings.PSObject.Properties[$Name]) { $settings.$Name = $Value } else { Add-Member -InputObject $settings -NotePropertyName $Name -NotePropertyValue $Value }'#13#10 +
    '}'#13#10 +
    'function Get-SettingValue([string]$Name) {'#13#10 +
    '  if ($settings.PSObject.Properties[$Name]) { return [string]$settings.$Name }'#13#10 +
    '  return '''''#13#10 +
    '}'#13#10 +
    'function Get-SettingValueWithFallback([string]$Name, [string]$LegacyName) {'#13#10 +
    '  $value = Get-SettingValue $Name'#13#10 +
    '  if ([string]::IsNullOrWhiteSpace($value)) { $value = Get-SettingValue $LegacyName }'#13#10 +
    '  return $value'#13#10 +
    '}'#13#10 +
    'Set-SettingValue ''NewsScrollerMode'' $incoming.NewsScrollerMode'#13#10 +
    'Set-SettingValue ''NewsRefreshMinutes'' $incoming.NewsRefreshMinutes'#13#10 +
    'Set-SettingValue ''AiApiKey'' '''''#13#10 +
    '$defaultEndpoint = ''https://openrouter.ai/api/v1'''#13#10 +
    '$existingEndpoint = Get-SettingValueWithFallback ''AiEndpointUrl'' ''DeepSeekEndpointUrl'''#13#10 +
    'if ([string]::IsNullOrWhiteSpace($existingEndpoint) -or [string]::Equals($existingEndpoint, $defaultEndpoint, [StringComparison]::OrdinalIgnoreCase)) {'#13#10 +
    '  Set-SettingValue ''AiEndpointUrl'' $incoming.AiEndpointUrl'#13#10 +
    '} else {'#13#10 +
    '  Set-SettingValue ''AiEndpointUrl'' $existingEndpoint'#13#10 +
    '}'#13#10 +
    '$defaultModel = ''openrouter/free'''#13#10 +
    '$existingModel = Get-SettingValueWithFallback ''AiModelId'' ''DeepSeekModelId'''#13#10 +
    'if ([string]::IsNullOrWhiteSpace($existingModel) -or [string]::Equals($existingModel, $defaultModel, [StringComparison]::OrdinalIgnoreCase)) {'#13#10 +
    '  Set-SettingValue ''AiModelId'' $incoming.AiModelId'#13#10 +
    '} else {'#13#10 +
    '  Set-SettingValue ''AiModelId'' $existingModel'#13#10 +
    '}'#13#10 +
    '$settings | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $settingsPath -Encoding UTF8'#13#10 +
    'if (-not [string]::IsNullOrWhiteSpace([string]$incoming.AiApiKey)) {'#13#10 +
    '  if (Test-Path -LiteralPath $secretsPath) {'#13#10 +
    '    try { $secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json } catch { $secrets = [pscustomobject]@{} }'#13#10 +
    '  } else {'#13#10 +
    '    $secrets = [pscustomobject]@{}'#13#10 +
    '  }'#13#10 +
    '  Add-Type -AssemblyName System.Security'#13#10 +
    '  $plainBytes = [Text.Encoding]::UTF8.GetBytes([string]$incoming.AiApiKey)'#13#10 +
    '  $protectedBytes = [Security.Cryptography.ProtectedData]::Protect($plainBytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)'#13#10 +
    '  $protectedValue = [Convert]::ToBase64String($protectedBytes)'#13#10 +
    '  if ($secrets.PSObject.Properties[''AiApiKey'']) { $secrets.AiApiKey = $protectedValue } else { Add-Member -InputObject $secrets -NotePropertyName ''AiApiKey'' -NotePropertyValue $protectedValue }'#13#10 +
    '  $secrets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $secretsPath -Encoding UTF8'#13#10 +
    '} elseif ((Test-Path -LiteralPath $secretsPath)) {'#13#10 +
    '  try { $secrets = Get-Content -Raw -LiteralPath $secretsPath | ConvertFrom-Json } catch { $secrets = [pscustomobject]@{} }'#13#10 +
    '  if (-not $secrets.PSObject.Properties[''AiApiKey''] -and $secrets.PSObject.Properties[''DeepSeekApiKey'']) { Add-Member -InputObject $secrets -NotePropertyName ''AiApiKey'' -NotePropertyValue $secrets.DeepSeekApiKey; $secrets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $secretsPath -Encoding UTF8 }'#13#10 +
    '}'#13#10 +
    'Remove-Item -LiteralPath $importPath -Force -ErrorAction SilentlyContinue'#13#10;
  SaveStringToFile(MergeScriptPath, MergeScript, False);

  if (not Exec(
    ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + MergeScriptPath + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode)) or (ResultCode <> 0) then
  begin
    MsgBox('The installer could not save optional AI settings. The application was installed successfully; AI settings can still be configured later from Settings.', mbInformation, MB_OK);
  end;

  DeleteFile(ImportPath);
  DeleteFile(MergeScriptPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    SaveOptionalAiSettings();
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if not UninstallSilent then
  begin
    Result := MsgBox(
      'Uninstall will remove the application from Program Files and delete the app-owned Local AppData folder named DoNotPanicPortfolioVisualizer for local Windows user profiles. External folders selected by users are not removed. Continue?',
      mbConfirmation,
      MB_YESNO) = IDYES;
  end;
end;
