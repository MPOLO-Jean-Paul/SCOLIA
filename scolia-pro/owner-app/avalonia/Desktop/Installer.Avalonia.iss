#define AdminVersion "1.1.0"
#define OwnerRoot GetEnv("OWNER_PUBLISH_DIR")
#if OwnerRoot == ""
  #error OWNER_PUBLISH_DIR must be the Avalonia Desktop publish output.
#endif

[Setup]
AppId={{73E1982B-93AE-472E-B31D-86F35684107B}
AppName=SCOLIA Pro - Administration JP-Services
AppVersion={#AdminVersion}
AppPublisher=JP-Services
AppPublisherURL=https://github.com/MPOLO-Jean-Paul/SCOLIA
DefaultDirName={autopf}\JP-Services\SCOLIA Admin
DefaultGroupName=SCOLIA Admin JP-Services
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=SCOLIA_JPServices_Avalonia_Setup_{#AdminVersion}_Windows_x64
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayName=SCOLIA Pro - Administration JP-Services
LicenseFile=../../windows/LICENSE_FR.txt

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Ajouter un raccourci sur le bureau"; GroupDescription: "Raccourcis"; Flags: unchecked

[Files]
Source: "{#OwnerRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SCOLIA Admin"; Filename: "{app}\SCOLIA-JPServices-Admin.exe"
Name: "{autodesktop}\SCOLIA Admin"; Filename: "{app}\SCOLIA-JPServices-Admin.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SCOLIA-JPServices-Admin.exe"; Description: "Lancer SCOLIA Admin Avalonia"; Flags: nowait postinstall skipifsilent
