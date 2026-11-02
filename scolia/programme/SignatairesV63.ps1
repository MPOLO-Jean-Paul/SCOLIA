# Responsables institutionnels. Les noms restent distincts des images de signatures.
function Get-ScoliaSignatoryInstitutionKey($Profile=$script:institutionProfile){
 if($script:scolia -and $script:scolia.Id){return 'school-'+[string]$script:scolia.Id}
 $hash=[Security.Cryptography.SHA256]::Create()
 try{return 'name-'+([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes(([string]$Profile.Name).Trim().ToUpperInvariant())))).Replace('-','').ToLowerInvariant()}finally{$hash.Dispose()}
}
function Get-ScoliaInstitutionSignatory([ValidateSet('Prefet','Proviseur','Secretaire')][string]$Role,$Profile=$script:institutionProfile){
 $label=switch($Role){'Prefet'{'Le préfet des études'};'Proviseur'{'Le proviseur'};'Secretaire'{'Le secrétaire'}}
 $name='';$key=Get-ScoliaSignatoryInstitutionKey $Profile
 if($Profile -and $Profile.PSObject.Properties['SignatoriesByInstitution']){
  $map=$Profile.SignatoriesByInstitution
  $entry=$(if($map -is [Collections.IDictionary]){$map[$key]}elseif($map -and $map.PSObject.Properties[$key]){$map.PSObject.Properties[$key].Value}else{$null})
  if($entry){$name=([string]$entry.$Role).Trim()}
 }
 return [pscustomobject]@{Role=$Role;Label=$label;Name=$name;Configured=(-not [string]::IsNullOrWhiteSpace($name));InstitutionKey=$key}
}
function Set-ScoliaInstitutionSignatories($Profile,$Names){
 $entry=[pscustomobject]@{Prefet='';Proviseur='';Secretaire=''}
 foreach($role in @('Prefet','Proviseur','Secretaire')){
  $name=([string]$Names.$role).Trim()
  if($name.Length -gt 160 -or $name -match '[\x00-\x1f\x7f]'){throw ('Nom du responsable invalide : '+$role+'. Maximum 160 caractères, sur une seule ligne.')}
  $entry.$role=$name
 }
 $map=[pscustomobject]@{}
 if($Profile.PSObject.Properties['SignatoriesByInstitution'] -and $Profile.SignatoriesByInstitution){
  $previous=$Profile.SignatoriesByInstitution
  if($previous -is [Collections.IDictionary]){foreach($k in $previous.Keys){$map|Add-Member NoteProperty ([string]$k) $previous[$k]}}
  else{foreach($p in $previous.PSObject.Properties){$map|Add-Member NoteProperty $p.Name $p.Value}}
 }
 $map|Add-Member NoteProperty (Get-ScoliaSignatoryInstitutionKey $Profile) $entry -Force
 $Profile|Add-Member NoteProperty SignatoriesByInstitution $map -Force
 return $Profile
}
function Get-ScoliaDocumentSignatory([ValidateSet('RapportRentree','ConvocationParents','ListeApprenants','AttestationScolaire')][string]$Document,[string]$Role=''){
 if(-not $Role){$Role=switch($Document){'ListeApprenants'{'Secretaire'};default{'Prefet'}}}
 if($Role -notin @('Prefet','Proviseur','Secretaire')){throw 'Fonction du signataire non reconnue.'}
 return Get-ScoliaInstitutionSignatory $Role
}
function Resolve-ScoliaDocumentPersonnelText([string]$Text){
 foreach($pair in @(@('Prefet','PREFET'),@('Proviseur','PROVISEUR'),@('Secretaire','SECRETAIRE'))){
  $signer=Get-ScoliaInstitutionSignatory $pair[0];$name=$(if($signer.Configured){$signer.Name}else{'Nom à renseigner'})
  $Text=$Text.Replace('{NOM_'+$pair[1]+'}',$name).Replace('{'+$pair[1]+'}',$name)
 }
 return $Text
}
# Utilisé uniquement sur les copies des grilles produites par le logiciel.
# Les cellules de formule et les autres cellules du modèle sont conservées.
function Set-ScoliaWorkbookPersonnel($Book){
 for($s=1;$s -le $Book.Worksheets.Count;$s++){
  $sheet=$Book.Worksheets.Item($s);$used=$sheet.UsedRange;$values=$used.Value2
  if($null -eq $values){continue}
  if($values -is [string] -and $values -match '\{(?:NOM_)?(?:PREFET|PROVISEUR|SECRETAIRE)\}' -and -not $used.HasFormula){$used.Value2=Resolve-ScoliaDocumentPersonnelText $values;continue}
  if($values -is [Array] -and $values.Rank -eq 2){
   $r0=$values.GetLowerBound(0);$c0=$values.GetLowerBound(1);$firstColumn=$used.Column
   for($r=$r0;$r -le $values.GetUpperBound(0);$r++){
    for($c=$c0;$c -le $values.GetUpperBound(1);$c++){
     $value=$values.GetValue($r,$c)
     if($value -isnot [string] -or $value -notmatch '\{(?:NOM_)?(?:PREFET|PROVISEUR|SECRETAIRE)\}'){continue}
     $cell=$sheet.Cells.Item(($used.Row+$r-$r0),($firstColumn+$c-$c0))
     if(-not $cell.HasFormula){$cell.Value2=Resolve-ScoliaDocumentPersonnelText $value}
    }
   }
  }
 }
}
