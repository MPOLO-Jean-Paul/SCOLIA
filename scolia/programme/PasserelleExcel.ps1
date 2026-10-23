if(-not (Get-Command Import-ITMCompiledSource -ErrorAction SilentlyContinue)){. (Join-Path $PSScriptRoot 'CompilationCache.ps1')}
# Shared Excel COM transport. Loaded by the app and data-store entry points.
if(-not ('ITMExcelDispatch' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ExcelDispatch.cs')}

function Test-ITMExcelDisconnectedFailure($failure) {
    $exception=$(if($failure -is [Management.Automation.ErrorRecord]){$failure.Exception}elseif($failure -is [Exception]){$failure}else{$null})
    while($exception){
        $message=[string]$exception.Message
        if($message -match 'RPC_E_DISCONNECTED|0x80010108|déconnecté de ses clients|deconnecte de ses clients|disconnected from its clients'){return $true}
        try{if([int]$exception.HResult -eq -2147417848){return $true}}catch{}
        $exception=$exception.InnerException
    }
    return $false
}

function Resolve-ITMLiveWorkbook($workbook) {
    if($null -eq $workbook){throw 'Le classeur Excel est absent avant la vérification de la passerelle.'}
    if(-not [Runtime.InteropServices.Marshal]::IsComObject($workbook)){return $workbook}

    # Fast path: most calls keep the original RCW alive.
    try{
        $name=[string][ITMExcelDispatch]::Get($workbook.PSObject.BaseObject,'Name',[object[]]@())
        if(-not [string]::IsNullOrWhiteSpace($name)){return $workbook}
    }catch{
        if(-not (Test-ITMExcelDisconnectedFailure $_)){throw}
    }

    # A workbook can be closed/reopened by the managed data layer while PowerShell
    # still holds the old RCW. Recover the live workbook from the private Excel
    # instance instead of forcing the user to restart SCOLIA.
    if($null -eq $script:excel){throw 'La session Excel nʼest plus disponible. Reconnectez le dossier data.'}
    $expectedName='';$expectedPath='';$storeIndex=-1
    if($script:store -and $script:storeFolder){
        if([object]::ReferenceEquals($workbook,$script:book)){
            $expectedName=[string]$script:store.Active
        }elseif($script:storeBooks){
            for($i=0;$i -lt @($script:storeBooks).Count;$i++){
                if([object]::ReferenceEquals($workbook,$script:storeBooks[$i])){$storeIndex=$i;break}
            }
            if($storeIndex -ge 0 -and $storeIndex -lt @($script:store.Segments).Count){$expectedName=[string]$script:store.Segments[$storeIndex]}
        }
        if($expectedName){$expectedPath=Join-Path $script:storeFolder $expectedName}
    }
    if(-not $expectedPath -and $script:connectedWorkbookPath){
        $expectedPath=[string]$script:connectedWorkbookPath
        try{$expectedName=[IO.Path]::GetFileName($expectedPath)}catch{}
    }
    if(-not $expectedPath -and $script:rememberedPath){
        $expectedPath=[string]$script:rememberedPath
        try{$expectedName=[IO.Path]::GetFileName($expectedPath)}catch{}
    }

    try{
        $books=[ITMExcelDispatch]::Get($script:excel.PSObject.BaseObject,'Workbooks',[object[]]@())
        $count=[int][ITMExcelDispatch]::Get($books,'Count',[object[]]@())
        $live=$null
        for($i=1;$i -le $count;$i++){
            $candidate=[ITMExcelDispatch]::Get($books,'Item',[object[]]@($i))
            if($null -eq $candidate){continue}
            $candidateName=[string][ITMExcelDispatch]::Get($candidate,'Name',[object[]]@())
            $candidatePath=''
            try{$candidatePath=[string][ITMExcelDispatch]::Get($candidate,'FullName',[object[]]@())}catch{}
            $nameMatch=$expectedName -and $candidateName.Equals($expectedName,[StringComparison]::OrdinalIgnoreCase)
            $pathMatch=$false
            if($expectedPath -and $candidatePath){
                try{$pathMatch=[IO.Path]::GetFullPath($candidatePath).Equals([IO.Path]::GetFullPath($expectedPath),[StringComparison]::OrdinalIgnoreCase)}catch{$pathMatch=$candidatePath -ieq $expectedPath}
            }
            if($pathMatch -or $nameMatch){$live=$candidate;break}
        }
        if($null -eq $live){throw 'Le classeur attendu nʼest plus ouvert dans la session Excel privée.'}

        $liveName=[string][ITMExcelDispatch]::Get($live,'Name',[object[]]@())
        if($script:store -and $liveName -eq [string]$script:store.Active){$script:book=$live}
        if($script:storeBooks){
            for($i=0;$i -lt @($script:storeBooks).Count;$i++){
                $segmentName=$(if($i -lt @($script:store.Segments).Count){[string]$script:store.Segments[$i]}else{''})
                if($segmentName -and $liveName -eq $segmentName){$script:storeBooks[$i]=$live;break}
            }
        }
        return $live
    }catch{
        throw (New-Object InvalidOperationException -ArgumentList @('La référence Excel du classeur sʼest déconnectée et SCOLIA nʼa pas pu la rattacher automatiquement. Fermez toute fenêtre Excel ouverte sur ce dossier puis utilisez Actualiser ou reconnectez le dossier data.',$_.Exception))
    }
}

function Get-ITMExcelMacroName($workbook,[string]$module,[string]$procedure) {
    if($null -eq $workbook){throw 'Le classeur Excel est absent avant la vérification de la passerelle.'}
    if($module -notmatch '^[A-Za-z_][A-Za-z0-9_]*$' -or $procedure -notmatch '^[A-Za-z_][A-Za-z0-9_]*$'){throw 'Nom de procédure Excel invalide.'}
    try{
        $liveWorkbook=Resolve-ITMLiveWorkbook $workbook
        if([Runtime.InteropServices.Marshal]::IsComObject($liveWorkbook)){
            $bookName=[string][ITMExcelDispatch]::Get($liveWorkbook.PSObject.BaseObject,'Name',[object[]]@())
        }else{$bookName=[string]$liveWorkbook.Name}
    }catch{throw (New-Object InvalidOperationException -ArgumentList @('Excel ne peut pas fournir le nom du classeur actif.',$_.Exception))}
    if([string]::IsNullOrWhiteSpace($bookName)){throw 'Excel a retourné un nom de classeur vide.'}
    return "'"+$bookName.Replace("'","''")+"'!"+$module+'.'+$procedure
}

function Invoke-ITMExcelRun($application,[string]$macro,[object[]]$macroArguments=@()) {
    $script:connectionMacro=$macro
    if($null -eq $application){throw 'La session Excel est absente. Reconnectez le classeur.'}
    if([string]::IsNullOrWhiteSpace($macro)){throw 'Le nom de la macro Excel est absent.'}
    if(@($macroArguments).Count -gt 30){throw 'Excel.Run accepte au maximum 30 paramètres métier.'}
    # Excel.Run has one macro parameter and thirty optional arguments. Supplying
    # every slot through the managed dispatcher avoids unstable PS COM binding.
    try{
        $arguments=[object[]]::new(31)
        $arguments[0]=$macro
        for($i=1;$i -lt $arguments.Length;$i++){$arguments[$i]=[Reflection.Missing]::Value}
        for($i=0;$i -lt @($macroArguments).Count;$i++){$arguments[$i+1]=$macroArguments[$i]}
        return [ITMExcelDispatch]::Call($application.PSObject.BaseObject,'Run',$arguments)
    }catch{
        throw (New-Object InvalidOperationException -ArgumentList @(("Échec de lʼappel Excel à la macro "+$macro+' : '+$_.Exception.Message),$_.Exception))
    }
}
function Invoke-ITMExcelRunNoArguments($application,[string]$macro) {
    return Invoke-ITMExcelRun $application $macro @()
}

# Explicit COM member access for persisted workbook identity and import operations.
function Get-ITMExcelProperty($target,[string]$name,[object[]]$index=@()) {
    if($null -eq $target){throw ('Objet Excel absent pour lire '+$name+'.')}
    $script:connectionOperation='Lecture : '+$name
    try{
        if([Runtime.InteropServices.Marshal]::IsComObject($target) -or $index.Count){
            $value=[ITMExcelDispatch]::Get($target.PSObject.BaseObject,$name,$index)
        }else{
            $property=$target.PSObject.Properties[$name]
            if(-not $property){throw ('Propriété absente : '+$name)}
            $value=$property.Value
        }
        return ,$value
    }catch{throw (New-Object InvalidOperationException -ArgumentList @(('Lecture Excel impossible : '+$name+' : '+$_.Exception.Message),$_.Exception))}
}
function Set-ITMExcelProperty($target,[string]$name,$value) {
    if($null -eq $target){throw ('Objet Excel absent pour écrire '+$name+'.')}
    $script:connectionOperation='Écriture : '+$name
    try{
        if([Runtime.InteropServices.Marshal]::IsComObject($target)){
            [void][ITMExcelDispatch]::Set($target.PSObject.BaseObject,$name,$value)
        }else{$target.PSObject.Properties[$name].Value=$value}
    }catch{throw (New-Object InvalidOperationException -ArgumentList @(('Écriture Excel impossible : '+$name+' : '+$_.Exception.Message),$_.Exception))}
}
function Invoke-ITMExcelMethod($target,[string]$name,[object[]]$arguments=@()) {
    if($null -eq $target){throw ('Objet Excel absent pour appeler '+$name+'.')}
    $script:connectionOperation='Appel : '+$name
    try{
        # Office methods receive every optional slot explicitly; no retries on writes.
        if([Runtime.InteropServices.Marshal]::IsComObject($target)){
            $value=[ITMExcelDispatch]::Call($target.PSObject.BaseObject,$name,$arguments)
        }else{$value=$target.PSObject.Methods[$name].Invoke($arguments)}
        return ,$value
    }catch{throw (New-Object InvalidOperationException -ArgumentList @(('Appel Excel impossible : '+$name+' : '+$_.Exception.Message),$_.Exception))}
}
