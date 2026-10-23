param(
    [string]$Lanceur,
    [switch]$SansLancement,
    [switch]$VerifierSeulement
)
$ErrorActionPreference='Stop'
$repairLog=$null
$backupRoot=$null
$committed=$false
$changed=New-Object 'System.Collections.Generic.List[object]'

function Write-RepairStep([string]$message) {
    Write-Host $message
    if($repairLog){[IO.File]::AppendAllText($repairLog,$message+[Environment]::NewLine,[Text.Encoding]::UTF8)}
}
function Get-RepairHash([string]$path) {
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256 -ErrorAction Stop).Hash
}
function Assert-RepairSyntax([string]$path) {
    $tokens=$null;$parseErrors=$null
    [void][Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$parseErrors)
    if($parseErrors.Count){
        $detail=($parseErrors | ForEach-Object {'Ligne '+$_.Extent.StartLineNumber+' : '+$_.Message}) -join [Environment]::NewLine
        throw ('Syntaxe invalide dans '+$path+[Environment]::NewLine+$detail)
    }
}

try {
    if($PSVersionTable.PSVersion -lt [version]'5.1') {throw 'Windows PowerShell 5.1 est requis.'}
    if(-not $Lanceur){
        Add-Type -AssemblyName System.Windows.Forms
        Write-Host 'Choisissez le fichier APPLICATION.vbs que vous lancez habituellement.'
        $dialog=New-Object Windows.Forms.OpenFileDialog
        $dialog.Title='SCOLIA V57 : choisir APPLICATION.vbs de votre installation actuelle'
        $dialog.Filter='Lanceur SCOLIA (APPLICATION.vbs)|APPLICATION.vbs'
        $dialog.CheckFileExists=$true
        $dialog.Multiselect=$false
        try {
            if($dialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK){
                Write-Host 'Reparation annulee. Aucun fichier modifie.'
                exit 0
            }
            $Lanceur=$dialog.FileName
        } finally {$dialog.Dispose()}
    }
    $launchFile=Get-Item -LiteralPath $Lanceur -ErrorAction Stop
    if($launchFile.PSIsContainer -or $launchFile.Name -ne 'APPLICATION.vbs' -or $launchFile.Directory.Name -ne '00_Demarrage'){
        throw 'Choisissez APPLICATION.vbs dans le dossier 00_Demarrage de votre installation SCOLIA.'
    }
    $root=$launchFile.Directory.Parent.FullName
    $programFolder=Join-Path $root 'programme'
    foreach($name in @('Lancer.ps1','Application.ps1','Installer.ps1','EtatEspace.ps1')){
        if(-not (Test-Path -LiteralPath (Join-Path $programFolder $name) -PathType Leaf)){
            throw ('Choisissez une installation V52, V53, V54, V55, V56 ou V57 complete, contenant EtatEspace.ps1. Fichier absent : '+$name)
        }
    }
    Write-RepairStep ('Dossier selectionne : '+$root)
    Write-RepairStep ('Ancienne empreinte EtatEspace.ps1 : '+(Get-RepairHash (Join-Path $programFolder 'EtatEspace.ps1')))

    $payload=[IO.Path]::GetDirectoryName($PSScriptRoot)
    if([IO.Path]::GetFullPath($root) -eq [IO.Path]::GetFullPath($payload)){throw "Choisissez le lanceur de votre ancienne installation. La nouvelle copie contient deja la V57."}
    $manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest-v57.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $entries=@($manifest.Files)
    $allowedPaths=@(
        'programme/EtatEspace.ps1',
        'programme/StockageClasseurs.ps1',
        'programme/TestsSoldesEtRechercheV52.ps1',
        'programme/TestsTransitionRuntimeV52.ps1',
        'programme/TestsSyntaxeV52.ps1',
        'programme/Application.ps1',
        'programme/ActivityIndicator.cs',
        'programme/ChromeModerne.cs',
        'programme/InterfaceResponsive.ps1',
        'programme/Ergonomie.ps1',
        'programme/ThemesAffichage.ps1',
        'programme/Lancer.ps1',
        'programme/TestsImagesTheme.ps1',
        'programme/TestsInscriptionPageUnique.ps1',
        'programme/TestsInscriptionDesignV53.ps1',
        'programme/VerifierV53.ps1',
        '00_Demarrage/VERIFIER_SYNTAXE_V52.cmd',
        '00_Demarrage/VERIFIER_V53.cmd',
        'LISEZ_MOI_V53.txt',
        'documentation/EVOLUTIONS_DESIGN_V53.md',
        'documentation/APERCU_INSCRIPTION_V53.html',
        'programme/ScoliaConfiguration.ps1',
        'programme/TransitionExperience.ps1',
        'programme/TestsConnexionDesignV54.ps1',
        'programme/VerifierV54.ps1',
        '00_Demarrage/VERIFIER_V54.cmd',
        'LISEZ_MOI_V54.txt',
        'documentation/EVOLUTIONS_CONNEXION_V54.md',
        'programme/FonctionnalitesV55.ps1',
        'programme/InscriptionBrouillonsV55.ps1',
        'programme/PresencesV55.ps1',
        'programme/ReinscriptionV55.ps1',
        'programme/CentreRechercheV55.ps1',
        'programme/ScoliaIntegration.ps1',
        'programme/ExploitationProfessionnelle.ps1',
        'programme/TestsFonctionnalitesV55.ps1',
        'programme/TestsInterfaceFonctionnalitesV55.ps1',
        'programme/TestsBrouillonsV55.ps1',
        'programme/TestsPresencesV55.ps1',
        'programme/TestsReinscriptionV55.ps1',
        'programme/TestsCentreRechercheV55.ps1',
        'programme/TestsSauvegardesV55.ps1',
        'programme/VerifierV55.ps1',
        '00_Demarrage/VERIFIER_V55.cmd',
        'LISEZ_MOI_V55.txt',
        'documentation/GUIDE_FONCTIONNALITES_V55.md',
        'documentation/PROPOSITIONS_SUIVANTES_V55.md',
        'programme/PersonnelV56.ps1',
        'programme/SalairesV56.ps1',
        'programme/ServicesPersonnelV56.ps1',
        'programme/InterfacePersonnelV56.ps1',
        'programme/SuiviApprenantsV56.ps1',
        'programme/TestsPersonnelV56.ps1',
        'programme/TestsSalairesV56.ps1',
        'programme/TestsServicesPersonnelV56.ps1',
        'programme/TestsInterfacePersonnelV56.ps1',
        'programme/TestsSuiviApprenantsV56.ps1',
        'programme/TestsSauvegardesV56.ps1',
        'programme/VerifierV56.ps1',
        '00_Demarrage/VERIFIER_V56.cmd',
        'LISEZ_MOI_V56.txt',
        'documentation/GUIDE_POINTAGE_SALAIRES_V56.md',
        'programme/StyleModerne.ps1',
        'programme/RubanAdaptatifV64.ps1',
        'programme/JsonCompatV65.ps1',
        'programme/InterfaceQualiteV65.ps1',
        'programme/TestsQualiteV65.ps1',
        'programme/TestsInterfaceQualiteV65.ps1',
        '00_Demarrage/VERIFIER_AUDIT_V65.cmd',
        'LIRE_MOI_AUDIT_V65.txt',
        'documentation/AUDIT_V65.md',
        'programme/TestsRubanAdaptatifV64.ps1',
        '00_Demarrage/VERIFIER_RUBAN_ADAPTATIF.cmd',
        'LIRE_MOI_RUBAN_ADAPTATIF.txt',
        'programme/NotificationsModernes.cs',
        'programme/CommandesClavier.ps1',
        'programme/TestsCatalogueCommandes.ps1',
        'programme/TestsNavigation.ps1',
        'programme/VerifierV57.ps1',
        '00_Demarrage/VERIFIER_V57.cmd',
        'LISEZ_MOI_V57.txt',
        'documentation/GUIDE_CONFIGURATION_PRESENCES_V57.md',
        'programme/TestsSeuilHoraireV57.ps1',
        'programme/TestsRetardsPaieV57.ps1',
        'programme/TestsPresenceFinJourV57.ps1',
        'programme/TestsSourcesPaieV57.ps1',
        'programme/TestsDispositionConfigurationV57.ps1',
        'programme/TestsConversionsInterfaceV57.ps1',
        'programme/TestsTransitionConfigurationV42.ps1',
        'programme/ScoliaLedger.cs',
        'programme/TestsCompilation.ps1',
        'programme/Lisibilite.ps1',
        'programme/PagesModernes.ps1',
        'programme/PerformanceInterface.ps1',
        'programme/PreferencesAffichage.ps1',
        'programme/TestsTableauxAdaptatifs.ps1',
        'programme/TestsTransitionPostInstallationV50.ps1',
        'programme/TestsPresentationProfessionnelle.ps1',
        'programme/CartesExamens.cs',
        'programme/Corrections/ModuleExportsFiables.txt',
        'programme/Corrections/ModuleProfessionnel.txt',
        'programme/TestsScolarite.ps1',
        'programme/manifest.json',
        'programme/Compatibilite/ModuleApplicationITM_AvantPerformanceV76.txt',
        'programme/TestsExportsPdfProfessionnels.ps1',
        'programme/MettreAJourV57.ps1',
        'LIRE_MOI_LISTES_CARTES_PDF.txt',
        'LIRE_MOI_DESIGN_PROFESSIONNEL.txt',
        '00_Demarrage/VERIFIER_LISTES_PDF.cmd',
        '00_Demarrage/VERIFIER_DESIGN_PRO.cmd',
        'programme/Compatibilite/ModuleProfessionnel_AvantDesignListes.txt',
        'programme/Compatibilite/ModuleExportsFiables_AvantDesignListes.txt',
        'programme/IconesFenetres.ps1',
        'programme/Personnalisation.ps1',
        'programme/Raccourcis.ps1',
        'programme/TestsExploitation.ps1',
        'programme/WindowsShortcuts.cs',
        'programme/TestsSauvegardesPersonnalisees.ps1',
        'programme/TestsRechercheSauvegardesDesign.ps1',
        'LIRE_MOI_SAUVEGARDES_RECHERCHE_LOGO.txt',
        '00_Demarrage/VERIFIER_RECHERCHE_SAUVEGARDES.cmd',
        'programme/GestionDonnees.ps1',
        'programme/NettoyageSauvegardes.ps1',
        'programme/TestsNettoyageSauvegardes.ps1',
        'LIRE_MOI_NETTOYAGE_SAUVEGARDES.txt',
        '00_Demarrage/VERIFIER_NETTOYAGE_SAUVEGARDES.cmd',
        'programme/Comptes.ps1',
        'programme/Securite.ps1',
        'programme/TestsValidationConnexion.ps1',
        'LIRE_MOI_VALIDATION_CONNEXION.txt',
        '00_Demarrage/VERIFIER_VALIDATION_CONNEXION.cmd',
        'programme/TestsSuppressionApprenant.ps1',
        'programme/TestsRevueFonctionnelleV58.ps1',
        'programme/VerifierFonctionnalitesV58.ps1',
        'LIRE_MOI_REVUE_FONCTIONNELLE.txt',
        '00_Demarrage/VERIFIER_FONCTIONNALITES.cmd',
        'programme/EnseignementsV59.ps1',
        'programme/InterfaceEnseignementsV59.ps1',
        'programme/TestsEnseignementsV59.ps1',
        'programme/TestsGrillesCoursV59.ps1',
        'programme/TestsInterfaceEnseignementsV59.ps1',
        'LIRE_MOI_COURS_ATTRIBUTIONS.txt',
        '00_Demarrage/VERIFIER_COURS_ATTRIBUTIONS.cmd',
        'programme/Scolarite.ps1',
        'programme/DisciplineV60.ps1',
        'programme/InterfaceDisciplineV60.ps1',
        'programme/RapportRentreeV62.ps1',
        'programme/SignatairesV63.ps1',
        'programme/TestsSignatairesV63.ps1',
        '00_Demarrage/VERIFIER_SIGNATURES.cmd',
        'LIRE_MOI_SIGNATURES_MISE_EN_PAGE.txt',
        'programme/Assets/RapportRentreeBase.docx',
        'programme/Assets/RapportRentree.json',
        'programme/TestsRapportRentreeV62.ps1',
        '00_Demarrage/VERIFIER_RAPPORT_RENTREE.cmd',
        'LIRE_MOI_RAPPORT_RENTREE.txt',
        'programme/TestsDisciplineV60.ps1',
        'programme/TestsInterfaceDisciplineV60.ps1',
        'LIRE_MOI_DISCIPLINE.txt',
        '00_Demarrage/VERIFIER_DISCIPLINE.cmd',
        'programme/PaiementsFiabiliteV61.ps1',
        'programme/TestsPaiementsFiabiliteV61.ps1',
        'programme/TestsInterfacePaiementsV61.ps1',
        'LIRE_MOI_PAIEMENTS_CORRIGES.txt',
        '00_Demarrage/VERIFIER_PAIEMENTS.cmd',
        'programme/Assets/SCOLIA.ico',
        'programme/Assets/SCOLIA.png',
        'programme/Assets/SCOLIA.svg',
        'programme/TestsExportsV66.ps1',
        'programme/TestsReactivitePages.ps1',
        '00_Demarrage/VERIFIER_V66.cmd',
        'LIRE_MOI_V66.txt',
        'programme/EspaceFonctionnelV67.ps1',
        'programme/TestsEspaceFonctionnelV67.ps1',
        'programme/TestsInterfaceEspaceV67.ps1',
        '00_Demarrage/VERIFIER_V67.cmd',
        'LIRE_MOI_V67.txt',
        'programme/CotesDeliberation.ps1',
        'programme/ReactiviteV68.ps1',
        'programme/TestsReactiviteV68.ps1',
        '00_Demarrage/VERIFIER_V68.cmd',
        'LIRE_MOI_V68.txt',
        'programme/ReferencesRecusV69.ps1',
        'programme/TestsReferencesRecusV69.ps1',
        'programme/TestsInterfaceEncaissementV69.ps1',
        '00_Demarrage/VERIFIER_V69.cmd',
        'LIRE_MOI_V69.txt',
        '00_Demarrage/VERIFIER_V70.cmd',
        'LIRE_MOI_V70.txt',
        'programme/PaiementsDirectsV71.ps1',
        'programme/TypographieV71.ps1',
        'programme/TestsPaiementsDirectsV71.ps1',
        'programme/TestsInterfacesV71.ps1',
        '00_Demarrage/VERIFIER_V71.cmd',
        'LIRE_MOI_V71.txt',
        'programme/FonctionsGestion.ps1',
        'programme/Installer.ps1',
        'programme/SynchronisationScolia.ps1',
        'programme/InstallationSession.ps1',
        'programme/TableauDeBordModerne.ps1',
        'programme/PrisesEnCharge.ps1',
        'programme/FinanceSynchronisee.ps1'
    )
    $uniqueNames=@($entries | ForEach-Object {[string]$_.Path} | Sort-Object -Unique)
    if($manifest.Version -ne 'V57' -or $entries.Count -ne $allowedPaths.Count -or $uniqueNames.Count -ne $allowedPaths.Count){
        throw 'Le manifeste du correctif est incomplet. Extrayez tout le ZIP dans un nouveau dossier.'
    }
    foreach($entry in $entries){
        if($allowedPaths -notcontains [string]$entry.Path -or [string]$entry.SHA256 -notmatch '^[0-9a-fA-F]{64}$'){
            throw 'Le manifeste contient un fichier inattendu. Aucun remplacement effectue.'
        }
        $source=Join-Path $payload ([string]$entry.Path)
        if((Get-RepairHash $source) -ne [string]$entry.SHA256){throw ('Correctif incomplet ou altere : '+$entry.Path)}
        if([IO.Path]::GetExtension($source) -eq '.ps1'){Assert-RepairSyntax $source}
    }
    # Compiler les composants modifies avant de toucher a l'installation.
    Add-Type -AssemblyName System.Windows.Forms,System.Drawing
    foreach($component in @('ActivityIndicator.cs','ChromeModerne.cs','NotificationsModernes.cs','CartesExamens.cs','WindowsShortcuts.cs')){
        Add-Type -Path (Join-Path $payload ('programme/'+$component)) -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Xml -IgnoreWarnings -ErrorAction Stop
    }
    # Le moteur financier doit aussi compiler sous Windows PowerShell 5.1 avant toute mise a jour.
    Add-Type -Path (Join-Path $payload 'programme/ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization -IgnoreWarnings -ErrorAction Stop
    # Analyser le futur ensemble : les remplacements ont deja ete analyses.
    $replacedScripts=@($entries | Where-Object {$_.Path -like 'programme/*.ps1'} | ForEach-Object {Split-Path -Leaf $_.Path})
    foreach($file in @(Get-ChildItem -LiteralPath $programFolder -Filter '*.ps1' -File)){
        if($file.Name -notin $replacedScripts){Assert-RepairSyntax $file.FullName}
    }
    if($VerifierSeulement){
        Write-Host 'OK : destination, contenu du correctif et syntaxe verifies. Aucun fichier modifie.' -ForegroundColor Green
        exit 0
    }

    $backupRoot=Join-Path $root ('journaux/reparation_V57_'+(Get-Date -Format 'yyyyMMdd_HHmmss')+'_'+[guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($backupRoot)
    $repairLog=Join-Path $backupRoot 'REPARATION.txt'
    Write-RepairStep ('SCOLIA V57 - mise a jour du '+(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
    Write-RepairStep ('Installation : '+$root)
    Write-RepairStep ('Lanceur : '+$launchFile.FullName)
    $jobs=New-Object 'System.Collections.Generic.List[object]'
    # Toutes les sauvegardes sont verifiees avant le premier remplacement.
    foreach($entry in $entries){
        $dest=Join-Path $root ([string]$entry.Path)
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($dest))
        $backup=Join-Path (Join-Path $backupRoot 'originaux') ([string]$entry.Path)
        $existed=[IO.File]::Exists($dest)
        $previousHash=$null
        if($existed){
            $previousHash=Get-RepairHash $dest
            [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($backup))
            [IO.File]::Copy($dest,$backup,$false)
            if((Get-RepairHash $backup) -ne $previousHash){throw ('Sauvegarde invalide : '+$entry.Path)}
        }
        $jobs.Add([pscustomobject]@{Path=[string]$entry.Path;Destination=$dest;Backup=$backup;Existed=$existed;PreviousHash=$previousHash})
    }
    [IO.File]::WriteAllText((Join-Path $backupRoot 'sauvegardes.json'),(ConvertTo-Json -InputObject $jobs.ToArray() -Depth 4),[Text.Encoding]::UTF8)
    foreach($job in $jobs){
        $temp=$job.Destination+'.reparation-'+[guid]::NewGuid().ToString('N')+'.tmp'
        try {
            $entry=@($entries | Where-Object {$_.Path -eq $job.Path})[0]
            [IO.File]::Copy((Join-Path $payload $job.Path),$temp,$false)
            if((Get-RepairHash $temp) -ne [string]$entry.SHA256){throw ('Copie temporaire invalide : '+$job.Path)}
            if($job.Existed){
                if((Get-RepairHash $job.Destination) -ne $job.PreviousHash){throw ('Le fichier a change pendant la reparation : '+$job.Path)}
                [IO.File]::Replace($temp,$job.Destination,$null)
            }else{
                [IO.File]::Move($temp,$job.Destination)
            }
            $changed.Add($job)
            if((Get-RepairHash $job.Destination) -ne [string]$entry.SHA256){throw ('Remplacement non confirme : '+$job.Path)}
            Write-RepairStep ('Remplace et verifie : '+$job.Path)
        } finally {if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}}
    }
    $count=0
    foreach($file in @(Get-ChildItem -LiteralPath $programFolder -Filter '*.ps1' -File)){
        Assert-RepairSyntax $file.FullName
        $count++
    }
    Write-RepairStep ('Nouvelle empreinte EtatEspace.ps1 : '+(Get-RepairHash (Join-Path $programFolder 'EtatEspace.ps1')))
    Write-RepairStep ('OK : '+$count+' scripts verifies par le parseur natif PowerShell.')
    Write-RepairStep ('Sauvegarde des anciens scripts : '+$backupRoot)
    $committed=$true
    Write-Host 'MISE A JOUR V57 TERMINEE.' -ForegroundColor Green
    if(-not $SansLancement){
        Write-RepairStep 'Ouverture du lanceur selectionne...'
        $wscript=Join-Path $env:SystemRoot 'System32/wscript.exe'
        $process=Start-Process -FilePath $wscript -ArgumentList ('"'+$launchFile.FullName+'"') -PassThru
        Write-Host ('Lanceur ouvert (processus '+$process.Id+'). Si une autre erreur apparait, conservez le journal affiche par SCOLIA.')
    }
    exit 0
} catch {
    $failure=$_.Exception.Message
    Write-Host ('ECHEC : '+$failure) -ForegroundColor Red
    if(-not $committed){
        $rollbackFailed=$false
        for($index=$changed.Count-1;$index -ge 0;$index--){
            $job=$changed[$index]
            try {
                if($job.Existed){
                    [IO.File]::Copy($job.Backup,$job.Destination,$true)
                    if((Get-RepairHash $job.Destination) -ne $job.PreviousHash){throw 'Restauration non confirmee.'}
                }elseif([IO.File]::Exists($job.Destination)){
                    [IO.File]::Delete($job.Destination)
                }
            } catch {
                $rollbackFailed=$true
                Write-Host ('Restauration manuelle necessaire : '+$job.Destination+' ; sauvegarde : '+$job.Backup) -ForegroundColor Red
            }
        }
        if(-not $rollbackFailed){Write-Host 'Les scripts existants ont ete conserves ou restaures.'}
    }else{
        Write-Host 'Les scripts sont corriges. Lancez APPLICATION.vbs depuis le dossier selectionne.'
    }
    if($repairLog){
        try {[IO.File]::AppendAllText($repairLog,'ECHEC : '+$failure+[Environment]::NewLine,[Text.Encoding]::UTF8)} catch {}
        Write-Host ('Journal : '+$repairLog)
    }
    exit 1
}
