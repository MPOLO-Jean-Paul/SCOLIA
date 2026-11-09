#define AdminVersion "1.1.1"
#define OwnerRoot GetEnv("OWNER_PUBLISH_DIR")
#if OwnerRoot == ""
  #error OWNER_PUBLISH_DIR must be the Avalonia Desktop publish output.
#endif

[Setup]
AppId={{73E1982B-93AE-472E-B31D-86F35684107B}
AppName=SCOLIA Console
AppVersion={#AdminVersion}
AppPublisher=JP-Services
AppPublisherURL=https://github.com/MPOLO-Jean-Paul/SCOLIA
DefaultDirName={autopf}\JP-Services\SCOLIA Console
DefaultGroupName=SCOLIA Console
DisableProgramGroupPage=yes
OutputDir=installer-output
OutputBaseFilename=SCOLIA_Console_Setup_{#AdminVersion}_Windows_x64
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
PrivilegesRequired=lowest
UninstallDisplayName=SCOLIA Console
LicenseFile=../../windows/LICENSE_FR.txt

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Ajouter un raccourci sur le bureau"; GroupDescription: "Raccourcis"; Flags: unchecked

[Files]
Source: "{#OwnerRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SCOLIA Console"; Filename: "{app}\SCOLIA-Console.exe"
Name: "{autodesktop}\SCOLIA Console"; Filename: "{app}\SCOLIA-Console.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\SCOLIA-Console.exe"; Description: "Lancer SCOLIA Console Avalonia"; Flags: nowait postinstall skipifsilent
