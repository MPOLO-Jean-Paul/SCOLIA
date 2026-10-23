if(-not (Get-Command Import-ITMCompiledSource -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
if(-not ('ITMActivity' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing}
$script:activity=$null;$script:activityDepth=0;$script:activitySource=$null
$script:learnerIndex=@();$script:learnerByMat=$null;$script:learnerIdentityLookup=$null;$script:learnerIndexSource=$null;$script:dataRevision=0;$script:learnerStamp='';$script:payLearnerStamp=''
$script:pendingLearnerSearch=$false;$script:pendingPaySearch=$false
$script:searchTimer=New-Object Windows.Forms.Timer;$script:searchTimer.Interval=45 # Sous le seuil perceptible, tout en regroupant les rafales de frappe.
function Get-ITMActivityAnchor {
    $owner=[Windows.Forms.Form]::ActiveForm;if(-not $owner){$owner=$script:main}
    if($script:activitySource -and -not $script:activitySource.IsDisposed -and $script:activitySource.Visible -and $script:activitySource.FindForm() -eq $owner){return $script:activitySource}
    $control=$owner.ActiveControl
    while($control -is [Windows.Forms.ContainerControl] -and $control.ActiveControl){$control=$control.ActiveControl}
    if($control -is [Windows.Forms.ButtonBase] -and $control.Visible){return $control}
    return $owner
}
function Start-ITMActivity([string]$message,$anchor=$null) {
    $script:activityDepth++
    try{
    # Réutiliser la carte du parcours de démarrage : pas de deux roues simultanées.
    if($global:ScoliaTransitionVisual){Update-ScoliaTransition $message;return}
    if($script:activity){$script:activity.Update($message);return}
    if(-not $anchor -or $anchor.IsDisposed){$anchor=Get-ITMActivityAnchor}
    $owner=$anchor.FindForm()
    if(-not $owner){$owner=$script:main}
    $script:activityAnchor=$anchor;$script:activityAnchorText=$null
    # L'indicateur moderne ne remplace plus le texte du bouton : aucune rupture visuelle.
    $bounds=$anchor.RectangleToScreen($anchor.ClientRectangle)
    $ownerBounds=$owner.RectangleToScreen($owner.ClientRectangle)
    $q=[Math]::Max(1,$owner.CurrentAutoScaleDimensions.Height/96.0)
    $cardHeight=[int][Math]::Min(144*$q,[Math]::Max(40,$ownerBounds.Height-20*$q))
    $cardWidth=[int][Math]::Max(100,[Math]::Min(440*$q,$ownerBounds.Width-(24*$q)))
    $left=[int]($ownerBounds.Left+($ownerBounds.Width-$cardWidth)/2)
    $top=[int]($ownerBounds.Bottom-$cardHeight-(22*$q))
    if($anchor -is [Windows.Forms.ButtonBase]){
        $left=[int]($bounds.Left+($bounds.Width-$cardWidth)/2)
        $left=[Math]::Max($ownerBounds.Left+[int](12*$q),[Math]::Min($left,$ownerBounds.Right-$cardWidth-[int](12*$q)))
        $above=[int]($bounds.Top-$cardHeight-(10*$q));$below=[int]($bounds.Bottom+(10*$q))
        if($above -ge $ownerBounds.Top+[int](10*$q)){$top=$above}elseif($below+$cardHeight -le $ownerBounds.Bottom-[int](10*$q)){$top=$below}
    }
    $top=[Math]::Max($ownerBounds.Top+[int](10*$q),[Math]::Min($top,$ownerBounds.Bottom-$cardHeight-[int](10*$q)))
    $rect=New-Object Drawing.Rectangle($left,$top,$cardWidth,$cardHeight)
    $handle=$(if($owner -and $owner.Visible){$owner.Handle}else{[IntPtr]::Zero})
    $dark=$(if(Get-Command Get-ITMEffectiveDarkTheme -ErrorAction SilentlyContinue){Get-ITMEffectiveDarkTheme}else{$false})
    $script:activity=New-Object ITMActivity($message,$rect,$handle,$script:windowIconPath,$dark)
    }catch{
        # Visual setup is auxiliary. Keep this call's depth until its matching Stop.
        Clear-ITMActivityVisual
    }
}
function Clear-ITMActivityVisual {
    try{if($script:activity){$script:activity.Dispose()}}catch{}
    finally{
        $script:activity=$null
        try{if($null -ne $script:activityAnchorText -and $script:activityAnchor -and -not $script:activityAnchor.IsDisposed){$script:activityAnchor.Text=$script:activityAnchorText}}catch{}
        finally{$script:activityAnchor=$null;$script:activityAnchorText=$null}
    }
}
function Stop-ITMActivity {
    $script:activityDepth=[Math]::Max(0,$script:activityDepth-1)
    if($script:activityDepth -eq 0){Clear-ITMActivityVisual}
}
function Set-ITMActivityMessage([string]$message) {
    if($global:ScoliaTransitionVisual){Update-ScoliaTransition $message;return}
    if($script:activity){$script:activity.Update($message)}
}
function Get-ITMBalanceKey([string]$mat,[string]$month) {
    return [string]::Format('{0}::{1}',(Key $mat),(Key $month))
}
function Schedule-ITMSearch([string]$target) {
    if($script:loading -or $script:busy){return}
    if($target -eq 'Learners'){$script:pendingLearnerSearch=$true}else{$script:pendingPaySearch=$true;$script:payLearnerGrid.Enabled=$false;$script:payLearnerGrid.ClearSelection()}
    $script:searchTimer.Stop();$script:searchTimer.Start()
}
$script:searchTimer.Add_Tick({
    if($script:busy -or $script:loading){return}
    $script:searchTimer.Stop()
    try{
        if($script:pendingLearnerSearch){$script:pendingLearnerSearch=$false;Refresh-Learners 'Learners'}
        if($script:pendingPaySearch){$script:pendingPaySearch=$false;Refresh-Learners 'Payments';Refresh-Payments}
    }catch{App-Error $_}finally{$script:payLearnerGrid.Enabled=[bool]$script:book}
})
function Get-ITMLearnerIdentityLookupKey([string]$nom,[string]$post,[string]$prenom,[string]$promotion) {
    $normalize={param([string]$value) (Key ($value -replace '[\s\u00a0\u202f]+',' ')).Trim()}
    return (& $normalize $nom)+'|'+(& $normalize $post)+'|'+(& $normalize $prenom)+'|'+(Promo $promotion)
}
function Get-ITMLearnerMatches([string]$mat) {
    $key=Key $mat
    if(-not $key){return @()}
    $current=$(if($script:data){$script:data.learners}else{$null})
    if($script:learnerByMat -and $script:learnerIndexSource -and [object]::ReferenceEquals($script:learnerIndexSource,$current) -and $script:learnerByMat.ContainsKey($key)){return $script:learnerByMat[$key].ToArray()}
    if($current){return @($current | Where-Object {(Key $_.Matricule) -eq $key})}
    return @()
}
function Get-ITMLearnersByIdentity([string]$nom,[string]$post,[string]$prenom,[string]$promotion) {
    $key=Get-ITMLearnerIdentityLookupKey $nom $post $prenom $promotion
    $current=$(if($script:data){$script:data.learners}else{$null})
    if($script:learnerIdentityLookup -and $script:learnerIndexSource -and [object]::ReferenceEquals($script:learnerIndexSource,$current) -and $script:learnerIdentityLookup.ContainsKey($key)){return $script:learnerIdentityLookup[$key].ToArray()}
    if(-not $current){return @()}
    $n=Get-ITMLearnerIdentityLookupKey $nom $post $prenom $promotion
    return @($current | Where-Object {(Get-ITMLearnerIdentityLookupKey ([string]$_.Nom) ([string]$_.PostNom) ([string]$_.Prenom) ([string]$_.Promotion)) -eq $n})
}
function Rebuild-ITMLearnerIndex {
    if(Get-Command Sync-ScoliaDisciplineSnapshot -ErrorAction SilentlyContinue){[void](Sync-ScoliaDisciplineSnapshot)}
    $index=New-Object 'System.Collections.Generic.List[object]'
    $byMat=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $byIdentity=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach($row in @($script:data.learners | Sort-Object Promotion,Nom,PostNom,Prenom)){
        $index.Add([pscustomobject]@{Entry=$row;Text=(Key ($row.Matricule+' '+$row.Nom+' '+$row.PostNom+' '+$row.Prenom+' '+$row.Promotion));Promotion=(Promo $row.Promotion)})
        $matKey=Key ([string]$row.Matricule)
        if(-not $byMat.ContainsKey($matKey)){$byMat[$matKey]=New-Object 'System.Collections.Generic.List[object]'}
        $byMat[$matKey].Add($row)
        $identityKey=Get-ITMLearnerIdentityLookupKey ([string]$row.Nom) ([string]$row.PostNom) ([string]$row.Prenom) ([string]$row.Promotion)
        if(-not $byIdentity.ContainsKey($identityKey)){$byIdentity[$identityKey]=New-Object 'System.Collections.Generic.List[object]'}
        $byIdentity[$identityKey].Add($row)
    }
    $script:learnerIndex=$index.ToArray();$script:learnerByMat=$byMat;$script:learnerIdentityLookup=$byIdentity;$script:learnerIndexSource=$script:data.learners;$script:dataRevision++
}

function Update-ITMLearnerIndexForSlice([string]$mat) {
    # Payments replace the learner object and the source array, but normally keep the
    # matricule/identity/sort keys unchanged. Refresh those references in place so the
    # O(1) indexes remain hot immediately after every payment.
    $mat=Key $mat
    if(@($script:learnerIndex).Count -ne @($script:data.learners).Count){Rebuild-ITMLearnerIndex;return}
    $item=$null;$matches=0;$learner=$null;$found=0
    foreach($candidate in @($script:learnerIndex)){
        if((Key $candidate.Entry.Matricule) -eq $mat){$item=$candidate;$matches++}
    }
    foreach($candidate in @($script:data.learners)){
        if((Key $candidate.Matricule) -eq $mat){$learner=$candidate;$found++}
    }
    if($matches -ne 1 -or $found -ne 1){Rebuild-ITMLearnerIndex;return}
    $oldEntry=$item.Entry
    foreach($field in @('Matricule','Promotion','Nom','PostNom','Prenom')){
        if([string]$oldEntry.$field -cne [string]$learner.$field){Rebuild-ITMLearnerIndex;return}
    }
    $fastIndexesPresent=[bool]($script:learnerByMat -or $script:learnerIdentityLookup)
    $identityList=$null;$identitySlot=-1;$matList=$null
    if($fastIndexesPresent){
        if(-not $script:learnerByMat -or -not $script:learnerByMat.ContainsKey($mat) -or $script:learnerByMat[$mat].Count -ne 1){Rebuild-ITMLearnerIndex;return}
        $identityKey=Get-ITMLearnerIdentityLookupKey ([string]$learner.Nom) ([string]$learner.PostNom) ([string]$learner.Prenom) ([string]$learner.Promotion)
        if(-not $script:learnerIdentityLookup -or -not $script:learnerIdentityLookup.ContainsKey($identityKey)){Rebuild-ITMLearnerIndex;return}
        $matList=$script:learnerByMat[$mat];$identityList=$script:learnerIdentityLookup[$identityKey]
        for($i=0;$i -lt $identityList.Count;$i++){if([object]::ReferenceEquals($identityList[$i],$oldEntry) -or (Key $identityList[$i].Matricule) -eq $mat){$identitySlot=$i;break}}
        if($identitySlot -lt 0){Rebuild-ITMLearnerIndex;return}
    }
    if(Get-Command Sync-ScoliaDisciplineSnapshot -ErrorAction SilentlyContinue){[void](Sync-ScoliaDisciplineSnapshot)}
    $item.Entry=$learner
    if($fastIndexesPresent){$matList[0]=$learner;$identityList[$identitySlot]=$learner}
    $script:learnerIndexSource=$script:data.learners
    $script:dataRevision++
}

function Copy-ITMObjectProperties($target,$source) {
    if(-not $target -or -not $source){return $false}
    foreach($property in $source.PSObject.Properties){
        $name=[string]$property.Name
        if($target.PSObject.Properties[$name]){
            try{$target.$name=$property.Value}catch{return $false}
        }
    }
    return $true
}

function Update-ITMLearnerSliceFast([string]$mat,$incoming) {
    # The common payment path changes non-key fields only. Keep the same learner
    # object and the same source array so all search dictionaries stay valid.
    if(-not $incoming){return $false}
    $mat=Key $mat
    $current=$(if($script:data){$script:data.learners}else{$null})
    if(-not $script:learnerIndexSource -or -not [object]::ReferenceEquals($script:learnerIndexSource,$current)){return $false}
    $matches=@(Get-ITMLearnerMatches $mat)
    if($matches.Count -ne 1){return $false}
    $existing=$matches[0]
    foreach($field in @('Matricule','Promotion','Nom','PostNom','Prenom')){
        if([string]$existing.$field -cne [string]$incoming.$field){return $false}
    }
    if(-not (Copy-ITMObjectProperties $existing $incoming)){return $false}
    if(Get-Command Sync-ScoliaDisciplineSnapshot -ErrorAction SilentlyContinue){[void](Sync-ScoliaDisciplineSnapshot)}
    $script:dataRevision++
    return $true
}

function Add-ITMArrayItems($source,$items) {
    $incoming=@($items)
    if(-not $incoming.Count){return $source}
    $count=0
    if($null -eq $source){$count=0}elseif($source -is [Array]){$count=$source.Length}else{$count=1}
    $expanded=[object[]]::new($count+$incoming.Count)
    if($count -gt 0){
        if($source -is [Array]){[Array]::Copy($source,0,$expanded,0,$count)}
        else{$expanded[0]=$source}
    }
    for($i=0;$i -lt $incoming.Count;$i++){$expanded[$count+$i]=$incoming[$i]}
    return $expanded
}

function Update-ITMPaymentSliceFast([string]$mat,$rows) {
    $mat=Key $mat
    $source=$script:data.payments
    if(-not $script:paymentLookup -or -not [object]::ReferenceEquals($script:paymentLookup.Source,$source)){
        # Build the lookup once if this screen has not needed payment history yet.
        [void](Get-ScoliaPaymentRows $mat)
    }
    if(-not $script:paymentLookup -or -not [object]::ReferenceEquals($script:paymentLookup.Source,$source)){return $false}

    $oldRows=@()
    if($script:paymentLookup.ByMat.ContainsKey($mat)){$oldRows=@($script:paymentLookup.ByMat[$mat])}
    $incoming=@($rows | Where-Object {$_})
    $oldById=@{};$newById=@{}
    foreach($row in $oldRows){
        $id=[string]$row.ID
        if([string]::IsNullOrWhiteSpace($id) -or $oldById.ContainsKey($id)){return $false}
        $oldById[$id]=$row
    }
    foreach($row in $incoming){
        if((Key ([string]$row.Matricule)) -ne $mat){return $false}
        $id=[string]$row.ID
        if([string]::IsNullOrWhiteSpace($id) -or $newById.ContainsKey($id)){return $false}
        $newById[$id]=$row
    }
    # A disappearing receipt is a structural correction: use the full safe fallback.
    foreach($id in $oldById.Keys){if(-not $newById.ContainsKey($id)){return $false}}

    $additions=New-Object 'System.Collections.Generic.List[object]'
    $resolved=New-Object 'System.Collections.Generic.List[object]'
    foreach($row in $incoming){
        $id=[string]$row.ID
        if($oldById.ContainsKey($id)){
            $existing=$oldById[$id]
            if(-not (Copy-ITMObjectProperties $existing $row)){return $false}
            $resolved.Add($existing)
        }else{
            $additions.Add($row);$resolved.Add($row)
        }
    }
    if($additions.Count){
        $script:data.payments=Add-ITMArrayItems $source $additions.ToArray()
    }
    $bucket=New-Object 'System.Collections.Generic.List[object]'
    foreach($row in $resolved){$bucket.Add($row)}
    $script:paymentLookup.Source=$script:data.payments
    if($bucket.Count){$script:paymentLookup.ByMat[$mat]=$bucket}
    elseif($script:paymentLookup.ByMat.ContainsKey($mat)){$script:paymentLookup.ByMat.Remove($mat)}
    $script:paymentLookup.Sorted.Clear()
    return $true
}

function Rebuild-ITMBalanceIndexes {
    $script:balance=@{};$script:balanceRows=@{};$script:balanceKeysByMat=@{}
    foreach($row in @($script:data.balances)){
        if(-not $row){continue}
        $mat=Key ([string]$row.Matricule);$key=Get-ITMBalanceKey $row.Matricule $row.Mois
        $script:balance[$key]=[decimal]$row.Verse
        $script:balanceRows[$key]=$row
        if(-not $script:balanceKeysByMat.ContainsKey($mat)){$script:balanceKeysByMat[$mat]=New-Object 'System.Collections.Generic.List[string]'}
        $script:balanceKeysByMat[$mat].Add($key)
    }
    $script:balanceIndexSource=$script:data.balances
}

function Update-ITMBalanceSliceFast([string]$mat,$rows) {
    $mat=Key $mat
    $source=$script:data.balances
    if(-not $script:balanceRows -or -not $script:balanceKeysByMat -or -not $script:balanceIndexSource -or -not [object]::ReferenceEquals($script:balanceIndexSource,$source)){
        Rebuild-ITMBalanceIndexes
    }
    if(-not [object]::ReferenceEquals($script:balanceIndexSource,$source)){return $false}
    $incoming=@($rows | Where-Object {$_})
    $newByKey=@{}
    foreach($row in $incoming){
        if((Key ([string]$row.Matricule)) -ne $mat){return $false}
        $key=Get-ITMBalanceKey $row.Matricule $row.Mois
        if($newByKey.ContainsKey($key)){return $false}
        $newByKey[$key]=$row
    }
    $oldKeys=@()
    if($script:balanceKeysByMat.ContainsKey($mat)){$oldKeys=@($script:balanceKeysByMat[$mat])}
    # A removed month is uncommon (receipt deletion/reallocation) and is safer through fallback.
    foreach($key in $oldKeys){if(-not $newByKey.ContainsKey($key)){return $false}}

    $additions=New-Object 'System.Collections.Generic.List[object]'
    $keys=New-Object 'System.Collections.Generic.List[string]'
    foreach($row in $incoming){
        $key=Get-ITMBalanceKey $row.Matricule $row.Mois
        if($script:balanceRows.ContainsKey($key)){
            $existing=$script:balanceRows[$key]
            if(-not (Copy-ITMObjectProperties $existing $row)){return $false}
            $script:balance[$key]=[decimal]$existing.Verse
        }else{
            $additions.Add($row);$script:balanceRows[$key]=$row;$script:balance[$key]=[decimal]$row.Verse
        }
        $keys.Add($key)
    }
    if($additions.Count){$script:data.balances=Add-ITMArrayItems $source $additions.ToArray()}
    $script:balanceIndexSource=$script:data.balances
    if($keys.Count){$script:balanceKeysByMat[$mat]=$keys}
    elseif($script:balanceKeysByMat.ContainsKey($mat)){$script:balanceKeysByMat.Remove($mat)}
    return $true
}

function Apply-ITMLearnerSlice($slice) {
    if(-not $slice -or -not $slice.PSObject.Properties['ok'] -or -not $slice.ok){throw 'Actualisation ciblée invalide.'}
    $mat=Key ([string]$slice.matricule)
    if(-not $mat){throw 'Matricule absent dans l actualisation ciblée.'}

    # Hot path: a payment normally keeps the learner's sorting/identity keys unchanged.
    # Mutate that one object in place instead of rebuilding the complete learner array.
    $learnerFast=$false
    if($slice.PSObject.Properties['learner'] -and $slice.learner){
        $learnerFast=Update-ITMLearnerSliceFast $mat $slice.learner
    }
    if(-not $learnerFast){
        $learners=New-Object 'System.Collections.Generic.List[object]'
        foreach($row in @($script:data.learners)){if((Key $row.Matricule) -ne $mat){$learners.Add($row)}}
        if($slice.PSObject.Properties['learner'] -and $slice.learner){$learners.Add($slice.learner)}
        $script:data.learners=$learners.ToArray()
        if(Get-Command Update-ITMLearnerIndexForSlice -ErrorAction SilentlyContinue){Update-ITMLearnerIndexForSlice $mat}else{Rebuild-ITMLearnerIndex}
    }

    # Existing receipts are updated in place and only genuinely new receipts are appended.
    # Structural changes (deletion/reallocation) retain the original full-safe fallback.
    if(-not (Update-ITMPaymentSliceFast $mat @($slice.payments))){
        $payments=New-Object 'System.Collections.Generic.List[object]'
        foreach($row in @($script:data.payments)){if((Key $row.Matricule) -ne $mat){$payments.Add($row)}}
        foreach($row in @($slice.payments)){if($row){$payments.Add($row)}}
        $script:data.payments=$payments.ToArray()
        $script:paymentLookup=$null
    }

    if(-not (Update-ITMBalanceSliceFast $mat @($slice.balances))){
        $balances=New-Object 'System.Collections.Generic.List[object]'
        foreach($row in @($script:data.balances)){if((Key $row.Matricule) -ne $mat){$balances.Add($row)}}
        foreach($row in @($slice.balances)){if($row){$balances.Add($row)}}
        $script:data.balances=$balances.ToArray()
        Rebuild-ITMBalanceIndexes
    }

    $script:learnerStamp='';$script:payLearnerStamp='';$script:snapshotValid=$true;$script:payNeedsRefresh=$false
    if($null -eq $script:viewRevision){$script:viewRevision=@{}}else{
        foreach($name in @('Tableau de bord','Apprenants','Paiements','Scolarité','Diagnostic')){if($script:viewRevision.ContainsKey($name)){$script:viewRevision.Remove($name)}}
    }
}
function Update-ITMAfterWrite([string]$mat,[bool]$deferView=$false) {
    $saveWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    try{
        $slice=$script:lastLearnerSlice;$script:lastLearnerSlice=$null
        if(-not $slice -or (Key $slice.matricule) -ne (Key $mat)){$slice=Invoke-ITMJsonDirect 'ITMAppEtatApprenant' @($mat)}
        Apply-ITMLearnerSlice $slice
    }catch{
        $script:lastLearnerSlice=$null
        # One compatibility fallback for missing/invalid data, never for a UI error.
        Refresh-Data $deferView
        return $false
    }
    if($script:scolia){$script:scoliaRosterPending=$true}
    if(-not $deferView){Refresh-ITMCurrentPage}
    # Do-Work restores controls once after the full action/batch.
    if(-not $script:busy){Update-WorkspaceState}
    return $true
    } finally {
        $saveWatch.Stop()
        if(Get-Command Add-ScoliaPerformanceSample -ErrorAction SilentlyContinue){Add-ScoliaPerformanceSample 'Actualisation apres ecriture' $saveWatch.Elapsed.TotalMilliseconds}
    }
}

function Update-ITMLearnerLayout($panel) {
    if(-not $script:learnerActions){return}
    $q=[Math]::Max(1,[Math]::Min(2.5,$script:main.CurrentAutoScaleDimensions.Height/96))
    $margin=[int](16*$q);$gap=[int](8*$q);$width=[Math]::Max(60,$panel.ClientSize.Width-2*$margin-[Windows.Forms.SystemInformation]::VerticalScrollBarWidth)
    $y=[int](14*$q)
    Set-ITMAdaptiveFont $script:learnerHint 10 ([Drawing.FontStyle]::Regular)
    $hh=Get-ITMTextHeight $script:learnerHint $width ([int](30*$q));$script:learnerHint.SetBounds($margin,$y,$width,$hh);$y+=$hh+$gap
    $surfaceTop=$y-[int](12*$q)
    $two=$width -ge 740*$q
    $searchWidth=$(if($two){[int]($width*0.58)}else{$width})
    $labelH=Get-ITMTextHeight $script:learnerSearchLabel $searchWidth ([int](24*$q))
    $fieldH=[Math]::Max([int](32*$q),$script:search.PreferredSize.Height)
    $script:learnerSearchLabel.SetBounds($margin,$y,$searchWidth,$labelH)
    $script:search.SetBounds($margin,($y+$labelH),$searchWidth,$fieldH)
    if($two){$x=$margin+$searchWidth+$gap;$comboWidth=$width-$searchWidth-$gap;$promoY=$y}else{$x=$margin;$comboWidth=$width;$promoY=$script:search.Bottom+$gap}
    $promoLabelH=Get-ITMTextHeight $script:learnerPromotionLabel $comboWidth ([int](24*$q))
    $script:learnerPromotionLabel.SetBounds($x,$promoY,$comboWidth,$promoLabelH)
    $comboH=[Math]::Max([int](32*$q),$script:filterPromo.PreferredSize.Height)
    $script:filterPromo.SetBounds($x,($promoY+$promoLabelH),$comboWidth,$comboH)
    $y=[Math]::Max($script:search.Bottom,$script:filterPromo.Bottom)+$gap
    $stack=$width/$q -lt 600
    $resetWidth=$(if($stack){$width}else{[Math]::Min([int](230*$q),[int]($width*.40))})
    $countWidth=$(if($stack){$width}else{$width-$resetWidth-$gap})
    $h=Get-ITMTextHeight $script:countLabel $countWidth ([int](30*$q));$script:countLabel.SetBounds($margin,$y,$countWidth,$h)
    $resetY=$(if($stack){$script:countLabel.Bottom+$gap}else{$y});$resetX=$(if($stack){$margin}else{$margin+$width-$resetWidth})
    $h=Get-ITMTextHeight $script:learnerReset $resetWidth ([int](38*$q));$script:learnerReset.SetBounds($resetX,$resetY,$resetWidth,$h)
    $y=[Math]::Max($script:countLabel.Bottom,$script:learnerReset.Bottom)+$gap
    $toolbarX=$margin;$toolbarY=$y;$rowHeight=0
    foreach($button in $script:learnerActions){
        Set-ITMAdaptiveFont $button 9.5 ([Drawing.FontStyle]::Regular)
        $textSize=[Windows.Forms.TextRenderer]::MeasureText($button.Text,$button.Font)
        $bw=[Math]::Min($width,[Math]::Max([int](130*$q),$textSize.Width+[int](32*$q)))
        $bh=Get-ITMTextHeight $button $bw ([int](38*$q))
        if($toolbarX -gt $margin -and $toolbarX+$bw -gt $margin+$width){$toolbarY+=$rowHeight+$gap;$toolbarX=$margin;$rowHeight=0}
        $button.SetBounds($toolbarX,$toolbarY,$bw,$bh);$toolbarX+=$bw+$gap;$rowHeight=[Math]::Max($rowHeight,$bh)
    }
    $y=$toolbarY+$rowHeight+[int](12*$q)
    $script:learnerSurface.Visible=$false
    $height=[Math]::Max([int](240*$q),$panel.ClientSize.Height-$y-[int](24*$q))
    $script:learnersGrid.SetBounds($margin,$y,$width,$height)
    Set-ITMGridReadability $script:learnersGrid $false
    $panel.AutoScrollMinSize=New-Object Drawing.Size(0,($y+$height+[int](24*$q)))
}

# Hidden views are redrawn when opened, from the verified current snapshot.
$script:viewRevision=@{};$script:snapshotValid=$false
function Test-ITMPageNeedsRefresh([string]$name){
    if(-not $name -or -not $script:data){return $false}
    if($name -eq 'Tableau de bord' -and $script:dashboardDate -ne (Get-Date).ToString('yyyyMMdd')){return $true}
    return ($null -eq $script:viewRevision -or -not $script:viewRevision.ContainsKey($name) -or $script:viewRevision[$name] -ne $script:dataRevision)
}
function Refresh-ITMCurrentPage {
    $name=[string]$script:currentPage
    if(-not $name -or -not $script:data){return}
    if($null -eq $script:viewRevision){$script:viewRevision=@{}}
    if(-not (Test-ITMPageNeedsRefresh $name)){return}
    switch($name){
        'Comptabilité' {Refresh-ScoliaFinance}
        'Apprenants' {Refresh-Learners 'Learners'}
        'Paiements' {
            Refresh-Learners 'Payments'
            if($script:payMat){
                $current=@(Get-ITMLearnerMatches $script:payMat | Where-Object {$_.Eligible})
                if($current.Count -eq 1){Choose-Payment $script:payMat}else{Reset-PaymentSelection}
            }else{Refresh-Payments}
        }
        'Tableau de bord' {Refresh-Dashboard}
        'Administration' {Refresh-Administration}
        'Exports' {Refresh-ITMSchoolCards}
        'Données' {Refresh-ITMDataPage}
        'Diagnostic' {
            Set-Grid $script:issuesGrid @($script:data.issues) @(@('Feuille','Feuille'),@('Ligne','Ligne'),@('Matricule','Matricule'),@('Numero','N°'),@('Observation','Observation'))
            $script:issuesLabel.Text="$(@($script:data.issues).Count) observation(s) de qualité des données. Aucun rapprochement automatique des fragments de paiement."
        }
    }
    $script:viewRevision[$name]=$script:dataRevision
}

# V75: bounded in-memory durations; one disk write on normal application close.
function Add-ScoliaPerformanceSample([string]$Stage,[double]$Milliseconds) {
    try{
        if($null -eq $script:scoliaPerfSamples){$script:scoliaPerfSamples=New-Object 'System.Collections.Generic.Queue[object]'}
        if($script:scoliaPerfSamples.Count -ge 1000){[void]$script:scoliaPerfSamples.Dequeue()}
        $script:scoliaPerfSamples.Enqueue([pscustomobject]@{Heure=(Get-Date).ToString('o');Etape=$Stage;Millisecondes=[Math]::Round($Milliseconds,2)})
    }catch{} # Diagnostics must never alter the outcome of an operation.
}
function Save-ScoliaPerformanceSamples {
    try{
        if($null -eq $script:scoliaPerfSamples -or -not $script:scoliaPerfSamples.Count){return}
        $directory=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ITMCD/diagnostics'
        [void][IO.Directory]::CreateDirectory($directory)
        $script:scoliaPerfSamples.ToArray() | Export-Csv -LiteralPath (Join-Path $directory 'performance-v79.csv') -NoTypeInformation -Encoding UTF8 -Delimiter ';'
    }catch{}
}

# Reconcile a bound table without detaching the grid. Validate identities before any mutation.
function Get-ScoliaGridRowKey($row,$keys) {
    $builder=New-Object Text.StringBuilder
    foreach($key in $keys){$value=[string]$row[$key];[void]$builder.Append($value.Length).Append(':').Append($value)}
    return $builder.ToString()
}
function Sync-ScoliaGridTable([Data.DataTable]$target,[Data.DataTable]$source,$keys) {
    if($target.Columns.Count -ne $source.Columns.Count){return $false}
    for($i=0;$i -lt $target.Columns.Count;$i++){
        if($target.Columns[$i].ColumnName -cne $source.Columns[$i].ColumnName -or $target.Columns[$i].DataType -ne $source.Columns[$i].DataType){return $false}
    }
    # Without a unique business key, only the exactly identical table is a safe no-op.
    if(-not $keys.Count){
        if($target.Rows.Count -ne $source.Rows.Count){return $false}
        for($i=0;$i -lt $target.Rows.Count;$i++){for($j=0;$j -lt $target.Columns.Count;$j++){
            if(-not [object]::Equals($target.Rows[$i][$j],$source.Rows[$i][$j])){return $false}
        }}
        return $true
    }
    $old=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $incoming=New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach($row in $target.Rows){$key=Get-ScoliaGridRowKey $row $keys;if($old.ContainsKey($key)){return $false};$old.Add($key,$row)}
    foreach($row in $source.Rows){$key=Get-ScoliaGridRowKey $row $keys;if($incoming.ContainsKey($key)){return $false};$incoming.Add($key,$row)}
    foreach($key in $old.Keys){if(-not $incoming.ContainsKey($key)){$target.Rows.Remove($old[$key])}}
    foreach($row in $source.Rows){
        $key=Get-ScoliaGridRowKey $row $keys
        if(-not $old.ContainsKey($key)){$target.ImportRow($row);continue}
        $existing=$old[$key]
        for($j=0;$j -lt $target.Columns.Count;$j++){
            if(-not [object]::Equals($existing[$j],$row[$j])){$existing[$j]=$row[$j]}
        }
    }
    return $true
}

function Get-ScoliaPaymentRows([string]$Matricule='', [string]$Month='Tous les mois') {
    $source=$script:data.payments
    if(-not $script:paymentLookup -or -not [object]::ReferenceEquals($script:paymentLookup.Source,$source)){
        $byMat=@{}
        foreach($row in $source){
            $key=Key ([string]$row.Matricule)
            if(-not $byMat.ContainsKey($key)){$byMat[$key]=New-Object 'System.Collections.Generic.List[object]'}
            $byMat[$key].Add($row)
        }
        $script:paymentLookup=@{Source=$source;ByMat=$byMat;Sorted=@{}}
    }
    # Prefix prevents a real matricule from colliding with the all-records cache.
    $key=$(if($Matricule){'mat:'+(Key $Matricule)}else{'all:'})
    $cache=$script:paymentLookup.Sorted
    if(-not $cache.ContainsKey($key)){
        $rows=$(if($Matricule){$script:paymentLookup.ByMat[(Key $Matricule)]}else{$source})
        if($cache.Count -ge 128){$cache.Clear()}
        $cache[$key]=@($rows | Sort-Object @{Expression={try{[DateTime]::ParseExact([string]$_.Date,'dd/MM/yyyy',[Globalization.CultureInfo]::InvariantCulture)}catch{[DateTime]::MinValue}}},@{Expression={[long]$_.ID}} -Descending)
    }
    if($Month -eq 'Tous les mois'){return $cache[$key]}
    $monthKey=Key $Month
    foreach($row in $cache[$key]){if((Key ([string]$row.Mois)) -eq $monthKey){$row}}
}

# The native modal message loop keeps painting and input alive while C# performs I/O.
# No DoEvents polling, PowerShell scriptblocks on worker threads, or COM cross-thread access.
function Wait-ScoliaNetworkTask($Task,[string]$Message='Enregistrement réseau en cours…') {
    if(-not $Task){throw 'Tâche réseau absente.'}
    if(-not $Task.IsCompleted){
        $dialog=$null;$timer=$null
        try {
        $owner=[Windows.Forms.Form]::ActiveForm;if(-not $owner){$owner=$script:main}
        $dialog=New-ScoliaDialog 'SCOLIA · Transmission' 480 180
        $dialog.MinimumSize=New-Object Drawing.Size(380,160);$dialog.Size=New-Object Drawing.Size(480,180);$dialog.StartPosition='CenterParent'
        $dialog.MinimizeBox=$false;$dialog.MaximizeBox=$false;$dialog.ControlBox=$false
        $label=New-Label $dialog $Message 16 20 440 80;$label.Dock='Fill';$label.Padding=New-Object Windows.Forms.Padding(18)
        $timer=New-Object Windows.Forms.Timer;$timer.Interval=50
        $timer.Add_Tick({if($Task.IsCompleted){$timer.Stop();$dialog.Close()}})
        $dialog.Add_FormClosing({param($sender,$e)if(-not $Task.IsCompleted){$e.Cancel=$true}})
        Apply-ITMThemeForm $dialog;$timer.Start();[void]$dialog.ShowDialog($owner)
        }catch{
            if(-not $Task.IsCompleted){throw [IO.IOException]::new('Transmission toujours en cours : vérifier le reçu avant de ressaisir. '+$_.Exception.Message)}
        }finally{
            try{if($timer){$timer.Stop();$timer.Dispose()};if($dialog){$dialog.Dispose()}}catch{}
        }
    }
    # Completed only: propagates the original failure, never waits on the UI thread.
    if(-not $Task.IsCompleted){throw 'Transmission non terminée : vérifier avant de ressaisir.'}
    return $Task.GetAwaiter().GetResult()
}

function Sync-ScoliaRosterForPayment {
    if(-not $script:scoliaSession -or -not (Test-ScoliaPermission 'Recevoir') -or -not $script:data -or -not $script:book){throw 'Publication du secrétariat indisponible : reconnectez les données et vérifiez les droits.'}
    if((Get-ScoliaConnectedYear) -ne ([ScoliaSharing]::YearKey($script:scolia.Year))){throw 'Connectez la base correspondant à l année du paiement.'}
    do {
        Publish-ScoliaRoster
        if(-not $script:scoliaRosterTask){throw 'Publication du répertoire non démarrée.'}
        [void](Wait-ScoliaNetworkTask $script:scoliaRosterTask 'Publication des données nécessaires au paiement…')
        # If a write happened during an earlier publication, publish the new revision
        # before committing a payment against the shared roster.
    } while($script:scoliaRosterPending)
}
