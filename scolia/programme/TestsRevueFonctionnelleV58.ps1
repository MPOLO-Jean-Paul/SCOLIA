# Régressions isolées : recherche de caisse, invalidation et routage des commandes.
$ErrorActionPreference='Stop'
function Assert-Revue([bool]$Valid,[string]$Message){if(-not $Valid){throw $Message}}
. (Join-Path $PSScriptRoot 'CentreRechercheV55.ps1')
function Test-ITMPermission([string]$Name){return $Name -in @('Lire','Comptabilite')}
$script:scolia=[pscustomobject]@{Id='school';Year='2026-2027'}
$script:scoliaRoot='test';$script:scoliaSession=[pscustomobject]@{Role='Comptabilité';AccountId='account'}
$script:tabsNames=@('Comptabilité');$script:main=$null;$script:data=$null;$script:scoliaLedgerStamp='same'
$entry=[pscustomobject]@{Id='receipt';Kind='Paiement';SchoolId='school';Annee='2026-2027';Nom='Marie';PostNom='LONO';Prenom='Claire';Matricule='INF123';Reference='REF001';Promotion='INF1';Date='2026-10-12';Montant=100;Category='EXAMEN';Cancelled='';Revision='0';Parts=@()}
$script:scoliaEntries=@($entry)
$rows=@(Get-ScoliaGlobalSearchRows 'INF123' 'Paiements')
Assert-Revue ($rows.Count -eq 1 -and $rows[0].Matricule -ceq 'INF123') 'Paiement de caisse introuvable par matricule'
Assert-Revue (@(Get-ScoliaGlobalSearchRows 'INF123 examen' 'Paiements').Count -eq 1) 'Recherche matricule et catégorie incorrecte'
Assert-Revue (@(Get-ScoliaGlobalSearchRows '2026-10-12' 'Paiements').Count -eq 1) 'Date du paiement absente de la recherche'
[void](Get-ScoliaCachedSearchRows -Force);$before=$script:scoliaSearchIndex
$replacement=$entry|ConvertTo-Json -Depth 10|ConvertFrom-Json;$replacement.Cancelled='1';$replacement.Revision='1';$replacement.Montant=0
$script:scoliaEntries=@($replacement)
$rows=@(Get-ScoliaCachedSearchRows)
Assert-Revue (-not [object]::ReferenceEquals($before,$script:scoliaSearchIndex)) 'Cache conservé malgré remplacement du journal de même taille'
Assert-Revue ($rows.Count -eq 1 -and $rows[0].Description -like '*Annulé*' -and $rows[0].Amount -eq '0') 'Reçu annulé présenté comme paiement actif'
$stamp=Get-ScoliaSearchIndexStamp;$script:scoliaSession.AccountId='other'
Assert-Revue ($stamp -cne (Get-ScoliaSearchIndexStamp)) 'Changement de compte absent de la clé de cache'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'CommandesClavier.ps1'),[ref]$tokens,[ref]$errors)
foreach($n in $ast.FindAll({param($n)$n -is [Management.Automation.Language.AssignmentStatementAst] -and $n.Left.Extent.Text.StartsWith('$script:commandDefinitions')},$true)){. ([scriptblock]::Create($n.Extent.Text))}
foreach($name in @('Convert-ITMShortcutKeys','Get-ITMCommandInfo')){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create($node.Extent.Text))}
Assert-Revue ((Convert-ITMShortcutKeys '') -eq 0 -and (Convert-ITMShortcutKeys '  ') -eq 0) 'Raccourci vide provoquant une erreur'
$script:commandDefinitions['Commande sans raccourci']=[pscustomobject]@{Shortcut='';Description='Aide disponible'}
$info=Get-ITMCommandInfo 'Commande sans raccourci'
Assert-Revue ($info.Keys -eq 0 -and $info.Description -eq 'Aide disponible') 'Aide sans raccourci inutilisable'
Assert-Revue ($script:commandDefinitions['Nettoyer les sauvegardes'].Shortcut -eq 'Ctrl+Shift+Delete') 'Nettoyage sans raccourci défini'
'PASS : caisse par matricule/catégorie/date, annulation visible, cache renouvelé, compte isolé et raccourcis vides sans erreur.'
