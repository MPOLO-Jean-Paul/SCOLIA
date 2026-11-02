$script:scoliaNetworkHost=$null
$script:scoliaNetworkCheckUtc=[datetime]::MinValue
$script:scoliaHostingFile=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'configuration\reseau-scolia.json'
function Initialize-ScoliaNetworkCode {
 if(-not ('ScoliaNetwork' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ScoliaNetwork.cs') -ReferencedAssemblies System,System.Xml,System.Core,System.Runtime.Serialization};[ScoliaNetwork]::WorkerScript=Join-Path $PSScriptRoot 'ScoliaProbe.ps1'
}
function Get-ScoliaPrivateNetworks {
 $list=@();foreach($profile in @(Get-NetConnectionProfile -ErrorAction SilentlyContinue|Where-Object {$_.NetworkCategory -in @('Private','DomainAuthenticated')})){
  foreach($ip in @(Get-NetIPAddress -InterfaceIndex $profile.InterfaceIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue|Where-Object {$_.IPAddress -notlike '169.254.*' -and $_.IPAddress -ne '127.0.0.1' -and $_.PrefixLength -ge 8 -and $_.PrefixLength -le 30})){$list+=($ip.IPAddress+'/'+$ip.PrefixLength)}
 };return @($list|Select-Object -Unique)
}
function Stop-ScoliaNetworkHost {if($script:scoliaNetworkHost){$script:scoliaNetworkHost.Dispose();$script:scoliaNetworkHost=$null}}
function Start-ScoliaNetworkHost {
 Stop-ScoliaNetworkHost
 if(-not $script:scolia -or -not [IO.File]::Exists($script:scoliaHostingFile)){return}
 try{$h=[IO.File]::ReadAllText($script:scoliaHostingFile)|ConvertFrom-Json;if([IO.Path]::GetFullPath($h.Root).TrimEnd('\') -ine $script:scoliaRoot.TrimEnd('\')){return};if($h.SchoolId -and $h.SchoolId -cne $script:scolia.Id){return}
  $networks=@(Get-ScoliaPrivateNetworks);if(-not $networks.Count){return};Initialize-ScoliaNetworkCode;$script:scoliaNetworkHost=New-Object ScoliaNetworkHost($script:scolia.Id,$script:scolia.School,$h.Share,[string[]]$networks)
 }catch{if($script:status){$script:status.Text='Découverte réseau indisponible ; le partage reste accessible par son adresse.'}}
}
function Assert-ScoliaNetworkJoin($profile,[string]$expectedId){
 Test-ScoliaProfile $profile
 if($script:scoliaRosterTask -and -not $script:scoliaRosterTask.IsCompleted){throw 'Attendez la fin de la publication du répertoire.'}
 if($expectedId -and $profile.Id -cne $expectedId){throw 'Ce dossier appartient à un autre établissement. La connexion mémorisée est conservée.'}
 if($script:scoliaSession -and $profile.Revision -ne $script:scoliaSession.Revision){throw 'Les paramètres partagés ont changé. Redémarrez SCOLIA pour authentifier le compte avec la configuration actuelle.'}
 if($script:scoliaSession -and -not @($profile.Accounts|Where-Object {(Get-ScoliaAccountId $_) -ceq $script:scoliaSession.AccountId}).Count){throw 'Le compte actuel nʼexiste plus dans cette configuration. Redémarrez et reconnectez-vous.'}
 if($script:scoliaLocalRoot -and [IO.File]::Exists((Join-Path $script:scoliaLocalRoot 'operation-caisse-en-attente.json'))){throw 'Vérifiez dʼabord lʼopération de caisse en attente avant de changer de connexion.'}
}
function Show-ScoliaNetwork([switch]$Setup,[string]$InitialPath='',[string]$ExpectedId='') {
 if(-not $Setup){Assert-ITMPermission 'Administrer';$ExpectedId=$script:scolia.Id;$InitialPath=$script:scoliaRoot}
 Initialize-ScoliaNetworkCode
 $resume=($script:scoliaTimer -and $script:scoliaTimer.Enabled);if($resume){$script:scoliaTimer.Stop()}
 $state=[pscustomobject]@{Task=$null;Process=$null;Request='';Kind='';PeerId='';Started=[datetime]::UtcNow;Result=$null;Busy=$false}
 $d=New-ScoliaDialog 'Réseau de lʼétablissement' 880 720
 $tabs=New-Object Windows.Forms.TabControl;$tabs.Dock='Fill';$d.Controls.Add($tabs)
 $hostPage=New-Object Windows.Forms.TabPage('Héberger');$joinPage=New-Object Windows.Forms.TabPage('Rejoindre');[void]$tabs.TabPages.Add($hostPage);[void]$tabs.TabPages.Add($joinPage)
 $t=New-ScoliaTable $hostPage;$folder=Add-ScoliaText $t 'Dossier de données local' $InitialPath;$folder.ReadOnly=(-not $Setup -or [bool]$ExpectedId)
 $account=Add-ScoliaText $t 'Compte Windows autorisé' ([Security.Principal.WindowsIdentity]::GetCurrent().Name)
 $share=Add-ScoliaText $t 'Nom du partage' ('SCOLIA_'+$(if($ExpectedId){$ExpectedId.Substring(0,8).ToUpperInvariant()}else{[guid]::NewGuid().ToString('N').Substring(0,8).ToUpperInvariant()}))
 if([IO.File]::Exists($script:scoliaHostingFile)){try{$saved=[IO.File]::ReadAllText($script:scoliaHostingFile)|ConvertFrom-Json;if($saved.Root -ieq $InitialPath){$share.Text=$saved.Share;$account.Text=$saved.Account}}catch{}}
 $network=New-Object Windows.Forms.ComboBox;$network.DropDownStyle='DropDownList';$profiles=@(Get-NetConnectionProfile -ErrorAction SilentlyContinue);$network.Items.AddRange([object[]]@($profiles|ForEach-Object {$_.Name+' · '+$_.NetworkCategory}));if($profiles.Count){$network.SelectedIndex=0};[void](Add-ScoliaField $t 'Réseau de lʼétablissement' $network)
 $trust=New-Object ITMReadableCheckBox;$trust.Text='Ce réseau est privé et appartient à lʼétablissement';$trust.AutoSize=$true;[void](Add-ScoliaField $t 'Réseau actuellement public' $trust)
 $explain=New-Label $t 'SCOLIA configure le partage et le pare-feu. Windows demandera lʼautorisation administrateur. Sur lʼautre poste, utilisez un compte Windows autorisé sur cet ordinateur. Aucun mot de passe Windows nʼest enregistré par SCOLIA.' 0 0 450 110;$explain.Dock='Top';$t.SetColumnSpan($explain,2);$t.SetCellPosition($explain,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$t.RowCount)));$t.RowCount++
 $hostButton=New-Button $t 'Configurer le partage automatiquement' 0 0 300;[void](Add-ScoliaField $t 'Créer ou vérifier' $hostButton)
 $jt=New-ScoliaTable $joinPage
 $search=New-Button $jt 'Rechercher les établissements' 0 0 280;[void](Add-ScoliaField $jt 'Découverte sur le réseau privé' $search)
 $peers=New-Object Windows.Forms.ComboBox;$peers.DropDownStyle='DropDownList';$peers.DisplayMember='School';[void](Add-ScoliaField $jt 'Établissements trouvés' $peers)
 $address=Add-ScoliaText $jt 'Adresse du dossier partagé' $(if($InitialPath.StartsWith('\\')){$InitialPath}else{''})
 $peers.Add_SelectedIndexChanged({if($peers.SelectedItem){$address.Text=$peers.SelectedItem.Root}})
 $access=New-Button $jt 'Autoriser lʼaccès Windows' 0 0 280;[void](Add-ScoliaField $jt 'Identifiants Windows si demandés' $access)
 $access.Add_Click({try{$target=[ScoliaNetwork]::ValidateRoot($address.Text);if(-not $target.StartsWith('\\')){throw 'Saisissez lʼadresse du partage réseau.'};Start-Process explorer.exe -ArgumentList ('"'+$target+'"')}catch{$status.Text=$_.Exception.Message}})
 $joinButton=New-Button $jt 'Vérifier et mémoriser la connexion' 0 0 310;[void](Add-ScoliaField $jt 'Connexion de cet ordinateur' $joinButton)
 $note=New-Label $jt 'La recherche détecte les postes hôtes sur lesquels SCOLIA est ouvert. Si la découverte est bloquée par le routeur, utilisez lʼadresse affichée sur le poste hôte. Le logiciel vérifie la configuration, lʼécriture et le remplacement atomique des fichiers avant de se connecter.' 0 0 480 110;$note.Dock='Top';$jt.SetColumnSpan($note,2);$jt.SetCellPosition($note,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$jt.RowCount)));$jt.RowCount++
 if($InitialPath.StartsWith('\\')){$tabs.SelectedTab=$joinPage}
 $status=New-Label $d 'Choisissez Héberger sur un poste, puis Rejoindre sur lʼautre.' 0 0 800 70;$status.Dock='Bottom';$status.Padding=New-Object Windows.Forms.Padding(16,8,16,8)
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.Height=60;$d.Controls.Add($bar);[void](Add-ScoliaButton $bar 'Fermer' {$d.Close()})
 $setBusy={param($value,$button,$text)$state.Busy=$value;foreach($c in @($hostButton,$search,$joinButton,$access,$folder,$account,$share,$network,$trust,$address,$peers)){$c.Enabled=-not $value};if($value){$status.Text=$text;Start-ITMActivity $text $button}else{Stop-ITMActivity}}
 $search.Add_Click({try{$nets=@(Get-ScoliaPrivateNetworks);if(-not $nets.Count){throw 'Aucun réseau privé IPv4 actif. Saisissez lʼadresse du partage ou configurez le réseau privé dans Windows.'};$peers.Items.Clear();$state.Kind='Discover';$state.Started=[datetime]::UtcNow;$state.Task=[ScoliaNetwork]::DiscoverAsync([string[]]$nets);& $setBusy $true $search 'Recherche des établissements…'}catch{$status.Text=$_.Exception.Message}})
 $joinButton.Add_Click({try{$target=[ScoliaNetwork]::ValidateRoot($address.Text);if(-not $target.StartsWith('\\')){throw 'Utilisez lʼadresse réseau \\ordinateur\partage.'};$state.PeerId='';if($peers.SelectedItem -and $peers.SelectedItem.Root -ieq $target){$state.PeerId=$peers.SelectedItem.SchoolId};$state.Kind='Probe';$state.Started=[datetime]::UtcNow;$state.Task=[ScoliaNetwork]::ProbeBoundedAsync($target,15000);& $setBusy $true $joinButton 'Vérification de la connexion réseau…'}catch{$status.Text=$_.Exception.Message}})
 $hostButton.Add_Click({try{
  if($network.SelectedIndex -lt 0){throw 'Choisissez le réseau actif.'};$root=[ScoliaNetwork]::ValidateRoot($folder.Text);if($root.StartsWith('\\')){throw 'Ce poste utilise déjà un partage distant. Configurez Héberger sur lʼordinateur contenant les données.'}
  if($share.Text -notmatch '^SCOLIA_[A-Z0-9_]{1,60}$'){throw 'Nom du partage : SCOLIA_ suivi de lettres majuscules, chiffres ou caractères _.'}
  if($ExpectedId -and $root.TrimEnd('\') -ine $InitialPath.TrimEnd('\')){throw 'Conservez le dossier de données existant.'}
  $selected=$profiles[$network.SelectedIndex];if($selected.NetworkCategory -eq 'Public' -and -not $trust.Checked){throw 'Confirmez que ce réseau public appartient à lʼétablissement avant de le rendre privé.'}
  if($script:scoliaRosterTask -and -not $script:scoliaRosterTask.IsCompleted){throw 'Attendez la fin de la publication du répertoire.'}
  $nonce=[guid]::NewGuid().ToString('N');$request=[pscustomobject]@{Nonce=$nonce;Root=$root;Share=$share.Text;Account=$account.Text;InterfaceIndex=$selected.InterfaceIndex;Trust=[bool]$trust.Checked;ApplicationRoot=[IO.Path]::GetDirectoryName($PSScriptRoot)}
  if($state.Request){foreach($old in @($state.Request,($state.Request+'.result.json'))){if([IO.File]::Exists($old)){[IO.File]::Delete($old)}}}
  $state.Request=Join-Path ([IO.Path]::GetDirectoryName($script:scoliaHostingFile)) ('reseau-demande-'+$nonce+'.json');Write-ScoliaJson $state.Request $request
  $helper=Join-Path $PSScriptRoot 'ScoliaReseauAdmin.ps1';$args='-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+$helper+'" -RequestPath "'+$state.Request+'"'
  $exe=Join-Path $env:windir 'System32\WindowsPowerShell\v1.0\powershell.exe';$state.Process=Start-Process $exe -ArgumentList $args -Verb RunAs -WindowStyle Hidden -PassThru
  $state.Kind='Host';$state.Started=[datetime]::UtcNow;& $setBusy $true $hostButton 'Configuration Windows du partage…'
 }catch{$status.Text=$_.Exception.Message}})
 $timer=New-Object Windows.Forms.Timer;$timer.Interval=150
 $timer.Add_Tick({if(-not $state.Busy){return};try{
  if($state.Kind -eq 'Host'){
   if(-not $state.Process.HasExited){return};$resultFile=$state.Request+'.result.json';if(-not [IO.File]::Exists($resultFile)){throw 'Windows nʼa pas terminé la configuration. Réessayez le partage.'};$r=[IO.File]::ReadAllText($resultFile)|ConvertFrom-Json;$req=[IO.File]::ReadAllText($state.Request)|ConvertFrom-Json;if($r.Nonce -cne $req.Nonce){throw 'Réponse de configuration invalide.'};if($r.Success -isnot [bool] -or -not $r.Success){throw ([string]$r.Error)};if($r.Root -ine $req.Root -or $r.Share -cne $req.Share){throw 'Le dossier du résultat ne correspond pas à la demande.'}
   Write-ScoliaJson $script:scoliaHostingFile ([pscustomobject]@{Root=$r.Root;Share=$r.Share;Account=$r.Account;Address=$r.Address;SchoolId=$ExpectedId})
   $status.Text='Partage prêt : '+$r.Address+' — compte Windows : '+$r.Account;$address.Text=$r.Address
   if($Setup -and -not $ExpectedId){$mode='Host';if([IO.File]::Exists((Join-Path $r.Root 'profil-scolia.json'))){$localProbe=[ScoliaNetwork]::Probe($r.Root);$localProfile=$localProbe.Profile|ConvertFrom-Json;Assert-ScoliaNetworkJoin $localProfile '';Set-ScoliaLocation $r.Root $localProfile;$mode='Join'};$state.Result=[pscustomobject]@{Mode=$mode;Root=$r.Root};$d.DialogResult='OK';$d.Close()}else{Start-ScoliaNetworkHost}
  }else{
   if(-not $state.Task.IsCompleted){if(([datetime]::UtcNow-$state.Started).TotalSeconds -gt 20){$status.Text='Windows attend le réseau. Vous pouvez fermer, vérifier les identifiants Windows, puis réessayer.'};return};if($state.Task.IsFaulted){throw $state.Task.Exception.GetBaseException()}
   if($state.Kind -eq 'Discover'){$found=@($state.Task.Result);foreach($peer in $found){[void]$peers.Items.Add($peer)};if($found.Count){$peers.SelectedIndex=0;$status.Text=$found.Count.ToString()+' établissement(s) trouvé(s).'}else{$status.Text='Aucun hôte détecté. Ouvrez SCOLIA sur le poste hôte ou utilisez son adresse réseau.'}}
   else{$probe=$state.Task.Result;$profile=$probe.Profile|ConvertFrom-Json -ErrorAction Stop;Assert-ScoliaNetworkJoin $profile $ExpectedId;if($state.PeerId -and $profile.Id -cne $state.PeerId){throw 'Lʼidentité de la configuration ne correspond pas à lʼannonce réseau.'}
    Set-ScoliaLocation $probe.Root $profile;Stop-ScoliaNetworkHost;$script:scoliaReadTask=$null;$script:scoliaLedgerStamp='';$state.Result=[pscustomobject]@{Mode='Join';Root=$probe.Root};if(-not $Setup){Apply-ITMInstitution;Set-ScoliaWorkspace};$d.DialogResult='OK';$d.Close()
   }
  }
 }catch{$status.Text=$_.Exception.Message}finally{if(($state.Kind -eq 'Host' -and $state.Process.HasExited) -or ($state.Kind -ne 'Host' -and $state.Task.IsCompleted)){& $setBusy $false $null ''; $state.Task=$null;$state.Process=$null;$state.Kind=''}}})
 $d.Add_FormClosing({param($sender,$e)if($state.Busy -and $state.Kind -eq 'Host' -and -not $state.Process.HasExited){$e.Cancel=$true;$status.Text='Attendez la fin de la configuration Windows avant de fermer.'}})
 Apply-ITMThemeForm $d;$timer.Start()
 try{[void]$d.ShowDialog();return $state.Result}finally{$timer.Stop();$timer.Dispose();if($state.Busy){Stop-ITMActivity};if($state.Request){foreach($f in @($state.Request,($state.Request+'.result.json'))){if([IO.File]::Exists($f)){[IO.File]::Delete($f)}}};$d.Dispose();if($resume){$script:scoliaTimer.Start()}}
}
function Open-ScoliaNetwork { [void](Show-ScoliaNetwork) }
function Read-ScoliaConfigurationWithRecovery {
 try{return (Read-ScoliaConfiguration)}catch{
  if(Test-Path function:Close-ScoliaTransition){Close-ScoliaTransition}
  $errorText=$_.Exception.Message;$pointer=[IO.File]::ReadAllText($script:scoliaPointer)|ConvertFrom-Json
  $d=New-ScoliaDialog 'Connexion mémorisée indisponible' 640 390;$t=New-ScoliaTable $d;$text=New-Label $t ($errorText+"`r`n"+$pointer.Root+"`r`nAllumez le poste hôte. La base existante reste conservée.") 0 0 560 150;$text.Dock='Top';$t.SetColumnSpan($text,2);$t.RowCount=1
  $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.Height=115;$d.Controls.Add($bar);$result=[pscustomobject]@{Connected=$false}
  [void](Add-ScoliaButton $bar 'Réessayer la connexion' {try{if(Read-ScoliaConfiguration){$result.Connected=$true;$d.Close()}}catch{$text.Text=$_.Exception.Message}})
  [void](Add-ScoliaButton $bar 'Rechercher le poste hôte' {try{$r=Show-ScoliaNetwork -Setup -InitialPath $pointer.Root -ExpectedId $pointer.Id;if($r -and $r.Mode -eq 'Join'){$result.Connected=$true;$d.Close()}}catch{$text.Text=$_.Exception.Message}})
  [void](Add-ScoliaButton $bar 'Quitter' {$d.Close()});Apply-ITMThemeForm $d;try{[void]$d.ShowDialog()}finally{$d.Dispose()};if(-not $result.Connected){throw 'Connexion annulée. La configuration et les données mémorisées sont conservées.'};return $true
 }
}
