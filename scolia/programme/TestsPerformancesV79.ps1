$ErrorActionPreference='Stop'
function Check($ok,[string]$why){if(-not $ok){throw $why}}
function Import-TestFunction([string]$File,[string]$Name){
 $tokens=$null;$errors=$null;$path=Join-Path $PSScriptRoot $File
 $ast=[Management.Automation.Language.Parser]::ParseFile($path,[ref]$tokens,[ref]$errors)
 if($errors.Count){throw ('Syntaxe invalide dans '+$File+' : '+$errors[0].Message)}
 $fn=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $Name},$true)
 if(-not $fn){throw ('Fonction absente : '+$Name)}
 . ([scriptblock]::Create('function global:'+ $Name + $fn.Extent.Text.Substring(('function '+$Name).Length)))
}
foreach($file in @('Application.ps1','PerformanceInterface.ps1','StockageClasseurs.ps1','PagesModernes.ps1','Lisibilite.ps1','PreferencesAffichage.ps1','InterfaceResponsive.ps1','Scolarite.ps1','InterfaceEnseignementsV59.ps1','InterfaceDisciplineV60.ps1','PaiementsMultiplesV72.ps1')){
 $tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$tokens,[ref]$errors)
 Check ($errors.Count -eq 0) ('Erreur PowerShell : '+$file+' : '+$(if($errors.Count){$errors[0].Message}else{''}))
}
$app=Get-Content (Join-Path $PSScriptRoot 'Application.ps1') -Raw -Encoding UTF8
$perf=Get-Content (Join-Path $PSScriptRoot 'PerformanceInterface.ps1') -Raw -Encoding UTF8
$pages=Get-Content (Join-Path $PSScriptRoot 'PagesModernes.ps1') -Raw -Encoding UTF8
$responsive=Get-Content (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1') -Raw -Encoding UTF8
$school=Get-Content (Join-Path $PSScriptRoot 'Scolarite.ps1') -Raw -Encoding UTF8
$teaching=Get-Content (Join-Path $PSScriptRoot 'InterfaceEnseignementsV59.ps1') -Raw -Encoding UTF8
$discipline=Get-Content (Join-Path $PSScriptRoot 'InterfaceDisciplineV60.ps1') -Raw -Encoding UTF8
$batch=Get-Content (Join-Path $PSScriptRoot 'PaiementsMultiplesV72.ps1') -Raw -Encoding UTF8
$display=Get-Content (Join-Path $PSScriptRoot 'PreferencesAffichage.ps1') -Raw -Encoding UTF8
$vba=Get-Content (Join-Path $PSScriptRoot 'Corrections/ModuleApplicationITM.txt') -Raw -Encoding UTF8
$reliability=Get-Content (Join-Path $PSScriptRoot 'Corrections/ModuleFiabilite.txt') -Raw -Encoding UTF8
Check ($app -notmatch 'if\(\$mat -and @\(\$script:data\.payments .*Promotion\.Enabled=\$false') 'La promotion est encore verrouillée après paiement.'
Check ($vba -notmatch 'Des paiements existent : promotion et annee conservees') 'Le moteur VBA bloque encore la promotion après paiement.'
Check ($vba -match 'SCOLIA-PERF-7') 'Version applicative V80 absente.'
Check ($reliability -match 'ITM-PERF-2') 'Version fiabilité V79 absente.'
Check ($reliability -match 'ITMTransactionExterneActive') 'La suppression de la double sauvegarde transactionnelle est absente.'
Check ($reliability -match 'ThisWorkbook\.Calculate') 'Le recalcul ciblé du classeur est absent.'
Check ($reliability -notmatch '(?m)^\s*Application\.Calculate\s*$') 'Un recalcul global Application.Calculate subsiste dans ModuleFiabilite.'
$paymentStart=$vba.IndexOf('Public Function ITMAppPaiement')
$paymentEnd=$vba.IndexOf('Public Function ITMAppDoublonsPaiement',$paymentStart)
Check ($paymentStart -ge 0 -and $paymentEnd -gt $paymentStart) 'ITMAppPaiement introuvable.'
$payment=$vba.Substring($paymentStart,$paymentEnd-$paymentStart)
Check ($payment -notmatch 'ITMChargerDonnees\(\)') 'Le paiement recharge encore toute la base.'
Check ($vba -match 'Public Function ITMAppExporter') 'API export perdue pendant l optimisation.'
Check ($vba -match 'Public Function ITMAppAdministration') 'API administration perdue pendant l optimisation.'
Check ($payment -match 'w\.Range\(w\.Cells\(5, 4\), w\.Cells\(last, 4\)\)\.Value2') 'Lecture ciblée des matricules de paiement absente.'
Check ($vba -notmatch 'CountIf\(livre\.Worksheets\("PAIEMENTS"\)\.Columns\(3\)') 'La génération de reçu balaie encore une colonne Excel entière.'
Check ($pages -match 'modernPageLayoutTimer\.Interval=16') 'Le regroupement des mises en page dynamiques est absent.'
Check ($perf -match 'searchTimer\.Interval=45') 'La recherche instantanée n utilise pas le délai V79.'
Check ($responsive -match 'responsiveLayoutTimer\.Interval=16') 'Le regroupement des redimensionnements responsifs est absent.'
Check ($school -match 'schoolSearchTimer\.Interval=45') 'La recherche Scolarité n est pas regroupée.'
Check ($school -match 'schoolRosterCache') 'Le cache de répertoire Scolarité est absent.'
Check ($school -match 'schoolSearchTimer\.Add_Tick\(\{[^\r\n]*Refresh-ITMSchoolCards -SkipDisciplineSync') 'La recherche Scolarité relance encore la synchronisation discipline.'
Check ($teaching -match '\$filterTimer\.Interval=45') 'La recherche des cours n est pas regroupée.'
Check ($discipline -match '\$filterTimer\.Interval=45') 'La recherche Discipline n est pas regroupée.'
Check ($batch -match '\$filterTimer\.Interval=45') 'La recherche Paiements multiples n est pas regroupée.'
Check ($batch -match 'HeaderCell\.Tag=Key') 'L index de recherche des paiements multiples est absent.'
Check ($display -match "AutoSizeRowsMode='None'") 'Les grilles restent en auto-dimensionnement permanent.'
Check ($perf -match 'function Update-ITMPaymentSliceFast') 'La mise à jour différentielle des paiements est absente.'
Check ($perf -match 'function Update-ITMBalanceSliceFast') 'La mise à jour différentielle des soldes est absente.'
Check ($perf -match 'function Update-ITMLearnerSliceFast') 'La mise à jour différentielle du dossier est absente.'
Check ($app -match 'balanceRows=@\{\}.*balanceKeysByMat=@\{\}') 'Les index de soldes ne sont pas préparés au chargement.'

Import-TestFunction 'Application.ps1' 'Key'
Import-TestFunction 'Application.ps1' 'Promo'
Import-TestFunction 'PerformanceInterface.ps1' 'Get-ITMLearnerIdentityLookupKey'
Import-TestFunction 'PerformanceInterface.ps1' 'Get-ITMLearnerMatches'
Import-TestFunction 'PerformanceInterface.ps1' 'Get-ITMLearnersByIdentity'
Import-TestFunction 'PerformanceInterface.ps1' 'Rebuild-ITMLearnerIndex'
Import-TestFunction 'PerformanceInterface.ps1' 'Update-ITMLearnerIndexForSlice'
$script:data=[pscustomobject]@{learners=@()}
$items=New-Object 'System.Collections.Generic.List[object]'
1..20000 | ForEach-Object {$items.Add([pscustomobject]@{Matricule=('M'+$_.ToString('00000'));Nom=('NOM'+($_%500));PostNom='POST';Prenom=('P'+($_%100));Promotion=('P'+($_%8));Eligible=$true})}
$script:data.learners=$items.ToArray();Rebuild-ITMLearnerIndex
Check (@(Get-ITMLearnerMatches 'm00100').Count -eq 1) 'Index matricule incorrect.'
$sample=$script:data.learners[99]
Check (@(Get-ITMLearnersByIdentity $sample.Nom $sample.PostNom $sample.Prenom $sample.Promotion).Count -ge 1) 'Index identité incorrect.'
$oldSource=$script:data.learners;$replacement=$script:data.learners[99].PSObject.Copy();$replacement|Add-Member NoteProperty Statut 'Actif' -Force
$next=[object[]]$script:data.learners.Clone();$next[99]=$replacement;$script:data.learners=$next
Update-ITMLearnerIndexForSlice 'M00100'
Check ([object]::ReferenceEquals($script:learnerIndexSource,$script:data.learners)) 'Index rapide détaché après actualisation ciblée.'
Check ([object]::ReferenceEquals(@(Get-ITMLearnerMatches 'M00100')[0],$replacement)) 'Index matricule obsolète après paiement.'

# Verify the V79 hot path mutates only the affected rows and keeps lookup sources hot.
foreach($name in @('Copy-ITMObjectProperties','Add-ITMArrayItems','Get-ScoliaPaymentRows','Update-ITMPaymentSliceFast','Get-ITMBalanceKey','Rebuild-ITMBalanceIndexes','Update-ITMBalanceSliceFast','Update-ITMLearnerSliceFast')){Import-TestFunction 'PerformanceInterface.ps1' $name}
$oldPay=[pscustomobject]@{ID='1';Matricule='A1';Mois='Septembre';Verse=10;NomComplet='Alpha'}
$script:data=[pscustomobject]@{payments=@($oldPay);balances=@();learners=@()};$script:paymentLookup=$null
$newPay=$oldPay.PSObject.Copy();$newPay.Verse=15
$addedPay=[pscustomobject]@{ID='2';Matricule='A1';Mois='Octobre';Verse=20;NomComplet='Alpha'}
Check (Update-ITMPaymentSliceFast 'A1' @($newPay,$addedPay)) 'Chemin rapide paiements refusé.'
Check (@($script:data.payments).Count -eq 2 -and [object]::ReferenceEquals($script:data.payments[0],$oldPay) -and [decimal]$oldPay.Verse -eq 15) 'Paiements reconstruits ou objet historique non actualisé.'
Check ([object]::ReferenceEquals($script:paymentLookup.Source,$script:data.payments) -and @($script:paymentLookup.ByMat['A1']).Count -eq 2) 'Index paiements froid après ajout ciblé.'
$oldBal=[pscustomobject]@{Matricule='A1';Mois='Septembre';Verse=10}
$script:data.balances=@($oldBal);Rebuild-ITMBalanceIndexes
$newBal=$oldBal.PSObject.Copy();$newBal.Verse=15
$addedBal=[pscustomobject]@{Matricule='A1';Mois='Octobre';Verse=20}
Check (Update-ITMBalanceSliceFast 'A1' @($newBal,$addedBal)) 'Chemin rapide soldes refusé.'
Check (@($script:data.balances).Count -eq 2 -and [object]::ReferenceEquals($script:data.balances[0],$oldBal) -and [decimal]$script:balance['A1::SEPTEMBRE'] -eq 15) 'Soldes entièrement reconstruits ou index incorrect.'
$existing=[pscustomobject]@{Matricule='A1';Promotion='INF1';Nom='Alpha';PostNom='Beta';Prenom='Gamma';Statut='Actif'}
$script:data.learners=@($existing);$script:learnerIndex=@([pscustomobject]@{Entry=$existing;Text='A1 ALPHA BETA GAMMA INF1';Promotion='INF1'})
$script:learnerByMat=@{'A1'=(New-Object 'System.Collections.Generic.List[object]')};$script:learnerByMat['A1'].Add($existing)
$identityKey=Get-ITMLearnerIdentityLookupKey $existing.Nom $existing.PostNom $existing.Prenom $existing.Promotion
$script:learnerIdentityLookup=@{$identityKey=(New-Object 'System.Collections.Generic.List[object]')};$script:learnerIdentityLookup[$identityKey].Add($existing);$script:learnerIndexSource=$script:data.learners
$incomingLearner=$existing.PSObject.Copy();$incomingLearner.Statut='Suspendu';$sameLearnerSource=$script:data.learners
Check (Update-ITMLearnerSliceFast 'A1' $incomingLearner) 'Chemin rapide dossier refusé.'
Check ([object]::ReferenceEquals($sameLearnerSource,$script:data.learners) -and [object]::ReferenceEquals($script:data.learners[0],$existing) -and $existing.Statut -eq 'Suspendu') 'Dossier reconstruit malgré des clés stables.'
$watch=[Diagnostics.Stopwatch]::StartNew();1..5000|ForEach-Object {[void](Get-ITMLearnerMatches ('M'+(($_%20000)+1).ToString('00000')))};$watch.Stop()
Write-Output ('BENCH index apprenants : 5000 recherches / 20000 dossiers = '+[Math]::Round($watch.Elapsed.TotalMilliseconds,2)+' ms sur cette machine.')
Write-Output 'PASS V79 : dossier entièrement modifiable après paiement, mise à jour différentielle après écriture, index apprenants/paiements/soldes, lecture ciblée, transaction unique, recalcul ciblé et mise en page regroupée.'
