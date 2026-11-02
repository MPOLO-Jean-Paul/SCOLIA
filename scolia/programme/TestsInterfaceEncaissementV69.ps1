# Vérification du formulaire réel sur Windows, sans enregistrement de paiement.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
if($PSVersionTable.PSVersion.Major -le 5){Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization}else{Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs')}
. (Join-Path $PSScriptRoot 'ReferencesRecusV69.ps1')
. (Join-Path $PSScriptRoot 'PaiementsDirectsV71.ps1')
function Import-CashFunction($file,$name){$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $file),[ref]$t,[ref]$e);$n=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);return [scriptblock]::Create($n.Extent.Text)}
foreach($name in @('New-ScoliaDialog','New-ScoliaTable','Add-ScoliaField','Add-ScoliaText','Add-ScoliaButton')){. (Import-CashFunction 'ScoliaConfiguration.ps1' $name)}
. (Import-CashFunction 'ScoliaIntegration.ps1' 'Show-ScoliaEntry')
function Set-ITMWindowIcon($form){}
function Apply-ITMThemeForm($form){}
function Assert-ITMPermission($permission){}
function Enable-ITMFluidTable($table){throw 'Le formulaire de caisse utilise le réagencement générique.'}
function New-Label($parent,$text,$x,$y,$w,$h,$size=10,$bold=$false){$c=New-Object ITMReadableLabel;$c.Text=$text;$c.SetBounds($x,$y,$w,$h);$c.Font=New-Object Drawing.Font('Segoe UI',$size,$(if($bold){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular}));$parent.Controls.Add($c);return $c}
function New-Button($parent,$text,$x,$y,$w,$primary=$false){$c=New-Object ITMLoginButton;$c.Text=$text;$c.SetBounds($x,$y,$w,38);$parent.Controls.Add($c);return $c}
function Get-ScoliaImportedLearners {return [pscustomobject]@{Nom='APPRENANT';PostNom='TEST';Prenom='Jean Paul';Promotion='INF1';Matricule='INF001';Sexe='M';Naissance='2000-01-01';Lieu='Lubumbashi';Contact1='0990000000';Contact2='';Adresse='Adresse complète'}}
function Get-ScoliaTypedAmount($text,$maximum){return [decimal]$text}
$temp=Join-Path ([IO.Path]::GetTempPath()) ('scolia-cash-layout-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($temp)
$script:scoliaLocalRoot=$temp;$script:scoliaRoot=$temp;$script:scolia=[pscustomobject]@{Id=([guid]::NewGuid().ToString('N'));Year='2026-2027';Revision=1;StartMonth=9;Months=10;Classes=@([pscustomobject]@{Code='INF1';Fee=100})}
$script:scolia|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $temp 'profil-scolia.json') -Encoding UTF8
$script:payMat='INF001';$script:main=$null;$state=$null
try{
 $state=Show-ScoliaEntry -LayoutOnly
 if(-not $state.Reference.ReadOnly -or $state.Reference.Text -cne $state.Identity.Reference){throw 'Référence non automatique ou modifiable.'}
 $state.Form.Show();[Windows.Forms.Application]::DoEvents()
 foreach($width in @(820,680,520)){
  $state.Form.ClientSize=New-Object Drawing.Size($width,550);[Windows.Forms.Application]::DoEvents();$state.Form.PerformLayout()
  if($state.Form.VerticalScroll.Visible -or $state.Table.VerticalScroll.Visible -or $state.Table.HorizontalScroll.Visible){throw 'Défilement présent dans la vue compacte.'}
  foreach($control in @($state.Table.Controls)){if($control.Bottom -gt $state.Table.ClientSize.Height -or $control.Right -gt $state.Table.ClientSize.Width){throw ('Champ coupé à '+$width+' : '+$control.AccessibleName)}}
  if($state.Footer.Bottom -gt $state.Form.ClientSize.Height){throw 'Commandes hors fenêtre.'}
 }
 'PASS : formulaire réel compact, référence en lecture seule, champs et commandes visibles sans défilement aux largeurs 520–820.'
}finally{if($state){$state.Poll.Dispose();$state.Form.Dispose()};if([IO.Directory]::Exists($temp)){[IO.Directory]::Delete($temp,$true)}}
