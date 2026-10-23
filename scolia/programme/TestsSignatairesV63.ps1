$ErrorActionPreference='Stop'
$script:scolia=$null
. (Join-Path $PSScriptRoot 'SignatairesV63.ps1')
function Assert-Signatory($Condition,[string]$Message){if(-not $Condition){throw $Message}}
$script:institutionProfile=[pscustomobject]@{Name='Institut Alpha';SignatoriesByInstitution=[pscustomobject]@{}}
$profile=Set-ScoliaInstitutionSignatories $script:institutionProfile ([pscustomobject]@{Prefet='  Préfet Alpha  ';Proviseur='Proviseur Alpha';Secretaire='Secrétaire Alpha'})
$script:institutionProfile=$profile|ConvertTo-Json -Depth 12|ConvertFrom-Json
Assert-Signatory ((Get-ScoliaInstitutionSignatory Prefet).Name -ceq 'Préfet Alpha') 'Nom non conservé après sérialisation.'
Assert-Signatory ((Get-ScoliaDocumentSignatory RapportRentree).Role -eq 'Prefet') 'Fonction du rapport incorrecte.'
Assert-Signatory ((Get-ScoliaDocumentSignatory RapportRentree Proviseur).Name -eq 'Proviseur Alpha') 'Choix du proviseur non appliqué.'
Assert-Signatory ((Resolve-ScoliaDocumentPersonnelText '{NOM_PREFET} / {PROVISEUR} / {SECRETAIRE}') -ceq 'Préfet Alpha / Proviseur Alpha / Secrétaire Alpha') 'Champs non remplacés.'
$script:scolia=[pscustomobject]@{Id='institution-b'}
Assert-Signatory (-not (Get-ScoliaInstitutionSignatory Prefet).Configured) 'Un autre institut reçoit le nom du préfet précédent.'
$script:institutionProfile=Set-ScoliaInstitutionSignatories $script:institutionProfile ([pscustomobject]@{Prefet='Préfet Beta';Proviseur='';Secretaire='Secrétaire Beta'})
Assert-Signatory ((Get-ScoliaInstitutionSignatory Prefet).Name -eq 'Préfet Beta') 'Deuxième institut non configuré.'
$script:scolia=$null
Assert-Signatory ((Get-ScoliaInstitutionSignatory Prefet).Name -eq 'Préfet Alpha') 'Responsable du premier institut perdu.'
Assert-Signatory ((Get-ScoliaInstitutionSignatory Proviseur).Name -eq 'Proviseur Alpha') 'Fonctions du premier institut mélangées.'
$blocked=$false;try{Set-ScoliaInstitutionSignatories $script:institutionProfile ([pscustomobject]@{Prefet="Nom`nInjecté";Proviseur='';Secretaire=''})|Out-Null}catch{$blocked=$true};Assert-Signatory $blocked 'Nom sur plusieurs lignes accepté.'
# Constantes textuelles dans un modèle Excel : seuls les marqueurs personnels changent.
class V63MockCell {
 [object]$Value2;[bool]$HasFormula
 V63MockCell([object]$Value,[bool]$Formula){$this.Value2=$Value;$this.HasFormula=$Formula}
}
class V63MockCells {
 [hashtable]$Items
 V63MockCells([hashtable]$Items){$this.Items=$Items}
 [object]Item([int]$Row,[int]$Column){return $this.Items["$Row,$Column"]}
}
class V63MockSheets {
 [object]$Sheet
 V63MockSheets([object]$Sheet){$this.Sheet=$Sheet}
 [int]$Count=1
 [object]Item([int]$Index){return $this.Sheet}
}
$values=[Array]::CreateInstance([object],[int[]]@(2,2),[int[]]@(1,1));$values.SetValue('{PREFET}',1,1);$values.SetValue('{PROVISEUR}',1,2);$values.SetValue('Intitulé conservé',2,1);$values.SetValue(18,2,2)
$items=@{'4,3'=[V63MockCell]::new('{PREFET}',$false);'4,4'=[V63MockCell]::new('{PROVISEUR}',$true);'5,3'=[V63MockCell]::new('Intitulé conservé',$false);'5,4'=[V63MockCell]::new(18,$false)}
$sheet=[pscustomobject]@{UsedRange=[pscustomobject]@{Value2=$values;Row=4;Column=3};Cells=[V63MockCells]::new($items)}
Set-ScoliaWorkbookPersonnel ([pscustomobject]@{Worksheets=[V63MockSheets]::new($sheet)})
Assert-Signatory ($items['4,3'].Value2 -ceq 'Préfet Alpha') 'Marqueur de la copie Excel non remplacé.'
Assert-Signatory ($items['4,4'].Value2 -ceq '{PROVISEUR}') 'Une formule du modèle a changé.'
Assert-Signatory ($items['5,3'].Value2 -ceq 'Intitulé conservé' -and $items['5,4'].Value2 -eq 18) 'Une autre cellule du modèle a changé.'
Write-Host 'OK : noms persistants, fonctions distinctes, changement d institut, champs automatiques et préservation des formules.' -ForegroundColor Green
