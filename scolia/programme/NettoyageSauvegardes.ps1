# Nettoyage ciblé des copies complètes du dossier data actuellement connecté.
function Get-ITMBackupCleanupTree([string]$Path) {
    $root=Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if(-not $root.PSIsContainer -or ($root.Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Dossier de sauvegarde lié ou non valide.'}
    $pending=New-Object 'System.Collections.Generic.Stack[string]';$pending.Push($root.FullName)
    $files=New-Object 'System.Collections.Generic.List[object]';$directories=New-Object 'System.Collections.Generic.List[string]';$stamp=New-Object 'System.Collections.Generic.List[string]';$bytes=[long]0
    while($pending.Count){
        $folder=$pending.Pop();$directories.Add($folder)
        foreach($item in @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction Stop)){
            if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Une sauvegarde contient un lien : nettoyage exclu.'}
            $relative=$item.FullName.Substring($root.FullName.Length)
            if($item.PSIsContainer){$pending.Push($item.FullName);$stamp.Add('D|'+$relative)}
            else{$files.Add($item);$bytes+=$item.Length;$stamp.Add('F|'+$relative+'|'+$item.Length+'|'+$item.LastWriteTimeUtc.Ticks)}
        }
    }
    $hash=[Security.Cryptography.SHA256]::Create()
    try{$fingerprint=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes((@($stamp | Sort-Object) -join "`n"))))).Replace('-','')}finally{$hash.Dispose()}
    return [pscustomobject]@{Files=$files.ToArray();Directories=$directories.ToArray();Bytes=$bytes;Stamp=$fingerprint}
}
function Get-ITMBackupCleanupPlan {
    Assert-ITMPermission 'Administrer'
    if(-not $script:store -or -not $script:storeFolder){throw 'Connectez un dossier data pour nettoyer ses sauvegardes.'}
    $root=[IO.Path]::GetFullPath((Join-Path $script:storeFolder 'sauvegardes'))
    $rows=New-Object 'System.Collections.Generic.List[object]';$excluded=0
    if([IO.Directory]::Exists($root)){
        if((Get-Item -LiteralPath $root -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Le dossier des sauvegardes est un lien : nettoyage bloqué.'}
        foreach($folder in @(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction Stop)){
            try{
                if($folder.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Lien exclu.'}
                $registry=Join-Path $folder.FullName 'registre.json';$manifest=Join-Path $folder.FullName 'empreintes.json'
                if(-not [IO.File]::Exists($registry) -or -not [IO.File]::Exists($manifest)){throw 'Dossier incomplet exclu.'}
                $tree=Get-ITMBackupCleanupTree $folder.FullName
                $index=Get-Content -LiteralPath $registry -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
                if([string]$index.Id -cne [string]$script:store.Id){throw 'Autre base exclue.'}
                $hashes=Get-Content -LiteralPath $manifest -Raw -Encoding UTF8 | ConvertFrom-Json -ErrorAction Stop
                Assert-ITMBackupManifest $index $hashes
                $created=(Get-Item -LiteralPath $registry).CreationTimeUtc;$date=[datetimeoffset]$created
                if($folder.Name -match '^(\d{8}-\d{6})-'){$date=[datetimeoffset][datetime]::ParseExact($Matches[1],'yyyyMMdd-HHmmss',[Globalization.CultureInfo]::InvariantCulture)}
                $rows.Add([pscustomobject]@{Folder=$folder.FullName;Name=$folder.Name;Date=$date;Created=$created;Bytes=[long]$tree.Bytes;Stamp=$tree.Stamp;RegistryHash=(Get-FileHash -LiteralPath $registry -Algorithm SHA256).Hash;ManifestHash=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash})
            }catch{$excluded++}
        }
    }
    $ordered=@($rows | Sort-Object @{Expression='Date';Descending=$true},@{Expression='Created';Descending=$true},@{Expression='Name';Descending=$true})
    $keep=$null;$delete=@();$bytes=[long]0
    if($ordered.Count){$keep=$ordered[0];[void](Test-ITMBackupIntegrity $keep.Folder);if($ordered.Count -gt 1){$delete=@($ordered | Select-Object -Skip 1);foreach($row in $delete){$bytes+=$row.Bytes}}}
    $hash=[Security.Cryptography.SHA256]::Create()
    try{$fingerprint=([BitConverter]::ToString($hash.ComputeHash([Text.Encoding]::UTF8.GetBytes((@($ordered | ForEach-Object {$_.Folder+'|'+$_.Stamp+'|'+$_.RegistryHash+'|'+$_.ManifestHash}) -join "`n"))))).Replace('-','')}finally{$hash.Dispose()}
    return [pscustomobject]@{DataSet=[string]$script:store.Id;Root=$root;Keep=$keep;Delete=$delete;Bytes=$bytes;Excluded=$excluded;Stamp=$fingerprint}
}
function Invoke-ITMBackupCleanup($ApprovedPlan) {
    Assert-ITMPermission 'Administrer'
    if(-not $ApprovedPlan -or -not $script:store -or $ApprovedPlan.DataSet -cne [string]$script:store.Id){throw 'Le dossier de données a changé. Rouvrez le nettoyage.'}
    $autoGate=$null;$maintenanceGate=$null
    try{
        try{
            $autoGate=[IO.File]::Open((Join-Path $script:storeFolder 'sauvegardes-auto.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            $maintenanceGate=[IO.File]::Open((Join-Path $script:storeFolder 'sauvegardes-entretien.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
        }catch{throw 'Une sauvegarde ou un autre nettoyage est en cours. Réessayez après sa fin.'}
        $plan=Get-ITMBackupCleanupPlan
        if($plan.Stamp -cne $ApprovedPlan.Stamp -or $plan.Root -cne $ApprovedPlan.Root){throw 'Les sauvegardes ont changé. Actualisez la liste avant de confirmer.'}
        $deleted=0;$freed=[long]0;$failures=New-Object 'System.Collections.Generic.List[string]'
        foreach($row in $plan.Delete){
            try{
                $full=[IO.Path]::GetFullPath($row.Folder)
                if([IO.Path]::GetDirectoryName($full) -cne $plan.Root -or $full -ceq $plan.Keep.Folder){throw 'Copie hors du périmètre autorisé.'}
                [void](Test-ITMBackupIntegrity $plan.Keep.Folder)
                $tree=Get-ITMBackupCleanupTree $full
                if($tree.Stamp -cne $row.Stamp){throw 'Copie modifiée depuis la confirmation.'}
                # Remove files explicitly; never recursively follow a directory link.
                # Identity metadata stays until all payload files have been deleted, so a failed removal can be retried.
                $files=@($tree.Files | Sort-Object @{Expression={if($_.Name -in @('registre.json','empreintes.json')){1}else{0}}},FullName)
                foreach($file in $files){$current=Get-Item -LiteralPath $file.FullName -Force -ErrorAction Stop;if($current.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Lien apparu pendant le nettoyage.'};if($current.IsReadOnly){$current.IsReadOnly=$false};[IO.File]::Delete($current.FullName)}
                foreach($directory in @($tree.Directories | Sort-Object Length -Descending)){[IO.Directory]::Delete($directory,$false)}
                $deleted++;$freed+=$row.Bytes
            }catch{$failures.Add($row.Name+' : '+$_.Exception.Message)}
        }
        if($plan.Keep){[void](Test-ITMBackupIntegrity $plan.Keep.Folder)}
        if($deleted){Write-ITMUserAudit ('Nettoyage des sauvegardes : '+$deleted+' copie(s) supprimée(s), '+$freed+' octets libérés')}
        return [pscustomobject]@{Deleted=$deleted;FreedBytes=$freed;Kept=$(if($plan.Keep){$plan.Keep.Folder}else{''});Failures=$failures.ToArray();Excluded=$plan.Excluded}
    }finally{if($maintenanceGate){$maintenanceGate.Dispose()};if($autoGate){$autoGate.Dispose()}}
}
function Show-ITMBackupCleanup {
    Assert-ITMPermission 'Administrer';if($script:busy){return}
    $script:backupCleanupPlan=$null
    Do-Work {$script:backupCleanupPlan=Get-ITMBackupCleanupPlan} 'Vérification des sauvegardes et de l espace occupé…'
    $plan=$script:backupCleanupPlan;if(-not $plan){return}
    $state=New-ScoliaFeatureDialog 'Nettoyage des sauvegardes' 'Conservez la dernière sauvegarde vérifiée et libérez lʼespace des anciennes copies de cette base.' 980 680
    $state.Form.MinimumSize=New-Object Drawing.Size(320,540)
    $state|Add-Member -NotePropertyName Plan -NotePropertyValue $plan
    $refresh={param($s)
        $rows=@($s.Plan.Delete | ForEach-Object {[pscustomobject]@{Nom=$_.Name;Date=$_.Date.ToString('dd/MM/yyyy HH:mm:ss');Taille=('{0:N2} Mo' -f ($_.Bytes/1MB))}})
        Set-Grid $s.Grid $rows @(@('Nom','Ancienne copie à supprimer'),@('Date','Date de sauvegarde'),@('Taille','Espace occupé'))
        $s.Message.Text=$(if($s.Plan.Keep){'Copie conservée : '+$s.Plan.Keep.Name+"`r`n"+$s.Plan.Delete.Count+' ancienne(s) copie(s) · espace à libérer : '+('{0:N2} Mo' -f ($s.Plan.Bytes/1MB))}else{'Aucune sauvegarde complète de cette base à nettoyer.'})
        if($s.Plan.Excluded){$s.Message.Text+="`r`n"+$s.Plan.Excluded+' dossier(s) non admissible(s) conservé(s).'}
        $s.DeleteButton.Enabled=$s.Plan.Delete.Count -gt 0
    }
    $state|Add-Member -NotePropertyName RefreshView -NotePropertyValue $refresh
    $delete=Add-ScoliaButton $state.Actions 'Supprimer les anciennes copies' {
        param($sender,$e)if($script:busy){return};$s=$sender.FindForm().Tag
        try{
            $message='Supprimer '+$s.Plan.Delete.Count+' ancienne(s) sauvegarde(s) et conserver '+$s.Plan.Keep.Name+' ?'
            if([ITMMessageBox]::Show($s.Form,$message,'Nettoyage des sauvegardes','YesNo','Warning',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes'){return}
            $script:backupCleanupResult=$null
            Do-Work {$script:backupCleanupResult=Invoke-ITMBackupCleanup $s.Plan} 'Nettoyage des anciennes sauvegardes…'
            $result=$script:backupCleanupResult;if(-not $result){return}
            $s.Plan=Get-ITMBackupCleanupPlan;& $s.RefreshView $s
            $s.Message.Text=$result.Deleted+' copie(s) supprimée(s) · '+('{0:N2} Mo' -f ($result.FreedBytes/1MB))+' libérés.'+"`r`n"+'Dernière sauvegarde conservée : '+[IO.Path]::GetFileName($result.Kept)
            if($result.Failures.Count){$s.Message.Tag='scolia:feedback:error';$s.Message.Text+="`r`n"+($result.Failures -join "`r`n")}
            if($script:book -and $script:data){Refresh-Data}
        }catch{$s.Message.Tag='scolia:feedback:error';$s.Message.Text=$_.Exception.Message}
    };$delete.Destructive=$true;$state|Add-Member -NotePropertyName DeleteButton -NotePropertyValue $delete
    [void](Add-ScoliaButton $state.Actions 'Actualiser' {param($sender,$e)if($script:busy){return};$s=$sender.FindForm().Tag;try{Do-Work {$s.Plan=Get-ITMBackupCleanupPlan} 'Actualisation des sauvegardes…';& $s.RefreshView $s}catch{$s.Message.Text=$_.Exception.Message}})
    $close=Add-ScoliaButton $state.Actions 'Fermer' {param($sender,$e)if(-not $script:busy){$sender.FindForm().Close()}};$state.Form.CancelButton=$close
    $state.Form.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}})
    & $refresh $state
    try{[void]$state.Form.ShowDialog($script:main)}finally{$state.Form.Dispose();$script:backupCleanupPlan=$null;$script:backupCleanupResult=$null}
}
