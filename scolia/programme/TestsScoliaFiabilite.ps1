$ErrorActionPreference='Stop'
Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaLedger.cs') -ReferencedAssemblies System.Xml,System.Core,System.Runtime.Serialization
function Import-Function([string]$name){$t=$null;$e=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaConfiguration.ps1'),[ref]$t,[ref]$e);$n=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);. ([scriptblock]::Create('function global:'+ $name + $n.Extent.Text.Substring(('function '+$name).Length)))}
foreach($name in @('Test-ScoliaAccountActive','Test-ScoliaAccountAdmin','Get-ScoliaAccountId','ConvertFrom-ScoliaClassText','New-ScoliaSettingsCandidate','Test-ScoliaProfile','Assert-ScoliaTariffChange','Get-ScoliaAttemptUntil')){Import-Function $name}
function Promo($s){return ([string]$s).Trim().ToUpperInvariant()}
$script:checks=0
function Test-ITMSecret($password,$secret){$script:checks++;return $password -ceq 'correct-password'}
function New-ITMSecret {return [pscustomobject]@{Salt=$zero;Hash=$zero}}
function Reject([scriptblock]$work,[string]$reason){$failed=$false;try{& $work}catch{$failed=$true};if(-not $failed){throw ('Accepté à tort : '+$reason)}}
$zero=[Convert]::ToBase64String((New-Object byte[] 32));$profile=[pscustomobject]@{Version=1;Revision=1;Id=[guid]::NewGuid().ToString('N');School='École';Year='2026 - 2027';Months=10;StartMonth=9;Classes=@([pscustomobject]@{Code='INF1';Fee=100});Accounts=@([pscustomobject]@{Role='Secrétariat';Locked=$true;Secret=[pscustomobject]@{Salt=$zero;Hash=$zero}},[pscustomobject]@{Role='Comptabilité';Locked=$true;Secret=[pscustomobject]@{Salt=$zero;Hash=$zero}})}
$controls=@{};foreach($a in $profile.Accounts){$controls[$a.Role]=[pscustomobject]@{Locked=[pscustomobject]@{Checked=$false};Password=[pscustomobject]@{Text=''};Confirm=[pscustomobject]@{Text=''};Current=[pscustomobject]@{Text='correct-password'}}}
$baseline=$profile|ConvertTo-Json -Depth 12 -Compress
$controls['Comptabilité'].Current.Text='incorrect'
Reject {New-ScoliaSettingsCandidate $profile 'École' '2026-2027' 10 9 'INF1;100' $controls} 'modification de la deuxième protection sans mot de passe'
if(($profile|ConvertTo-Json -Depth 12 -Compress) -cne $baseline){throw 'Profil modifié après échec de validation'}
$controls['Secrétariat'].Current.Text='incorrect';$controls['Comptabilité'].Current.Text='correct-password'
Reject {New-ScoliaSettingsCandidate $profile 'École' '2026-2027' 10 9 'INF1;100' $controls} 'deuxième tentative contournant la protection du premier compte'
$controls['Secrétariat'].Current.Text='correct-password';$candidate=New-ScoliaSettingsCandidate $profile 'École' '2026-2027' 10 9 'INF1;100' $controls
if($candidate.Accounts[0].Locked -or -not $profile.Accounts[0].Locked){throw 'Copie de travail non indépendante'}
$entry=[pscustomobject]@{Annee='2026-2027'};$roster=New-Object ScoliaRoster
$candidate.Classes[0].Fee=200;Reject {Assert-ScoliaTariffChange $profile $candidate @($entry) $roster} 'tarif contourné par espaces dans année'
$candidate.Classes[0].Fee=100;$candidate.Months=9;Reject {Assert-ScoliaTariffChange $profile $candidate @($entry) $roster} 'nombre de mois modifié pour année avec paiement'
$candidate.Months=10;$candidate.StartMonth=8;Reject {Assert-ScoliaTariffChange $profile $candidate @($entry) $roster} 'premier mois modifié pour année avec paiement'
$candidate.StartMonth=9;Assert-ScoliaTariffChange $profile $candidate @($entry) $roster
$learner=New-Object ScoliaLearner;$part=New-Object ScoliaPart;$part.Mois='Septembre';$part.Montant=100;$learner.Covered.Add($part);$roster.Learners.Add($learner);$candidate.Classes[0].Fee=200
Reject {Assert-ScoliaTariffChange $profile $candidate @() $roster} 'tarif changé malgré prise en charge'
$candidate.Year='2027 - 2028';Assert-ScoliaTariffChange $profile $candidate @($entry) $roster
$utc=[datetime]::UtcNow.AddMinutes(-1);if((Get-ScoliaAttemptUntil $utc) -ne $utc -or (Get-ScoliaAttemptUntil $utc.ToString('o')) -ne $utc){throw 'Expiration du blocage convertie dans mauvais fuseau'}
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-integrity-'+[guid]::NewGuid().ToString('N'))
try{
 Reject {[ScoliaLedger]::Read($root,$profile.Id)} 'partage absent présenté comme journal vide'
 Reject {[ScoliaSharing]::ReadRoster($root,$profile.Id,$profile.Year)} 'partage absent présenté comme répertoire vide'
 [void][IO.Directory]::CreateDirectory($root);Reject {[ScoliaLedger]::Read($root,$profile.Id)} 'configuration manquante présentée comme caisse vide'
 $file=Join-Path $root 'profil-scolia.json';[IO.File]::WriteAllText($file,($profile|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($true))
 if([ScoliaLedger]::Read($root,$profile.Id).Count -ne 0){throw 'Premier journal vide valide refusé'}
 $roster.SchoolId=$profile.Id;$roster.Year=$profile.Year;[ScoliaSharing]::Publish($root,$roster);$saved=[IO.File]::ReadAllBytes((Join-Path $root ('roster-'+$profile.Id+'-2026-2027.xml')))
 $roster.Year='2027-2028';Reject {[ScoliaSharing]::Publish($root,$roster)} 'répertoire dʼune autre année publié'
 $profile.Id=[guid]::NewGuid().ToString('N');[IO.File]::WriteAllText($file,($profile|ConvertTo-Json -Depth 12));Reject {[ScoliaLedger]::Read($root,$roster.SchoolId)} 'configuration dʼautre école présentée comme caisse vide'
 [IO.File]::Delete($file);Reject {[ScoliaSharing]::ReadAsync($root,$roster.SchoolId,'2026-2027').GetAwaiter().GetResult()} 'lecture asynchrone acceptée après perte de configuration'
 'PASS tarifs/calendrier protégés malgré espaces, prises en charge protégées, validation sans mutation, mots de passe requis à chaque essai, expiration UTC, partage absent refusé, répertoire isolé par année'
}finally{if([IO.Directory]::Exists($root)){[IO.Directory]::Delete($root,$true)}}
