; Inno Setup 6.3+ script - builds a single Setup.exe that installs the CTS Revit Plugin bundle
; for Revit 2023, 2024, 2025 and 2026 (all users).
;
; Prerequisite: run Build.bat (or build.ps1 -Bundle) first, so that dist\CTSRevitPlugin.bundle exists.
; Then compile this file with Inno Setup, or just run:  .\build.ps1 -Installer
; Output: dist\CTSRevitPlugin_Setup_<version>.exe

#define AppName    "CTS Revit Plugin"
#define AppVersion "5.0.0"
#define BundleName "CTSRevitPlugin.bundle"

[Setup]
AppId={{A7C2E9D4-3B18-4F6A-8D05-C91E2B7F4A63}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=CTS BIM
; Revit scans this folder at startup and loads every *.bundle it finds (all users).
DefaultDirName={commonappdata}\Autodesk\ApplicationPlugins\{#BundleName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=CTSRevitPlugin_Setup_{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}

[Messages]
WelcomeLabel2=This will install {#AppName} {#AppVersion} for Autodesk Revit 2023, 2024, 2025 and 2026.%n%nPlease close Revit before continuing.

[Files]
Source: "..\dist\{#BundleName}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
