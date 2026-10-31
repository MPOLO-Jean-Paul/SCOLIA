param([Parameter(Mandatory=$true)][string]$RequestPath)
# Executed only in elevated Windows PowerShell; never dot-sourced by SCOLIA.
$ErrorActionPreference='Stop';$request=$null;$newShare=$false;$newRules=@();$originalAcl=$null;$changedCategory=$false;$folderCreated=$false
try {
 $request=[IO.File]::ReadAllText($RequestPath)|ConvertFrom-Json
 if($request.Nonce -notmatch '^[a-f0-9]{32}$' -or $request.Share -notmatch '^SCOLIA_[A-Z0-9_]{1,60}$' -or [int]$request.InterfaceIndex -le 0){throw 'Demande réseau invalide.'}
 $root=[IO.Path]::GetFullPath([string]$request.Root);$drive=[IO.Path]::GetPathRoot($root)
 if($root.TrimEnd('\') -ieq ([IO.Path]::GetFullPath([string]$request.ApplicationRoot)).TrimEnd('\')){throw 'Choisissez un sous-dossier réservé aux données, pas le dossier du programme.'}
 if($root.StartsWith('\\') -or $root.TrimEnd('\') -eq $drive.TrimEnd('\') -or $root.StartsWith($env:windir,[StringComparison]::OrdinalIgnoreCase)){throw 'Hébergez uniquement un dossier de données local, hors des dossiers Windows.'}
 $cursor=New-Object IO.DirectoryInfo($root);while($cursor){if($cursor.Exists -and ($cursor.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Utilisez un dossier local direct, sans lien ni dossier synchronisé.'};$cursor=$cursor.Parent}
 $identity=New-Object Security.Principal.NTAccount([string]$request.Account);$sid=$identity.Translate([Security.Principal.SecurityIdentifier]);$account=$sid.Translate([Security.Principal.NTAccount]).Value
 $profile=Get-NetConnectionProfile -InterfaceIndex ([int]$request.InterfaceIndex)
 if($profile.NetworkCategory -eq 'Public'){if($request.Trust -ne $true){throw 'Confirmez que ce réseau est le réseau privé de lʼétablissement.'};Set-NetConnectionProfile -InterfaceIndex $profile.InterfaceIndex -NetworkCategory Private;$changedCategory=$true}
 $share=Get-SmbShare -Name $request.Share -ErrorAction SilentlyContinue
 if($share -and ([IO.Path]::GetFullPath($share.Path).TrimEnd('\') -ine $root.TrimEnd('\'))){throw 'Ce nom de partage appartient à un autre dossier.'}
 # Existing shares are reused only with an existing explicit authorisation. No widening of third-party ACLs.
 if($share -and $share.Description -ne 'SCOLIA - données de lʼétablissement'){throw 'Ce partage nʼa pas été créé par lʼassistant SCOLIA. Utilisez un autre nom.'}
 if($share){$access=@(Get-SmbShareAccess -Name $request.Share|Where-Object {$_.AccountName -ieq $account -and $_.AccessControlType -eq 'Allow' -and $_.AccessRight -in @('Change','Full')});if(-not $access.Count){throw 'Ce partage existe pour un autre compte Windows. Choisissez le compte déjà autorisé ou un nouveau nom de partage.'}}
 if(-not [IO.Directory]::Exists($root)){[void][IO.Directory]::CreateDirectory($root);$folderCreated=$true}
 $originalAcl=Get-Acl -LiteralPath $root;$acl=Get-Acl -LiteralPath $root
 $rule=New-Object Security.AccessControl.FileSystemAccessRule($sid,'Modify','ContainerInherit,ObjectInherit','None','Allow');$acl.AddAccessRule($rule);Set-Acl -LiteralPath $root -AclObject $acl
 if(-not $share){New-SmbShare -Name $request.Share -Path $root -ChangeAccess $account -CachingMode None -Description 'SCOLIA - données de lʼétablissement'|Out-Null;$newShare=$true}
 foreach($port in @(@('TCP',445),@('UDP',48629))){$name='SCOLIA-'+$request.Share+'-'+$port[0]+'-'+$port[1];$existing=Get-NetFirewallRule -Name $name -ErrorAction SilentlyContinue;if($existing){if($existing.Description -ne 'SCOLIA réseau 99'){throw 'Une règle de pare-feu étrangère porte ce nom.'}}else{New-NetFirewallRule -Name $name -DisplayName $name -Description 'SCOLIA réseau 99' -Direction Inbound -Action Allow -Protocol $port[0] -LocalPort $port[1] -Profile Private,Domain -RemoteAddress LocalSubnet|Out-Null;$newRules+=$name}}
 $result=[pscustomobject]@{Nonce=$request.Nonce;Success=$true;Root=$root;Share=$request.Share;Account=$account;Address=('\\'+$env:COMPUTERNAME+'\'+$request.Share);Error=''}
} catch {
 $failure=$_.Exception.Message
 foreach($name in $newRules){Remove-NetFirewallRule -Name $name -ErrorAction SilentlyContinue}
 if($newShare){Remove-SmbShare -Name $request.Share -Force -ErrorAction SilentlyContinue}
 if($originalAcl){Set-Acl -LiteralPath $root -AclObject $originalAcl -ErrorAction SilentlyContinue}
 if($changedCategory){Set-NetConnectionProfile -InterfaceIndex $profile.InterfaceIndex -NetworkCategory Public -ErrorAction SilentlyContinue}
 if($folderCreated -and [IO.Directory]::Exists($root) -and [IO.Directory]::GetFileSystemEntries($root).Length -eq 0){[IO.Directory]::Delete($root)}
 $result=[pscustomobject]@{Nonce=$(if($request){$request.Nonce}else{''});Success=$false;Error=$failure}
}
$temp=$RequestPath+'.result.tmp';[IO.File]::WriteAllText($temp,($result|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($true));[IO.File]::Move($temp,$RequestPath+'.result.json')
