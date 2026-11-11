$ErrorActionPreference='Stop'
if(-not ('ScoliaNetwork' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaNetwork.cs') -ReferencedAssemblies System,System.Xml,System.Core,System.Runtime.Serialization}
function Assert($condition,[string]$message){if(-not $condition){throw $message}}
function MustFail([scriptblock]$action,[string]$message){$failed=$false;try{& $action}catch{$failed=$true};Assert $failed $message}
$nonce='aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa';$school='bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb';$nets=[string[]]@('127.0.0.1/8')
$reply='SCOLIA|1|'+$nonce+'|'+$school+'|'+[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('École test'))+'|SCOLIA_TEST|POSTE-ECOLE'
$peer=[ScoliaNetwork]::ParseReply([Text.Encoding]::UTF8.GetBytes($reply),[Net.IPAddress]::Loopback,$nonce,$nets)
Assert ($peer.School -ceq 'École test' -and $peer.Root -ceq '\\POSTE-ECOLE\SCOLIA_TEST') 'Annonce ou nom persistant incorrect.'
Assert ($null -eq [ScoliaNetwork]::ParseReply([Text.Encoding]::UTF8.GetBytes($reply),[Net.IPAddress]::Parse('192.168.1.3'),$nonce,$nets)) 'Annonce hors réseau acceptée.'
Assert ($null -eq [ScoliaNetwork]::ParseReply([Text.Encoding]::UTF8.GetBytes($reply),[Net.IPAddress]::Loopback,$school,$nets)) 'Réponse dʼune autre recherche acceptée.'
foreach($invalid in @($reply.Replace('SCOLIA_TEST','..\autre'),$reply.Replace('POSTE-ECOLE','../host'),$reply.Replace($school,'invalid'),$reply.Replace('|1|','|2|'),('X'*2049))){Assert ($null -eq [ScoliaNetwork]::ParseReply([Text.Encoding]::UTF8.GetBytes($invalid),[Net.IPAddress]::Loopback,$nonce,$nets)) 'Annonce invalide acceptée.'}
Assert ([ScoliaNetwork]::Broadcast('192.168.4.5/23') -eq '192.168.5.255') 'Diffusion sous-réseau incorrecte.'
$hoster=New-Object ScoliaNetworkHost($school,'École test','SCOLIA_TEST',$nets)
$udp=New-Object Net.Sockets.UdpClient;$udp.Client.ReceiveTimeout=2500
try{$data=[Text.Encoding]::ASCII.GetBytes('SCOLIA_DISCOVER_V1|'+$nonce);[void]$udp.Send($data,$data.Length,'127.0.0.1',48629);$remote=New-Object Net.IPEndPoint([Net.IPAddress]::Any,0);$received=$udp.Receive([ref]$remote);$peer=[ScoliaNetwork]::ParseReply($received,$remote.Address,$nonce,$nets);Assert ($peer.SchoolId -eq $school) 'Dialogue UDP hôte/client impossible.'}finally{$udp.Close();$hoster.Dispose()}
$root=Join-Path ([IO.Path]::GetTempPath()) ('scolia-network-'+[guid]::NewGuid().ToString('N'));[void][IO.Directory]::CreateDirectory($root)
try{[IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),'profil initial');$probe=[ScoliaNetwork]::Probe($root);Assert ($probe.Profile -ceq 'profil initial') 'Profil modifié par le contrôle réseau.';Assert ([IO.Directory]::GetFiles($root).Length -eq 1) 'Fichiers de contrôle non nettoyés.';MustFail {[void][ScoliaNetwork]::Probe((Join-Path $root 'absent'))} 'Dossier absent accepté.';[IO.File]::WriteAllText((Join-Path $root 'profil-scolia.json'),('x'*1048577));MustFail {[void][ScoliaNetwork]::Probe($root)} 'Configuration surdimensionnée acceptée.'}finally{[IO.Directory]::Delete($root,$true)}
# Extract only the join gate; no WinForms or Windows administration needed here.
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'ScoliaReseau.ps1'),[ref]$tokens,[ref]$errors)
$f=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Assert-ScoliaNetworkJoin'},$true);. ([scriptblock]::Create($f.Extent.Text))
function Test-ScoliaProfile($profile){if(-not $profile.Id){throw 'Profil invalide'}}
$script:scoliaSession=[pscustomobject]@{Role='Secrétariat';Revision=1};$profile=[pscustomobject]@{Id=$school;Revision=1;Accounts=@([pscustomobject]@{Role='Secrétariat'})}
Assert-ScoliaNetworkJoin $profile $school
MustFail {Assert-ScoliaNetworkJoin $profile $nonce} 'Autre institution acceptée.'
$profile.Revision=2;MustFail {Assert-ScoliaNetworkJoin $profile $school} 'Session périmée acceptée.';$profile.Revision=1
$profile.Accounts=@();MustFail {Assert-ScoliaNetworkJoin $profile $school} 'Compte absent accepté.';$profile.Accounts=@([pscustomobject]@{Role='Secrétariat'})
$script:scoliaRosterTask=[pscustomobject]@{IsCompleted=$false};MustFail {Assert-ScoliaNetworkJoin $profile $school} 'Changement pendant publication accepté.';$script:scoliaRosterTask=$null
[void][IO.Directory]::CreateDirectory($root);$script:scoliaLocalRoot=$root
try{[IO.File]::WriteAllText((Join-Path $root 'operation-caisse-en-attente.json'),'pending');MustFail {Assert-ScoliaNetworkJoin $profile $school} 'Opération de caisse en attente ignorée.'}finally{[IO.Directory]::Delete($root,$true)}
'PASS réseau : protocole borné, source/nonce/identité, nom hôte stable, dialogue UDP réel, lecture/écriture/remplacement atomique, nettoyage et refus des connexions incompatibles.'

# Evaluate the cleanup arrays used by the production assistant (regression 100).
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'ScoliaReseau.ps1'))
$matches=[regex]::Matches($source,'@\(\$state\.Request,\(\$state\.Request\+''\.result\.json''\)\)')
Assert ($matches.Count -eq 2) 'Expressions de nettoyage absentes.'
$state=[pscustomobject]@{Request='request-test.json'}
foreach($match in $matches){$paths=& ([scriptblock]::Create($match.Value));Assert ($paths.Count -eq 2 -and $paths[0] -ceq 'request-test.json' -and $paths[1] -ceq 'request-test.json.result.json') 'Nettoyage coupe le nom du résultat.'}
MustFail {[ScoliaNetwork]::ProbeBoundedAsync('x',999)} 'Délai inférieur au minimum accepté.'
MustFail {[ScoliaNetwork]::ProbeBoundedAsync('x',30001)} 'Délai supérieur au maximum accepté.'
'SUCCESS : nettoyage exact des deux fichiers et bornes du délai réseau.'
