; SCOLIA Pro - installeur Windows natif.
; Compile les fichiers publies dans artifacts/SCOLIA via Inno Setup 6+.
; L'installeur est autonome ; il ne depend ni de .NET installe ni d'Excel.
#ifndef AppVersion
  #define AppVersion "1.2.22"
#endif

#define AppName "SCOLIA Pro"
#define AppPublisher "JP-Services"
#define AppExe "SCOLIA.exe"
#define AppContactEmail "jeanpaulmpolo242@gmail.com"
#define AppSupportLink "mailto:jeanpaulmpolo242@gmail.com"

[Setup]
AppId=JP-Services.SCOLIA-Pro.Desktop
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppContact={#AppContactEmail}
AppPublisherURL={#AppSupportLink}
AppSupportURL={#AppSupportLink}
; Les mises à jour restent détectées et téléchargées par SCOLIA Pro,
; sans afficher de lien GitHub dans l'assistant d'installation Windows.
AppCopyright=Copyright 2026 JP-Services
AppComments=Gestion scolaire : apprenants, paiements, scolarite, rapports et partage prive. Assistance : jeanpaulmpolo242@gmail.com
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Installation de SCOLIA Pro pour Windows 64 bits
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\SCOLIA Pro
DefaultGroupName=SCOLIA Pro
DisableDirPage=no
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
WizardStyle=modern
WizardSizePercent=110
SetupIconFile=..\..\src\Scolia.Api\scolia.ico
UninstallDisplayIcon={app}\SCOLIA.exe
UninstallDisplayName=SCOLIA Pro
LicenseFile=CONDITIONS_UTILISATION.txt
InfoBeforeFile=AVANT_INSTALLATION.txt
InfoAfterFile=APRES_INSTALLATION.txt
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
ShowLanguageDialog=no
OutputDir=..\..\artifacts\installer
OutputBaseFilename=SCOLIA_Pro_Installation_{#AppVersion}_Windows_x64

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Creer un raccourci SCOLIA Pro sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
; Le serveur, l'application et leurs ressources sont inclus dans un seul EXE.
; Une mise a jour ne doit JAMAIS remplacer le parametrage de l'etablissement.
Source: "..\..\artifacts\SCOLIA\*"; DestDir: "{app}"; Excludes: "appsettings.json,donnees\*"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\artifacts\SCOLIA\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Dirs]
; Le dossier SQLite, ses sauvegardes et ses clefs ne sont pas des fichiers
; installes, et restent intacts meme en cas de desinstallation.
Name: "{app}\donnees"; Flags: uninsneveruninstall

[Icons]
Name: "{autoprograms}\SCOLIA Pro"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Comment: "Gestion scolaire SCOLIA Pro"
Name: "{autodesktop}\SCOLIA Pro"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer SCOLIA Pro maintenant"; Flags: nowait postinstall skipifsilent

[Code]
function ServiceSCOLIAInstalle(): Boolean;
var
  CodeRetour: Integer;
begin
  // Le service de l'etablissement peut servir d'autres appareils sans fenetre.
  // Un desinstallateur utilisateur ne doit pas effacer ses executables.
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query SCOLIA',
    '', SW_HIDE, ewWaitUntilTerminated, CodeRetour) and (CodeRetour = 0);
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  if ServiceSCOLIAInstalle() then
  begin
    MsgBox('Le service Windows SCOLIA est encore installe.' + #13#10 +
      'Avant de desinstaller, ouvrez SCOLIA Pro > Poste et reseau ' +
      'et desactivez "Demarrer avec Windows". ' +
      'Cela permet de fermer proprement le serveur et de conserver les donnees scolaires.',
      mbError, MB_OK);
    Result := False;
  end;
end;
