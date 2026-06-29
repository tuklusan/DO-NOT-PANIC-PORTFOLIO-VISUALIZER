; ============================================================================
; Copyright (c) 2026 Supratim Sanyal of SANYALnet Labs.
; Proprietary rights reserved except as expressly licensed herein.
;
; DO NOT PANIC PORTFOLIO VIEWER
; This software and its derivatives are licensed for STRICTLY NON-COMMERCIAL,
; personal, educational, or hobbyist use only. Commercial exploitation,
; corporate internal operations, or AI model training are strictly forbidden.
;
; ATTRIBUTION & DEPENDENCIES: This application incorporates the YFinance library,
; which is licensed under the Apache License, Version 2.0. A copy of the Apache
; License is provided within the distribution environment.
;
; FINANCIAL DISCLAIMER: This software is a passive visualization tool only.
; It does not provide financial, investment, legal, or tax advice. All data
; calculation and scraping outputs are provided 'AS IS' with zero guarantee
; of real-time accuracy or upstream availability.
;
; This file is subject to the terms and conditions defined in the LICENSE
; file located in the root directory of this source code repository.
; Removal or modification of this legal notice constitutes copyright infringement.
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
