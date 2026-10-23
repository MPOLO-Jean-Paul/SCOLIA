param([string]$Classeur)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'IconesFenetres.ps1')
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'PasserelleExcel.ps1')
. (Join-Path $PSScriptRoot 'PaiementsFiabiliteV61.ps1')
# V80 : noyau d'accélération compilé. En cas d'échec de compilation, les fonctions
# gardent automatiquement leur implémentation PowerShell d'origine.
$script:scoliaFast=$false
try{
    if(-not ('ITMFast' -as [type])){Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'AccelerationScolia.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing,System.Data,System.Xml}
    $script:scoliaFast=[bool]('ITMFast' -as [type])
}catch{$script:scoliaFast=$false}
[Windows.Forms.Application]::EnableVisualStyles()
$script:excel=$null; $script:book=$null; $script:data=$null; $script:busy=$false; $script:loading=$false
$script:payNeedsRefresh=$false;$script:payReste=[decimal]0
$script:balance=@{}; $script:fees=@{}; $script:pages=@{}; $script:nav=@{}
$script:fr=[Globalization.CultureInfo]::GetCultureInfo('fr-FR')
# Pure text cache: bounded, ordinal keys, no business state cached here.
$script:scoliaTextKeys=New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)
function Key([object]$value) {
    if($script:scoliaFast){return [ITMFast]::Key([string]$value)}
    if($null -eq $script:scoliaTextKeys){$script:scoliaTextKeys=New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::Ordinal)}
    $raw=[string]$value
    if($script:scoliaTextKeys.ContainsKey($raw)){return $script:scoliaTextKeys[$raw]}
    $t=$raw.Trim().ToUpperInvariant().Normalize([Text.NormalizationForm]::FormD)
    $builder=New-Object Text.StringBuilder
    foreach($character in $t.ToCharArray()){
        if([Globalization.CharUnicodeInfo]::GetUnicodeCategory($character) -ne [Globalization.UnicodeCategory]::NonSpacingMark){[void]$builder.Append($character)}
    }
    $result=$builder.ToString().Normalize([Text.NormalizationForm]::FormC)
    if($script:scoliaTextKeys.Count -ge 16384){$script:scoliaTextKeys.Clear()}
    $script:scoliaTextKeys[$raw]=$result
    return $result
}
function Promo([object]$value) { $v=(Key $value).Replace(' ','').Replace('-',''); if($v -eq 'TML1'){$v='TLM1'}; return $v }
function Money([object]$value) { return ([decimal]$value).ToString('N0',$script:fr)+' FC' }
function New-Label($parent,[string]$text,[int]$x,[int]$y,[int]$w,[int]$h,[int]$size=10,[bool]$bold=$false) {
    $c=New-Object ITMReadableLabel; $c.Text=$text; $c.SetBounds($x,$y,$w,$h)
    $c.Font=New-Object Drawing.Font('Segoe UI',$size,$(if($bold){[Drawing.FontStyle]::Bold}else{[Drawing.FontStyle]::Regular}))
    $c.ForeColor=[Drawing.Color]::FromArgb(30,41,59); $parent.Controls.Add($c); return $c
}
function New-Button($parent,[string]$text,[int]$x,[int]$y,[int]$w=160,[bool]$primary=$false) {
    $b=New-Object ITMActionButton; $b.Primary=$primary; $b.Text=$text; $b.SetBounds($x,$y,$w,36); $b.FlatStyle='Flat'; $b.FlatAppearance.BorderSize=0
    $b.BackColor=$(if($primary){[Drawing.Color]::FromArgb(0,102,204)}else{[Drawing.Color]::FromArgb(247,249,252)})
    $b.FlatAppearance.MouseOverBackColor=$(if($primary){[Drawing.Color]::FromArgb(36,80,143)}else{[Drawing.Color]::FromArgb(229,235,246)});$b.FlatAppearance.MouseDownBackColor=[Drawing.Color]::FromArgb(18,48,92)
    $b.ForeColor=$(if($primary){[Drawing.Color]::White}else{[Drawing.Color]::FromArgb(0,102,204)})
    $b.Font=New-Object Drawing.Font('Segoe UI',10,[Drawing.FontStyle]::Bold); $b.Cursor='Hand'; $parent.Controls.Add($b);if(Test-Path function:Set-ITMCommandHelp){Set-ITMCommandHelp $b $null};return $b
}
function New-Combo($parent,[int]$x,[int]$y,[int]$w=190) {
    $c=New-Object ITMThemeComboBox; $c.DropDownStyle='DropDownList'; $c.SetBounds($x,$y,$w,30); $parent.Controls.Add($c); return $c
}
function New-Grid($parent,[int]$x,[int]$y,[int]$w,[int]$h) {
    $g=New-Object Windows.Forms.DataGridView; $g.SetBounds($x,$y,[Math]::Max($w,$parent.ClientSize.Width-$x-24),$h); $g.Anchor='Top,Left,Right'
    $g.ReadOnly=$true; $g.AllowUserToAddRows=$false; $g.AllowUserToDeleteRows=$false; $g.AllowUserToOrderColumns=$true
    $g.MultiSelect=$false; $g.SelectionMode='FullRowSelect'; $g.RowHeadersVisible=$false; $g.AutoSizeColumnsMode='None';$g.AutoGenerateColumns=$false
    $g.BackgroundColor=[Drawing.Color]::White; $g.BorderStyle='None'; $g.GridColor=[Drawing.Color]::FromArgb(230,236,245)
    $g.EnableHeadersVisualStyles=$false; $g.ColumnHeadersDefaultCellStyle.BackColor=[Drawing.Color]::FromArgb(231,238,248)
    $g.ColumnHeadersDefaultCellStyle.ForeColor=[Drawing.Color]::FromArgb(0,102,204)
    $g.ColumnHeadersDefaultCellStyle.Font=New-Object Drawing.Font('Segoe UI',10,[Drawing.FontStyle]::Bold)
    $g.ColumnHeadersHeightSizeMode='AutoSize';$g.ScrollBars='Both';$g.ColumnHeadersDefaultCellStyle.WrapMode='True';$g.DefaultCellStyle.Padding=New-Object Windows.Forms.Padding(5,3,5,3);$g.DefaultCellStyle.WrapMode='True';$g.AutoSizeRowsMode='None'; $g.RowTemplate.Height=34; $g.DefaultCellStyle.SelectionBackColor=[Drawing.Color]::FromArgb(217,231,253)
    $g.DefaultCellStyle.SelectionForeColor=[Drawing.Color]::FromArgb(0,102,204)
    $g.AlternatingRowsDefaultCellStyle.BackColor=[Drawing.Color]::FromArgb(247,249,253);[ITMActivity]::BufferGrid($g); $parent.Controls.Add($g)
    $g.Add_DataBindingComplete({param($sender,$e) if(-not $script:scoliaBindingGrids -or -not $script:scoliaBindingGrids.Contains($sender)){Set-ITMGridReadability $sender}})
    $g.Add_SizeChanged({param($sender,$e) if(-not $script:scoliaBindingGrids -or -not $script:scoliaBindingGrids.Contains($sender)){Set-ITMGridReadability $sender $false}})
    if(Test-Path function:Set-ITMGridAppearance){Set-ITMGridAppearance $g}
    return $g
}
function Set-Grid($grid,$rows,$columns) {
    $performanceWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    if($script:scoliaFast){
        # V80 : même table, construite en C# (aucune boucle interprétée par cellule).
        $columnProperties=New-Object 'System.Collections.Generic.List[string]';$columnNames=New-Object 'System.Collections.Generic.List[string]';$columnTypes=New-Object 'System.Collections.Generic.List[type]'
        foreach($column in $columns){$columnProperties.Add([string]$column[0]);$columnNames.Add([string]$column[1]);$columnTypes.Add($(if($column.Count -gt 2 -and $column[2] -is [type]){$column[2]}else{[string]}))}
        $table=[ITMFast]::BuildTable([object[]]@($rows),$columnProperties.ToArray(),$columnNames.ToArray(),$columnTypes.ToArray())
    } else {
    $table=New-Object Data.DataTable
    foreach($column in $columns){$columnType=$(if($column.Count -gt 2 -and $column[2] -is [type]){$column[2]}else{[string]});[void]$table.Columns.Add([string]$column[1],$columnType)}
    $table.BeginLoadData()
    try {
    foreach($entry in @($rows)) {
        if($null -eq $entry){continue}; $r=$table.NewRow()
        foreach($column in $columns){ $v=$entry.([string]$column[0]); $isText=$table.Columns[[string]$column[1]].DataType -eq [string];$r[[string]$column[1]]=$(if($null -eq $v){if($isText){''}else{[DBNull]::Value}}elseif($isText){[string]$v}else{$v}) }
        $table.Rows.Add($r)
    }
    } finally {$table.EndLoadData()}
    }
    # Keep identity rather than an index: filters and new rows can move a dossier.
    $identity=@{};$identityColumns=@()
    $names=@($columns | ForEach-Object {[string]$_[1]})
    if('Identifiant opération' -in $names){$identityColumns=@('Identifiant opération')}
    elseif('N° enregistrement' -in $names){$identityColumns=@('N° enregistrement','Matricule','Mois')}
    elseif('Clé' -in $names){$identityColumns=@('Clé')}
    elseif('Matricule' -in $names -and 'Observation' -notin $names){$identityColumns=@('Matricule')}
    elseif('Classeur' -in $names -and 'Feuille' -in $names){$identityColumns=@('Classeur','Feuille')}
    if($grid.SelectedRows.Count -eq 1){
        foreach($name in $identityColumns){if($grid.Columns.Contains($name)){$identity[$name]=[string]$grid.SelectedRows[0].Cells[$name].Value}}
    }
    $firstVisible=$grid.FirstDisplayedScrollingRowIndex
    $previous=$grid.DataSource
    if($null -eq $script:scoliaBindingGrids){$script:scoliaBindingGrids=New-Object 'System.Collections.Generic.HashSet[object]'}
    [void]$script:scoliaBindingGrids.Add($grid)
    $grid.SuspendLayout()
    try {
        $grid.AutoSizeColumnsMode='None';$grid.AutoGenerateColumns=$false
        $sameSchema=($grid.Columns.Count -eq $columns.Count)
        if($sameSchema){
            for($i=0;$i -lt $columns.Count;$i++){if($grid.Columns[$i].Name -cne [string]$columns[$i][1]){$sameSchema=$false;break}}
        }
        if($sameSchema -and $previous -is [Data.DataTable] -and
            (Sync-ScoliaGridTable $previous $table $identityColumns)){
            # Keep the binding, selection, sort and scroll. Recalculate only visible
            # row heights once; never continuously during paint/scroll.
            Set-ITMGridReadability $grid
            return
        }
        $grid.DataSource=$null
        if(-not $sameSchema){
            $grid.Columns.Clear()
            foreach($column in $columns){
                $col=New-Object Windows.Forms.DataGridViewTextBoxColumn
                $col.Name=[string]$column[1];$col.HeaderText=[string]$column[1];$col.DataPropertyName=[string]$column[1]
                # Configure the detached column before adding it to the live grid.
                $col.AutoSizeMode='None';$col.MinimumWidth=2;$col.Width=110
                [void]$grid.Columns.Add($col)
            }
        }
        $grid.DataSource=$table
        # Le filtrage instantané conserve les colonnes et leurs largeurs ; on évite
        # de reconstruire toute la grille à chaque frappe.
        Set-ITMGridReadability $grid
        $grid.ClearSelection()
        $fastIndex=-2
        if($identity.Count -gt 0 -and ('ITMFastGrid' -as [type])){
            $identityNames=[string[]]@($identity.Keys);$identityValues=[string[]]@(foreach($name in $identityNames){[string]$identity[$name]})
            $fastIndex=[ITMFastGrid]::FindUniqueRow($grid,$identityNames,$identityValues)
            if($fastIndex -ge 0){$match=$grid.Rows[$fastIndex];$cell=@($match.Cells|Where-Object {$_.Visible})|Select-Object -First 1;if($cell){$grid.CurrentCell=$cell};$match.Selected=$true}
        }
        if($identity.Count -gt 0 -and $fastIndex -eq -2){
            $match=$null;$matches=0
            foreach($row in $grid.Rows){
                $equal=$true
                foreach($name in $identity.Keys){if([string]$row.Cells[$name].Value -cne $identity[$name]){$equal=$false;break}}
                if($equal){$match=$row;$matches++}
            }
            # Ambiguous or hidden records must never select a different dossier.
            if($matches -eq 1){$cell=@($match.Cells|Where-Object {$_.Visible})|Select-Object -First 1;if($cell){$grid.CurrentCell=$cell};$match.Selected=$true}
        }
        if($firstVisible -ge 0 -and $grid.Rows.Count -gt 0){$grid.FirstDisplayedScrollingRowIndex=[Math]::Min($firstVisible,$grid.Rows.Count-1)}
    } finally {
        [void]$script:scoliaBindingGrids.Remove($grid)
        $grid.ResumeLayout()
        if($previous -is [Data.DataTable] -and $grid.DataSource -ne $previous){$previous.Dispose()}
        if($grid.DataSource -ne $table){$table.Dispose()}
    }
    } finally {
        $performanceWatch.Stop()
        if(Test-Path function:Add-ScoliaPerformanceSample){Add-ScoliaPerformanceSample 'Tableau' $performanceWatch.Elapsed.TotalMilliseconds}
    }
}
function Api-Name([string]$name) {return Get-ITMExcelMacroName $script:book 'ModuleApplicationITM' $name}

function Invoke-ITMExcelRead([string]$name) {
    if($name -notin @('ITMAppVersion','ITMAppSnapshot')){throw 'Cette fonction est réservée à la lecture de la passerelle.'}
    if($null -eq $script:book){throw 'Aucun classeur actif pour la lecture de la passerelle.'}
    $macro=Api-Name $name
    return Invoke-ITMExcelRunNoArguments $script:excel $macro
}

function App-Error($errorRecord,[bool]$committed=$false) {
    $exception=$null
    if($errorRecord -is [Management.Automation.ErrorRecord]){$exception=$errorRecord.Exception}
    elseif($errorRecord -is [Exception]){$exception=$errorRecord}
    $message=$(if($exception){[string]$exception.Message}else{[string]$errorRecord})
    if($exception -and $exception.InnerException){$message+="
"+$exception.InnerException.Message}
    if([string]::IsNullOrWhiteSpace($message)){$message='Une erreur inattendue a interrompu cette opération.'}
    if(-not $committed -and $message -match '438|Propriété ou méthode non gérée|Object doesn.t support'){
        $message+="

Pour les rapports : installez cette édition sur votre classeur ACTUEL avec INSTALLER, puis connectez la nouvelle copie corrigée. Si cette copie est déjà utilisée, essayez le format Excel et conservez les détails et le numéro de cette erreur."
    }
    $owner=$script:main
    foreach($candidate in @($script:correctionDialog,$script:promotionDialog,$script:ficheDialog)){if($candidate -and -not $candidate.IsDisposed -and $candidate.Visible){$owner=$candidate}}
    if($script:editDialog -and -not $script:editDialog.IsDisposed -and $script:editDialog.Visible){$owner=$script:editDialog}
    if($committed -and $script:committedSourcePath){
        $message="Le classeur a été créé et conservé :
"+$script:committedSourcePath+"

Sa connexion ou sa préparation a été interrompue. Dans Données, utilisez Ouvrir le dernier créé. Il est inutile de le générer à nouveau.

"+$message
        $script:status.Text='Classeur créé et conservé ; connexion à reprendre.'
    }elseif($committed){$message="L'enregistrement a réussi. Ne ressaisissez pas la même opération. L'affichage n'a pas pu être actualisé.

"+$message;$script:status.Text='Saisie enregistrée, actualisation interrompue.'}else{$script:status.Text='Opération interrompue. Consultez le message.'}
    Close-ScoliaTransition
    Clear-ITMActivityVisual
    [void][ITMErrorDialog]::Show($owner,$message,'ITM CD')
}
function Update-WorkspaceState {
    $ready=[bool]($script:book -and $script:data)
    foreach($panel in $script:pages.Values){if($panel -and -not $panel.IsDisposed){$panel.Enabled=$ready -and -not $script:busy}}
    if($script:pages.ContainsKey('Administration')){$script:pages['Administration'].Enabled=-not $script:busy}
    if($script:pages.ContainsKey('Données')){$script:pages['Données'].Enabled=-not $script:busy}
    if($script:pages.ContainsKey('Préférences')){$script:pages['Préférences'].Enabled=-not $script:busy}
    if($script:pages.ContainsKey('Diagnostic')){$script:pages['Diagnostic'].Enabled=-not $script:busy}
    if($script:pages.ContainsKey('Sécurité')){$script:pages['Sécurité'].Enabled=-not $script:busy}
    foreach($button in @($script:writeActions)){if($button -and -not $button.IsDisposed){$button.Enabled=$ready -and -not $script:busy -and -not $script:payNeedsRefresh -and -not $script:data.closed}}
    foreach($button in $script:nav.Values){if(-not $button.IsDisposed){$button.Enabled=-not $script:busy}}
    if($script:disconnect){$script:disconnect.Enabled=(-not $script:busy) -and [bool]($script:book -or $script:rememberedPath)}
    if($script:refresh){$script:refresh.Enabled=$ready -and -not $script:busy}
    if($script:connect){$script:connect.Enabled=-not $script:busy;$script:connect.Text=$(if($script:book){'Changer les données'}else{'Ouvrir les données'})}
    if($script:prepare){$script:prepare.Enabled=-not $script:busy}
    if($script:ribbonToggle){$script:ribbonToggle.Enabled=-not $script:busy}
    if($script:accountButton){$script:accountButton.Enabled=-not $script:busy}
    if($script:scoliaSession){$script:pages['Comptabilité'].Enabled=-not $script:busy;if($script:scoliaSession.Role -eq 'Comptabilité'){$script:pages['Tableau de bord'].Enabled=-not $script:busy};if($script:scoliaAutoControl){$script:scoliaAutoControl.Enabled=(Test-ScoliaPermission 'Recevoir')}}
    Update-PaymentButton
    Apply-ITMRoleControls
    if($script:schoolGrid -and $script:currentPage -eq 'Exports'){Update-ITMCardSelection}
    Update-ITMRibbonState
    Update-ScoliaFeatureCommands
    if(Test-Path function:Ensure-ScoliaDefaultWorkspaceView){Ensure-ScoliaDefaultWorkspaceView}
}
function Do-Work([scriptblock]$action,[string]$message='Traitement en cours...') {
    $performanceWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    if($script:busy){return}
    $activityAnchor=Get-ITMActivityAnchor;$script:busy=$true;$script:committedThisAction=$false;$script:committedSourcePath=''
    try {
        if($script:paySearch){$script:paySearch.ReadOnly=$true}
        Update-WorkspaceState
        Start-ITMActivity $message $activityAnchor
        [Windows.Forms.Application]::DoEvents();[void](& $action)
    }catch {App-Error $_ $script:committedThisAction}
    finally {
        # Release the operation gate even if animation disposal or a control fails.
        $script:busy=$false
        try{Restore-ScoliaBorrowedExcelSettings}catch{}
        try{Stop-ITMActivity}catch{$script:activityDepth=0}
        try{if($script:main -and -not $script:main.IsDisposed){$script:main.UseWaitCursor=$false}}finally{
            if($script:paySearch -and -not $script:paySearch.IsDisposed){$script:paySearch.ReadOnly=$false}
            Update-WorkspaceState
        }
    }
    } finally {
        $performanceWatch.Stop()
        if(Test-Path function:Add-ScoliaPerformanceSample){Add-ScoliaPerformanceSample 'Operation' $performanceWatch.Elapsed.TotalMilliseconds}
    }
}
# Preference belongs to this Windows user and survives application updates.
$script:connectionKey='HKCU:\Software\ITMCD\Secretariat'
$script:rememberedPath=''
 . (Join-Path $PSScriptRoot 'LocalisationClasseurs.ps1')
 . (Join-Path $PSScriptRoot 'MiseAJourPerformance.ps1')
function Read-ConnectionChoice {
    if((Get-ITMLocationPreference 'ConnexionAutomatique') -eq 'non'){return ''}
    $script:detectedLocation=Get-ITMWorkbookLocation -Purpose Connexion -Explicit $Classeur
    return $script:detectedLocation.Path
}
function Save-ConnectionChoice([string]$path) {
    try {
        $path=[IO.Path]::GetFullPath($path)
        if(-not (Test-Path -LiteralPath $script:connectionKey)){[void](New-Item -Path $script:connectionKey -Force -ErrorAction Stop)}
        [void](New-ItemProperty -LiteralPath $script:connectionKey -Name 'Classeur' -Value $path -PropertyType String -Force -ErrorAction Stop)
        $script:rememberedPath=$path
        # Remember the path independently of the user's automatic-opening choice.
        if(-not (Get-ITMLocationPreference 'ConnexionAutomatique')){
            if(-not (Set-ITMLocationPreference 'ConnexionAutomatique' 'oui')){return $false}
        }
        return $true
    }catch{return $false}
}
function Forget-ConnectionChoice {
    if(Test-Path -LiteralPath $script:connectionKey){
        $stored=Get-ItemProperty -LiteralPath $script:connectionKey -ErrorAction Stop
        if($stored.PSObject.Properties['Classeur']){Remove-ItemProperty -LiteralPath $script:connectionKey -Name 'Classeur' -ErrorAction Stop}
    }
    [void](Set-ITMLocationPreference 'ConnexionAutomatique' 'non')
    $script:rememberedPath='';$script:connectPath=''
}
function Disconnect-Book {
    if($script:book){
        try{if(-not $script:excelBorrowed){[void](Invoke-ITMExcelMethod $script:book 'Close' @($false,[Reflection.Missing]::Value,[Reflection.Missing]::Value))}}catch{}
        finally{try{if([Runtime.InteropServices.Marshal]::IsComObject($script:book)){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($script:book)}}catch{};$script:book=$null}
    }
    if($script:excel){
        try{if($script:excelBorrowed){Restore-ScoliaBorrowedExcelSettings}else{$script:excel.EnableEvents=$false;$script:excel.AutomationSecurity=3;$script:excel.Quit()}}catch{}
        finally{try{if([Runtime.InteropServices.Marshal]::IsComObject($script:excel)){[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($script:excel)}}catch{};$script:excel=$null}
    }
    Close-ITMStore
    $script:excelBorrowed=$false;$script:excelOriginalSettings=$null
    $script:connectedWorkbookPath='';$script:snapshotValid=$false;if($script:viewRevision){$script:viewRevision.Clear()};$script:peLedger=$null;$script:peIndex=@{};$script:peError='';$script:peVersion=$false;$script:data=$null;$script:paymentLookup=$null;$script:scoliaFinanceIndex=$null;if($script:schoolGrid){Refresh-ITMSchoolCards};$script:learnerIndex=@();$script:payMat='';$script:payReste=0;$script:payNeedsRefresh=$false
    if($script:payAmount){$script:payAmount.Value=0}
    if($script:paySave){$script:paySave.Enabled=$false}
    if($script:payIdentity){$script:payIdentity.Text='Sélectionnez un apprenant.'}
    if($script:paySituation){$script:paySituation.Text=''}
    foreach($value in @($script:cardValues)){if($value){$value.Text='—'}}
    foreach($chart in @($script:dashCharts)){if($chart){$chart.SetData([string[]]@(),[double[]]@())}}
    if($script:dashNote){$script:dashboardDate=(Get-Date).ToString('yyyyMMdd');$script:dashNote.Text='Connectez un classeur pour afficher vos indicateurs.'};if($script:dashUpdated){$script:dashUpdated.Text='Aucune actualisation'}
    if($script:dashPECValue){$script:dashPECValue.Text='—';$script:dashPECHint.Text='Connectez un classeur pour afficher les prises en charge.'}
    $script:lateRows=@();if($script:dashGrid){$script:dashGrid.DataSource=$null}
    # Éviter une collecte GC complète synchrone dans le chemin interactif : elle bloque lʼinterface.
}
function Connect-Book([string]$path) {
    if([IO.Path]::GetExtension($path) -ine '.xlsm'){throw 'Choisissez un classeur Excel .xlsm.'}
    Test-ITMWorkbookSet $path
    # Reuse a writable managed workbook already open in the user's Excel session.
    $managed=[IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))) 'registre.json'))
    if(-not $managed){$managed=[IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))) 'transaction.json'))}
    Disconnect-Book
    $phase='Ouverture Excel';$script:connectionDiagnostic='';$script:connectionMacro='';$script:connectionOperation='';$script:storeConnectionStep=''
    try {
        $script:excelBorrowed=$false;$script:excelOriginalSettings=$null
        if($true){
            try{$candidate=[Runtime.InteropServices.Marshal]::GetActiveObject('Excel.Application');$open=Find-ScoliaOpenWorkbook $candidate $path}catch{$candidate=$null;$open=$null}
            if($open){
                if([bool](Get-ITMExcelProperty $open 'ReadOnly')){throw 'Le classeur ouvert dans Excel est en lecture seule. Reconnectez une copie modifiable.'}
                if([IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName($path)) 'transaction.json'))){throw 'Une récupération est nécessaire. Fermez ce classeur dans Excel avant de reconnecter les données.'}
                $script:excel=$candidate;$script:excelBorrowed=$true;$script:excelOriginalSettings=@{}
                foreach($name in @('DisplayAlerts','EnableEvents','AutomationSecurity','ScreenUpdating','Calculation')){$script:excelOriginalSettings[$name]=Get-ITMExcelProperty $candidate $name}
            }
        }
        if(-not $script:excel -and -not $managed){$lock=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None);$lock.Dispose()}
        if(-not $script:excel){$script:excel=New-Object -ComObject Excel.Application;$script:excel.Visible=$false;$script:excel.DisplayAlerts=$false;$script:excel.EnableEvents=$false;$script:excel.AutomationSecurity=1}
        if(-not $script:excel -or -not $script:excel.Workbooks){throw 'Excel n''a pas fourni sa collection de classeurs.'}
        $phase='Ouverture et contrôle de la base'
        if($managed){$phase='Ouverture et vérification des classeurs data';Initialize-ITMStore $path}else{if($script:excelBorrowed){$script:book=$open}else{$script:book=$script:excel.Workbooks.Open($path,0,$false)}}
        $script:excel.AutomationSecurity=3
        if(-not $script:book){throw 'Aucun classeur actif n''a été retourné par Excel.'}
        if($script:book.ReadOnly){throw 'Le classeur est en lecture seule.'}
        $phase='Vérification de la passerelle'
        $version=Invoke-ITMExcelRead 'ITMAppVersion'
        if($version -notin @('ITM-DESKTOP-2','ITM-DESKTOP-3','ITM-DESKTOP-4')){throw 'Version de la passerelle non reconnue.'}
        if($version -eq 'ITM-DESKTOP-4' -and -not $script:store){
            $phase='Initialisation du dossier data';Initialize-ITMStore $path
        }
        if($null -eq $script:book){throw "La connexion ne dispose plus de son classeur actif après lʼinitialisation."}
        $script:connectedWorkbookPath=$(if($script:store -and $script:storeFolder){Join-Path $script:storeFolder ([string]$script:store.Active)}else{[IO.Path]::GetFullPath($path)})
        $phase='Mise à jour des modules connus'
        if($script:store){Update-ITMManagedRuntime}
        $path=[string]$script:book.FullName
        $phase='Lecture des données'
        Refresh-Data $true
        Initialize-ScoliaBlankWorkbook
        Restore-ScoliaBorrowedExcelSettings
    } catch {
        $failure=$_;$script:connectionDiagnostic=Write-ITMConnectionDiagnostic $phase $failure
        try{Disconnect-Book}catch{}
        if($script:fileLabel -and -not $script:fileLabel.IsDisposed){$script:fileLabel.Text='Aucun classeur connecté';$script:fileLabel.ForeColor=[Drawing.Color]::FromArgb(100,116,139)}
        throw ("Connexion impossible à l'étape « "+$phase+" » : "+$failure.Exception.Message+"

Conservez votre dossier data. Ne recréez pas le classeur. Fermez Excel si la base est déjà ouverte."+$(if($script:connectionDiagnostic){"
Diagnostic : "+$script:connectionDiagnostic}else{''}))
    }
    # From here, the database and snapshot are verified. UI failures do not close Excel.
    $remembered=Save-ConnectionChoice $path
    try{
        Refresh-ITMSnapshotChoices
        $script:paymentDatesAvailable=($version -in @('ITM-DESKTOP-3','ITM-DESKTOP-4'));$script:payDate.Enabled=$script:paymentDatesAvailable
        $script:payDate.MaxDate=(Get-Date).Date;$script:payDate.Value=(Get-Date).Date
        $script:payDateHint.Text=$(if($script:paymentDatesAvailable){'Date du reçu : aujourd''hui ou une date antérieure. Les mois sont affectés automatiquement.'}else{'Pour saisir une date antérieure, préparez votre classeur actuel avec cette nouvelle édition.'})
        $script:fileLabel.Text=$(if($script:store){'Dossier data connecté | '+$script:store.Segments.Count+' classeur(s)'}else{'Classeur connecté | Préparez cette édition pour activer data'}); $script:fileLabel.ForeColor=[Drawing.Color]::FromArgb(15,118,110)
        $script:inputHelp.SetToolTip($script:fileLabel, $(if($script:store){$script:storeFolder}else{[string]$script:book.FullName}))
        $script:status.Text=$(if($script:data.closed){'Année clôturée : consultation uniquement.'}else{$(if($script:store){'Dossier data connecté. Saisies sauvegardées automatiquement ; historique et rapports communs.'}else{'Classeur connecté. Préparez cette édition pour activer la gestion data.'})})
        if($script:runtimeUpdateNote){$script:status.Text=$script:runtimeUpdateNote}
        if(-not $remembered){$script:fileLabel.Text='Classeur connecté | Choix non mémorisé';$script:status.Text='Connexion réussie, mais Windows a refusé de mémoriser le choix. Reconnectez le classeur au prochain lancement.'}
        Refresh-ITMCurrentPage
        Update-WorkspaceState
    }catch{
        $script:connectionDiagnostic=Write-ITMConnectionDiagnostic 'Affichage après connexion' $_
        if($script:status){$script:status.Text='Base connectée et données vérifiées. Affichage incomplet ; utilisez Actualiser. '+$script:connectionDiagnostic}
        if($script:fileLabel){$script:fileLabel.Text='Base connectée | Affichage à actualiser'}
    }
}
function Set-Choices($combo,$values,[string]$default='') {
    if(-not $combo -or $combo.IsDisposed){return}
    $old=[string]$combo.Text; $combo.Items.Clear()
    foreach($v in @($values)){if([string]$v -ne ''){[void]$combo.Items.Add([string]$v)}}
    if($combo.Items.Contains($old)){$combo.SelectedItem=$old}elseif($combo.Items.Contains($default)){$combo.SelectedItem=$default}elseif($combo.Items.Count -gt 0){$combo.SelectedIndex=0}
}
function Refresh-Data([bool]$deferView=$false) {
    $performanceWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    $script:loading=$true;$script:snapshotValid=$false;$script:payNeedsRefresh=$true
    try {
        Set-ITMActivityMessage 'Lecture et contrôle du classeur...'
        $json=Invoke-ITMExcelRead 'ITMAppSnapshot'
        if([string]::IsNullOrWhiteSpace([string]$json)){throw 'Excel a retourné une réponse vide lors de la lecture.'}
        $script:data=([string]$json | ConvertFrom-Json -ErrorAction Stop)
        if(-not $script:data){throw 'La passerelle n''a pas retourné de données.'}
        foreach($required in @('learners','payments','promotions','months','balances','issues','closed')){
            if(-not $script:data.PSObject.Properties[$required]){throw ('Réponse de la passerelle incomplète : '+$required)}
        }
        Set-ITMActivityMessage 'Préparation des données et de la recherche...'
        Load-ITMPEC
        if(Test-Path function:Add-ScoliaDiagnostic){Add-ScoliaDiagnostic}
        Rebuild-ITMLearnerIndex
        # Build the balance lookup during the snapshot pass already required here.
        # Subsequent payments can then update one matricule without rescanning all balances.
        $script:balance=@{};$script:balanceRows=@{};$script:balanceKeysByMat=@{};$script:balanceIndexSource=$script:data.balances;$script:fees=@{}
        foreach($b in @($script:data.balances)){
            $balanceKey = Get-ITMBalanceKey $b.Matricule $b.Mois
            $balanceMat = Key ([string]$b.Matricule)
            $script:balance[$balanceKey] = [decimal]$b.Verse
            $script:balanceRows[$balanceKey] = $b
            if(-not $script:balanceKeysByMat.ContainsKey($balanceMat)){$script:balanceKeysByMat[$balanceMat]=New-Object 'System.Collections.Generic.List[string]'}
            $script:balanceKeysByMat[$balanceMat].Add($balanceKey)
        }
        foreach($p in @($script:data.promotions)){$script:fees[(Promo $p.Code)]=$p}
        if(Test-Path function:Publish-ScoliaRoster){try{Publish-ScoliaRoster}catch{$script:status.Text='Répertoire SCOLIA non publié : '+$_.Exception.Message}}
    } finally {$script:loading=$false}
    # Business data is current before any optional page or journal redraw.
    $script:snapshotValid=$true;$script:payNeedsRefresh=$false
    if($null -eq $script:viewRevision){$script:viewRevision=@{}}else{$script:viewRevision.Clear()}
    if(-not $deferView){Refresh-ITMSnapshotChoices;Refresh-ITMCurrentPage;Update-WorkspaceState}
    } finally {
        $performanceWatch.Stop()
        if(Test-Path function:Add-ScoliaPerformanceSample){Add-ScoliaPerformanceSample 'Lecture donnees' $performanceWatch.Elapsed.TotalMilliseconds}
    }
}

function Refresh-ITMSnapshotChoices {
    if(-not $script:data){return}
    $priorLoading=$script:loading;$script:loading=$true
    try{
        $promos=@('Toutes les promotions')+@($script:data.promotions | ForEach-Object {$_.Code} | Select-Object -Unique)
        Set-Choices $script:filterPromo $promos 'Toutes les promotions';Set-Choices $script:dashPromo $promos 'Toutes les promotions'
        Set-Choices $script:exportPromo $promos 'Toutes les promotions'
        $months=@('Tous les mois')+@($script:data.months)
        Set-Choices $script:dashMonth $months 'Tous les mois';Set-Choices $script:exportMonth $months 'Tous les mois'
        Set-Choices $script:payFilterMonth $months 'Tous les mois'
    }finally{$script:loading=$priorLoading}
}

function Learner-Rows([string]$search='', [string]$promotion='Toutes les promotions') {
    if($null -eq $script:data){return @()}
    $terms=@((Key $search) -split '\s+' | Where-Object {$_});$promoKey=Promo $promotion
    if($script:scoliaFast){return [ITMFast]::FilterLearners([object[]]@($script:learnerIndex),[string[]]$terms,($promotion -eq 'Toutes les promotions'),[string]$promoKey)}
    $result=New-Object 'System.Collections.Generic.List[object]'
    foreach($item in $script:learnerIndex){
        if($promotion -ne 'Toutes les promotions' -and $item.Promotion -ne $promoKey){continue}
        $match=$true
        foreach($term in $terms){if($item.Text.IndexOf($term,[StringComparison]::Ordinal) -lt 0){$match=$false;break}}
        if($match){$result.Add($item.Entry)}
    }
    return $result.ToArray()
}
function Refresh-Learners([string]$target='All') {
    if(Test-Path function:Sync-ScoliaDisciplineSnapshot){[void](Sync-ScoliaDisciplineSnapshot)}
    if($script:loading){return}
    if($target -in @('All','Learners')){
        $stamp = [string]::Format('{0}::{1}::{2}', [string]$script:dataRevision, (Key $script:search.Text), (Promo $script:filterPromo.Text))
        if($stamp -ne $script:learnerStamp){
            $rows=@(Learner-Rows $script:search.Text $script:filterPromo.Text)
            Set-Grid $script:learnersGrid $rows @(@('Matricule','Matricule'),@('Nom','Nom'),@('PostNom','Post-nom'),@('Prenom','Prénom'),@('Sexe','Sexe'),@('Promotion','Promotion'),@('Contact1','Contact parent'),@('Statut','Statut'))
            $script:countLabel.Text="$($rows.Count) dossier(s) affiché(s)";$script:learnerStamp=$stamp
        }
    }
    if($target -in @('All','Payments')){
        $stamp = [string]::Format('{0}::{1}', [string]$script:dataRevision, (Key $script:paySearch.Text))
        if($stamp -ne $script:payLearnerStamp){
            $payrows=@(Learner-Rows $script:paySearch.Text 'Toutes les promotions')
            Set-Grid $script:payLearnerGrid $payrows @(@('Matricule','Matricule'),@('Nom','Nom'),@('PostNom','Post-nom'),@('Prenom','Prénom'),@('Promotion','Promotion'))
            $script:payCount.Text="$($payrows.Count) résultat(s)";$script:payLearnerStamp=$stamp
            $script:payLearnerGrid.ShowCellToolTips=$true
        }
    }
}
function Refresh-Payments {
    if($script:loading -or $null -eq $script:data){return}
    $m=[string]$script:payFilterMonth.Text
    $rows=@(Get-ScoliaPaymentRows $script:payMat $m)
    Set-Grid $script:paymentGrid $rows @(@('Date','Date'),@('Numero','N° enregistrement'),@('Matricule','Matricule'),@('NomComplet','Nom complet'),@('Mois','Mois'),@('Verse','Montant affecté (FC)'),@('Statut','Statut'))
}
function Paid([string]$mat,[string]$month='') {
    if($month){
        $k = Get-ITMBalanceKey $mat $month
        if($script:balance.ContainsKey($k)){return [decimal]$script:balance[$k]}
        return [decimal]0
    }
    $total=[decimal]0;foreach($m in @($script:data.months)){$total+=Paid $mat ([string]$m)};return $total
}
function Due-Months($person,[datetime]$asOf=(Get-Date)) {
    $year=0
    if([string]$person.Annee -match '(\d{4})\s*[-–—]'){$year=[int]$Matches[1]}
    $result=New-Object 'System.Collections.Generic.List[string]'
    $all=@('Septembre','Octobre','Novembre','Décembre','Janvier','Février','Mars','Avril','Mai');if(Get-ITMPECRecord $person){$all=[string[]][ITMPE]::Months}
    $start=9;if($script:scolia){$all=[ScoliaLedger]::Schedule($script:scolia.StartMonth,$script:scolia.Months);$start=[int]$script:scolia.StartMonth}
    for($i=0;$i -lt $all.Count;$i++){
        $month=$start+$i;$yr=$year;if($month -gt 12){$month-=12;$yr++}
        if($year -gt 0 -and (New-Object DateTime($yr,$month,6)).AddMonths(1) -le $asOf.Date){$result.Add($all[$i])}
    }
    return $result.ToArray()
}
function Refresh-Dashboard {
    $performanceWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    if($script:scoliaSession -and $script:scoliaSession.Role -eq 'Comptabilité'){Refresh-ScoliaFinance;return}
    if($script:loading -or $null -eq $script:data){return}
    $promotion=[string]$script:dashPromo.Text;$month=[string]$script:dashMonth.Text
    $people=@((Learner-Rows '' $promotion) | Where-Object {$_.Eligible})
    $debtByPromotion=@{}
    $revenue=[decimal]0;$debt=[decimal]0;$settled=0;$rows=New-Object 'System.Collections.Generic.List[object]'
    foreach($a in $people){
        $fee=$script:fees[(Promo $a.Promotion)];$due=[decimal]0;$monthPaid=[decimal]0
        $dueMonths=@(Due-Months $a)
        $dueAmounts=@{}
        foreach($m in $dueMonths){$dueAmounts[$m]=Get-ITMPECDue $a $m}
        if($month -eq 'Tous les mois'){
            $revenue+=Paid $a.Matricule
            foreach($m in $dueMonths){$due+=$dueAmounts[$m]}
            $due=[Math]::Min($due,(Get-ITMPECPayingBalance $a))
        } else {
            $monthPaid=Paid $a.Matricule $month;$revenue+=$monthPaid;if($month -in $dueMonths){$due=$dueAmounts[$month]}
        }
        $debt+=$due;if($due -eq 0){$settled++}
        $group=[string]$a.Promotion;if(-not $debtByPromotion.ContainsKey($group)){$debtByPromotion[$group]=[decimal]0};$debtByPromotion[$group]+=$due
        $unpaidMonths=@($dueMonths | Where-Object {$dueAmounts[$_] -gt 0});$lateMonths=$unpaidMonths.Count
        if($due -gt 0 -and ($script:dashLate.Text -eq 'Tous' -or ($script:dashLate.Text -eq '1 mois' -and $lateMonths -eq 1) -or ($script:dashLate.Text -eq '2 mois' -and $lateMonths -eq 2) -or ($script:dashLate.Text -eq '3 mois et plus' -and $lateMonths -ge 3))){$rows.Add([pscustomobject]@{MoisEnRetard=$lateMonths;Contact1=$a.Contact1;Contact2=$a.Contact2;Matricule=$a.Matricule;Nom=($a.Nom+' '+$a.PostNom+' '+$a.Prenom);Promotion=$a.Promotion;Dette=(Money $due);DetteValeur=[decimal]$due;Periode=$(if($month -eq 'Tous les mois'){$unpaidMonths -join ', '}else{$month})})}
    }
    $script:cardValues[0].Text=[string]$people.Count;$script:cardValues[1].Text=Money $revenue;$script:cardValues[2].Text=Money $debt
    $script:cardValues[3].Text=$(if($people.Count){([decimal](100*$settled/$people.Count)).ToString('N1',$script:fr)+' %'}else{'—'})
    $script:lateRows=$rows.ToArray()
    Set-Grid $script:dashGrid $script:lateRows @(@('Matricule','Matricule'),@('Nom','Nom complet'),@('Promotion','Promotion'),@('Periode','Période'),@('Dette','Reste exigible'),@('MoisEnRetard','Mois en retard'),@('Contact1','Contact parent 1'),@('Contact2','Contact parent 2'))
    $script:dashNote.Text="$($people.Count) dossier(s) valides • $($rows.Count) avec un reste exigible • $(@($data.issues).Count) observation(s) à consulter dans Diagnostic."
    Update-ITMPECDashboard $people
    Update-ITMDashboardVisuals $people $debtByPromotion
    $script:dashboardDate=(Get-Date).ToString('yyyyMMdd')
    } finally {
        $performanceWatch.Stop()
        if(Test-Path function:Add-ScoliaPerformanceSample){Add-ScoliaPerformanceSample 'Tableau de bord' $performanceWatch.Elapsed.TotalMilliseconds}
    }
}
function Selected-Mat($grid) {
    if($grid.SelectedRows.Count -eq 0){throw 'Sélectionnez un apprenant dans la liste.'}
    $mat=[string]$grid.SelectedRows[0].Cells['Matricule'].Value
    if(-not $mat.Trim()){throw 'Ce dossier n''a pas de matricule. Vérifiez-le dans le classeur Excel.'}
    return $mat
}
function New-NavIcon([string]$name,[bool]$active=$false) {
    # Chaque espace possède son propre pictogramme. Les icônes sont dessinées
    # localement afin d'éviter les polices d'icônes externes et les doublons visuels.
    $bitmap=New-Object Drawing.Bitmap(28,28)
    $graphics=[Drawing.Graphics]::FromImage($bitmap)
    $color=[Drawing.Color]::White
    $tile=switch($name){
        'Tableau de bord'{[Drawing.Color]::FromArgb(41,98,174)}
        'Apprenants'{[Drawing.Color]::FromArgb(31,122,82)}
        'Paiements'{[Drawing.Color]::FromArgb(0,117,155)}
        'Exports'{[Drawing.Color]::FromArgb(173,101,22)}
        'Diagnostic'{[Drawing.Color]::FromArgb(185,62,68)}
        'Administration'{[Drawing.Color]::FromArgb(91,74,160)}
        'Sécurité'{[Drawing.Color]::FromArgb(70,86,151)}
        'Préférences'{[Drawing.Color]::FromArgb(91,103,120)}
        'Données'{[Drawing.Color]::FromArgb(43,111,126)}
        'Scolarité'{[Drawing.Color]::FromArgb(116,76,170)}
        'Comptabilité'{[Drawing.Color]::FromArgb(43,116,64)}
        default{[Drawing.Color]::FromArgb(0,102,204)}
    }
    $pen=New-Object Drawing.Pen($color,2)
    try {
        $graphics.Clear([Drawing.Color]::Transparent)
        $graphics.SmoothingMode=[Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $tilePath=[ITMDesign]::Rounded((New-Object Drawing.RectangleF(0.5,0.5,27,27)),6)
        $tileBrush=New-Object Drawing.SolidBrush($tile)
        try{$graphics.FillPath($tileBrush,$tilePath)}finally{$tileBrush.Dispose();$tilePath.Dispose()}
        $savedState=$graphics.Save();$graphics.TranslateTransform(3,3);$graphics.ScaleTransform(.78,.78)
        switch($name){
            'Tableau de bord' {
                $graphics.DrawRectangle($pen,3,3,8,8);$graphics.DrawRectangle($pen,17,3,8,8)
                $graphics.DrawRectangle($pen,3,17,8,8);$graphics.DrawRectangle($pen,17,17,8,8)
            }
            'Apprenants' {
                $graphics.DrawEllipse($pen,9,3,9,9);$graphics.DrawArc($pen,4,14,19,15,180,180)
                $graphics.DrawEllipse($pen,2,7,5,5);$graphics.DrawEllipse($pen,21,7,5,5)
            }
            'Paiements' {
                $graphics.DrawRectangle($pen,6,3,16,22)
                $graphics.DrawLine($pen,10,8,18,8);$graphics.DrawLine($pen,10,13,18,13)
                $graphics.DrawLine($pen,10,18,14,18);$graphics.DrawLine($pen,18,17,18,22)
                $graphics.DrawLine($pen,16,20,20,20)
            }
            'Exports' {
                $graphics.DrawLine($pen,14,3,14,17);$graphics.DrawLine($pen,8,11,14,17);$graphics.DrawLine($pen,20,11,14,17)
                $graphics.DrawLine($pen,4,18,4,24);$graphics.DrawLine($pen,4,24,24,24);$graphics.DrawLine($pen,24,24,24,18)
            }
            'Diagnostic' {
                $graphics.DrawEllipse($pen,4,4,14,14);$graphics.DrawLine($pen,15,15,24,24)
                $graphics.DrawLine($pen,11,7,11,12);$graphics.DrawEllipse($pen,10,14,2,2)
            }
            'Administration' {
                $graphics.DrawEllipse($pen,8,8,12,12);$graphics.DrawEllipse($pen,12,12,4,4)
                foreach($a in @(0,45,90,135,180,225,270,315)){
                    $rad=$a*[Math]::PI/180;$x1=14+[Math]::Cos($rad)*8;$y1=14+[Math]::Sin($rad)*8;$x2=14+[Math]::Cos($rad)*12;$y2=14+[Math]::Sin($rad)*12
                    $graphics.DrawLine($pen,[float]$x1,[float]$y1,[float]$x2,[float]$y2)
                }
            }
            'Sécurité' {
                $points=[Drawing.PointF[]]@((New-Object Drawing.PointF(14,2)),(New-Object Drawing.PointF(23,6)),(New-Object Drawing.PointF(22,16)),(New-Object Drawing.PointF(14,25)),(New-Object Drawing.PointF(6,16)),(New-Object Drawing.PointF(5,6)))
                $graphics.DrawPolygon($pen,$points);$graphics.DrawLine($pen,9,14,13,18);$graphics.DrawLine($pen,13,18,20,10)
            }
            'Préférences' {
                foreach($y in @(6,14,22)){$graphics.DrawLine($pen,3,$y,25,$y)}
                $graphics.DrawEllipse($pen,7,3,6,6);$graphics.DrawEllipse($pen,16,11,6,6);$graphics.DrawEllipse($pen,9,19,6,6)
            }
            'Données' {
                $graphics.DrawEllipse($pen,5,3,18,6);$graphics.DrawLine($pen,5,6,5,22);$graphics.DrawLine($pen,23,6,23,22)
                $graphics.DrawArc($pen,5,10,18,6,0,180);$graphics.DrawArc($pen,5,18,18,6,0,180)
            }
            'Scolarité' {
                $graphics.DrawPolygon($pen,[Drawing.PointF[]]@((New-Object Drawing.PointF(3,10)),(New-Object Drawing.PointF(14,4)),(New-Object Drawing.PointF(25,10)),(New-Object Drawing.PointF(14,16))))
                $graphics.DrawLine($pen,7,13,7,20);$graphics.DrawArc($pen,7,16,14,8,0,180);$graphics.DrawLine($pen,25,10,25,22)
            }
            'Comptabilité' {
                $graphics.DrawRectangle($pen,5,3,18,22);$graphics.DrawRectangle($pen,8,6,12,5)
                foreach($x in @(9,14,19)){foreach($y in @(15,20)){$graphics.DrawEllipse($pen,$x-1,$y-1,2,2)}}
            }
            default {
                $graphics.DrawEllipse($pen,5,5,18,18);$graphics.DrawLine($pen,14,8,14,20);$graphics.DrawLine($pen,8,14,20,14)
            }
        }
        $graphics.Restore($savedState)
    } finally {$pen.Dispose();$graphics.Dispose()}
    return $bitmap
}
function Show-Page([string]$name) {
    $performanceWatch=[Diagnostics.Stopwatch]::StartNew()
    try {
    if($script:scoliaSession -and $name -notin $script:tabsNames){throw 'Cet onglet ne fait pas partie de votre espace métier.'}
    $previousPage=[string]$script:scoliaDisplayedPage
    $changed=($previousPage -ne $name)
    $script:ribbonPeek=$false
    $keys=$(if($previousPage -and $script:pages.ContainsKey($previousPage)){@($previousPage,$name) | Select-Object -Unique}else{@($script:pages.Keys)})
    foreach($key in $keys){
        if(-not $changed){break}
        $active=$key -eq $name;$pages[$key].Visible=$active
        $nav[$key].Active=$active
        $nav[$key].ForeColor=$(if($active){[Drawing.Color]::White}else{[Drawing.Color]::FromArgb(71,85,105)})
        $navIconKey = [string]$key
        $navIconKey += '__'
        $navIconKey += [string]$active
        $nav[$key].Image = $script:navIcons[$navIconKey]
        $nav[$key].AccessibleDescription=$(if($active){'Onglet actif : '+$key}else{'Ouvrir '+$key})
    }
    if($changed){$pages[$name].BringToFront();$script:pageTitle.Text=$name;$script:scoliaDisplayedPage=$name}
    $script:currentPage=$name
    if($script:book -and $script:data -and (Test-ITMPageNeedsRefresh $name)){
        # Local presentation only: no busy cycle, Excel read or loading overlay on navigation.
        Refresh-ITMCurrentPage
    }
    Update-ITMRibbonState
    Update-ITMNavigation
    if(Test-Path function:Ensure-ScoliaDefaultWorkspaceView){Ensure-ScoliaDefaultWorkspaceView}
    } finally {
        $performanceWatch.Stop()
        if(Test-Path function:Add-ScoliaPerformanceSample){Add-ScoliaPerformanceSample ('Onglet: '+$name) $performanceWatch.Elapsed.TotalMilliseconds}
    }
}
function Show-Dossier([string]$mat='') {
    Assert-ITMPermission 'Saisie'
    if($null -eq $script:data){throw 'Connectez un classeur avant de saisir.'}
    if($script:payNeedsRefresh){throw 'Actualisez les données avant de modifier un dossier.'}
    if($script:data.closed){throw 'Cette année est clôturée. Connectez le nouveau classeur.'}
    $a=$null;if($mat){$foundRows=$(if(Test-Path function:Get-ITMLearnerMatches){@(Get-ITMLearnerMatches $mat)}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})});if($foundRows.Count -ne 1){throw 'Le matricule est absent ou en double.'};$a=$foundRows[0]}
    $script:lastMat=$mat
    $script:editMat=$mat;$script:editControls=@{};$fieldLabels=@();$fieldBoxes=@()
    $dialog=New-Object Windows.Forms.Form;Set-ITMWindowIcon $dialog; $script:editDialog=$dialog;$dialog.AutoScaleMode='Dpi'
    $dialog.Text=$(if($mat){'SCOLIA | Modifier le dossier '+$mat}else{"SCOLIA | Nouvelle inscription"})
    $dialog.MinimumSize=New-Object Drawing.Size([Math]::Min(780,$script:main.Width-24),[Math]::Min(600,$script:main.Height-24));$dialog.Size=New-Object Drawing.Size([Math]::Min(1040,$script:main.Width-24),[Math]::Min(850,$script:main.Height-24));$dialog.StartPosition='CenterParent';$dialog.Font=$script:main.Font
    $dialog.BackColor=[Drawing.Color]::White
    $header=New-Object Windows.Forms.Panel;$header.Dock='Top';$header.Height=100;$header.BackColor=[Drawing.Color]::White;$dialog.Controls.Add($header)
    $t=New-Label $header $(if($mat){'Modifier le dossier'}else{'Nouvelle inscription'}) 24 17 780 30 20 $true;$t.Tag='scolia:heading'
    $t=New-Label $header $(if($mat){"Matricule $mat · Les champs marqués * sont obligatoires."}else{'Matricule attribué automatiquement · Les champs marqués * sont obligatoires.'}) 24 55 780 20 10;$t.ForeColor=[Drawing.Color]::FromArgb(100,116,139)
    # Zone de confirmation : chaque instruction est separee pour eviter toute
    # concatenation ambigue lors de la livraison/copie du script PowerShell.
    $script:editFeedback = New-Label $header '' 24 78 780 22 10 $true
    $script:editFeedback.ForeColor = [Drawing.Color]::FromArgb(22,125,72)
    $script:editFeedback.BackColor = [Drawing.Color]::Transparent
    $script:editFeedback.Padding = New-Object Windows.Forms.Padding(0,2,0,2)
    $script:editFeedback.AccessibleName = 'Confirmation de sauvegarde'
    $script:editFeedback.Tag = 'scolia:feedback:success'
    $script:editFeedback.Visible = $false
    $script:editFeedback.Add_TextChanged({param($sender,$e)
        $sender.Visible=-not [string]::IsNullOrWhiteSpace($sender.Text)
        $owner=$sender.FindForm();if($owner -and $script:adaptivePanels.ContainsKey($owner)){Update-ITMDossierLayout $owner}
    })

    $footer = New-Object Windows.Forms.Panel
    $footer.Dock = 'Bottom'
    $footer.Height = 70
    $footer.BackColor = [Drawing.Color]::FromArgb(245,245,245)
    $dialog.Controls.Add($footer)

    $script:editSave = New-Button $footer 'Enregistrer le dossier' 500 18 300 $true
    $script:editSave.Anchor = 'Top,Right'
    $cancel=New-Button $footer 'Annuler' 24 18 150;$cancel.DialogResult='Cancel';$dialog.CancelButton=$cancel
    $body=New-Object Windows.Forms.Panel;$body.Dock='Fill';$body.BackColor=$dialog.BackColor;$body.AutoScroll=$true;$dialog.Controls.Add($body);$body.BringToFront()
    $spec=@(@('Nom','Nom'),@('PostNom','Post-nom'),@('Prenom','Prénom'),@('Sexe','Sexe'),@('Naissance','Date de naissance · jj/mm/aaaa'),@('Lieu','Lieu de naissance'),@('Contact1','Téléphone du parent · 10 chiffres'),@('Contact2','Autre téléphone · facultatif'),@('Promotion','Promotion'),@('Annee','Année scolaire'),@('Statut','Statut'))
    foreach($item in $spec){if($item[0] -in @('Nom','PostNom','Prenom','Sexe','Naissance','Lieu','Contact1','Promotion','Annee','Statut')){$item[1]+=' *'}}
    for($i=0;$i -lt $spec.Count;$i++){
        $field=$spec[$i][0];$x=24+($i%2)*390;$y=20+[int][Math]::Floor($i/2)*72
        $fieldLabels+=(New-Label $body $spec[$i][1] $x $y 350 22 10 $true)
        if($field -in @('Sexe','Promotion','Annee','Statut')){
            $c=New-Combo $body $x ($y+26) 350
            $choices=switch($field){'Sexe'{@('M','F')};'Promotion'{@($data.promotions | Where-Object {$_.Active} | ForEach-Object {$_.Code} | Select-Object -Unique)};'Annee'{@($data.promotions | Where-Object {$_.Active} | ForEach-Object {$_.Annee} | Select-Object -Unique)};'Statut'{@('Actif','Suspendu','Sorti')}}
            if($a -and $field -in @('Promotion','Annee') -and [string]$a.$field -notin $choices){$choices+=([string]$a.$field)}
            Set-Choices $c $choices
            if($a){
                $selectedValue=[string]$a.$field
                if($field -eq 'Statut' -and $a.PSObject.Properties['DisciplineBaseStatus']){
                    $selectedValue=[string]$a.DisciplineBaseStatus;$fieldLabels[-1].Text='Statut administratif *'
                    if($a.DisciplineInfo){$c.AccessibleDescription=$a.DisciplineInfo+' · Cette mesure reste gérée dans Scolarité > Discipline.';if($script:inputHelp){$script:inputHelp.SetToolTip($c,$c.AccessibleDescription)}}
                }
                if($c.Items.Contains($selectedValue)){$c.SelectedItem=$selectedValue}else{$c.SelectedIndex=-1}
            }elseif($field -in @('Sexe','Promotion')){$c.SelectedIndex=-1}
        } else {
            $c=$(if($field -eq 'Naissance'){New-ITMDateField}else{New-Object Windows.Forms.TextBox});$c.SetBounds($x,($y+26),350,30);$body.Controls.Add($c);if($a){$c.Text=[string]$a.$field}
            if($field -in @('Nom','PostNom','Prenom')){$c.CharacterCasing='Upper';$c.MaxLength=80}
            if($field -in @('Contact1','Contact2')){$c.MaxLength=10;$c.Add_KeyPress({param($sender,$e)if(-not [char]::IsControl($e.KeyChar) -and -not [char]::IsDigit($e.KeyChar)){$e.Handled=$true}})}
        }
        $script:editControls[$field]=$c;$fieldBoxes+=$c
    }
    # Preserve existing optional values outside the simplified form.
    $script:editTuteur=$(if($a){[string]$a.Tuteur}else{''})
    $addressLabel=New-Label $body 'Adresse de résidence · facultatif' 24 455 740 22 10 $true
    $address=New-Object Windows.Forms.TextBox;$address.SetBounds(24,481,740,58);$address.Multiline=$true;$address.MaxLength=250;$body.Controls.Add($address)
    if($a){$address.Text=[string]$a.Adresse};$script:editControls['Adresse']=$address
    $body.AutoScrollMinSize=New-Object Drawing.Size(770,558)
    $script:editErrors=New-Object Windows.Forms.ErrorProvider
    $script:editErrors.ContainerControl=$dialog;$script:editErrors.BlinkStyle='NeverBlink'
    foreach($field in @('Nom','PostNom','Prenom','Sexe','Promotion')){
        $c=$script:editControls[$field];$c.AccessibleDescription='Champ obligatoire'
    }
    if(-not $mat){
        $script:editControls['Annee'].Enabled=$false
        $script:editControls['Promotion'].Add_SelectedIndexChanged({
            $selected=@($script:data.promotions | Where-Object {(Promo $_.Code) -eq (Promo $script:editControls['Promotion'].Text) -and $_.Active})
            if($selected.Count -eq 1 -and $script:editControls['Annee'].Items.Contains([string]$selected[0].Annee)){$script:editControls['Annee'].SelectedItem=[string]$selected[0].Annee}
        })
    }

    # Un paiement historique ne verrouille plus la promotion ni l'annee du dossier.
    # Les regles de prise en charge et de cloture restent controlees par le moteur metier.
    $editSave.Add_Click({
        if($script:busy){return}
        if(-not (Test-ITMDossierFields)){return}
        $script:editSave.Enabled=$false;$script:editDone=$false
        Do-Work {
            $f=$script:editControls
            foreach($key in @('Nom','PostNom','Prenom','Naissance','Lieu','Contact1','Promotion','Annee','Sexe','Statut')){if(-not $f[$key].Text.Trim()){throw "Le champ $key est obligatoire."}}
            $result=Invoke-ITMDossier @([string]$script:editMat,[string]$f.Nom.Text,[string]$f.PostNom.Text,[string]$f.Prenom.Text,[string]$f.Sexe.Text,[string]$f.Naissance.Text,[string]$f.Lieu.Text,[string]$script:editTuteur,[string]$f.Contact1.Text,[string]$f.Contact2.Text,[string]$f.Promotion.Text,[string]$f.Annee.Text,[string]$f.Statut.Text,[string]$f.Adresse.Text)
            $reply=([string]$result | ConvertFrom-Json);if(-not $reply.ok){throw [string]$reply.error}
            $script:committedThisAction=$true
            $savedMat=[string]$reply.matricule
            $wasNew=[string]::IsNullOrWhiteSpace([string]$script:editMat)
            $draftClean=$true;if($wasNew){$draftClean=Complete-ScoliaRegistrationDraft $savedMat}
            $script:editFeedback.Tag='scolia:feedback:success'
            $script:lastMat=$savedMat;$script:editDone=$true;$script:committedThisAction=$true
            try{Write-ITMUserAudit 'Dossier enregistré' $savedMat}catch{}
            $script:payNeedsRefresh=$true
            if($wasNew){
                foreach($key in @('Nom','PostNom','Prenom','Naissance','Lieu','Contact1','Contact2','Adresse')){$control=$script:editControls[$key];if($control){$control.Clear()}}
                $script:editControls.Sexe.SelectedIndex=-1
                if($script:editControls.Statut.Items.Contains('Actif')){$script:editControls.Statut.SelectedItem='Actif'}
                # Keep the selected promotion so the next learner can be entered faster.
                $script:editFeedback.Text='✓ Inscription enregistrée · '+$savedMat+' · Prêt pour le prochain apprenant.'
            }else{
                $script:editFeedback.Text='✓ Modifications enregistrées · '+$savedMat
            }
            if($script:editErrors){$script:editErrors.Clear()}
            if($script:adaptivePanels.ContainsKey($script:editDialog)){Update-ITMDossierLayout $script:editDialog}
            $script:editFeedback.ForeColor=[Drawing.Color]::FromArgb(22,125,72)
            $script:status.Text='Dossier enregistré avec succès : '+$savedMat
            # Establish a clean baseline so closing after a successful save does not warn about saved data.
            $script:editOriginal=Get-ITMDossierFingerprint;$script:editDone=$false
            [void](Update-ITMAfterWrite $savedMat $true)
            if($wasNew){
                [ITMSuccessToast]::Show($script:editDialog,'Inscription enregistrée',('Matricule '+$savedMat+' · Vous pouvez saisir immédiatement le prochain apprenant.'))
                $script:editControls.Nom.Focus()
                if(-not $draftClean){$script:status.Text='Inscription enregistrée : '+$savedMat+' ; retrait du brouillon à réessayer. Ne ressaisissez pas ce dossier.'}
            }else{
                [ITMSuccessToast]::Show($script:editDialog,'Dossier mis à jour',('Les modifications du matricule '+$savedMat+' sont sauvegardées.'))
            }
        } 'Enregistrement du dossier et sauvegarde...'
        # A successful save never closes the registration dialog. Keep it available for the next learner.
        if(-not $script:editDialog.IsDisposed){$script:editSave.Enabled=$true}
    })
    $dialog.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}})
    Enable-ITMDossierLayout $dialog $body $header $footer $fieldLabels $fieldBoxes $addressLabel $address $script:editSave $cancel
    Enable-ITMRegistrationForm $dialog
    if(-not $mat){Enable-ScoliaRegistrationDraft $dialog}
    $dialog.Add_Shown({$script:editControls['Nom'].Focus()})
    try{[void]$dialog.ShowDialog($script:main)}finally{$script:editErrors.Dispose();$script:editErrors=$null;$dialog.Dispose();if($script:data){try{Refresh-ITMCurrentPage}catch{}}}
}
function Update-PaymentButton {
    if(-not $script:paySave -or -not $script:payAmount){return}
    $amount=Get-ScoliaTypedAmount $script:payAmount.Text $script:payAmount.Maximum
    $canWrite=$(if($script:scolia){Test-ScoliaPermission 'Encaisser'}else{Test-ITMPermission 'Saisie'})
    $script:paySave.Enabled=$canWrite -and [bool]($script:book -and -not $script:data.closed -and $script:payMat -and $script:payReste -gt 0 -and $null -ne $amount -and $amount -gt 0 -and $amount -le $script:payReste -and -not $script:busy -and -not $script:payNeedsRefresh)
}
function Reset-PaymentSelection([bool]$refresh=$true) {
    $script:payMat='';$script:payReste=0
    if($script:payAmount){$script:payAmount.Value=0}
    $script:payIdentity.Text='Sélectionnez un apprenant.';$script:paySituation.Text=''
    Update-PaymentButton
    if($refresh){Refresh-Payments}
}
function Choose-Payment([string]$mat) {
    $a=$(if(Test-Path function:Get-ITMLearnerMatches){@(Get-ITMLearnerMatches $mat)}else{@($script:data.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})})
    if($a.Count -ne 1 -or -not $a[0].Eligible){throw 'Dossier invalide ou en double. Consultez Diagnostic.'}
    if((Key $script:payMat) -ne (Key $mat)){$script:payAmount.Value=0}
    $a=$a[0];$script:payMat=$mat;$fee=$script:fees[(Promo $a.Promotion)];$paidTotal=Paid $mat
    $script:payIdentity.Text=$a.Nom+' '+$a.PostNom+' '+$a.Prenom+"
"+$mat+' • '+$a.Promotion
    $script:payDate.MaxDate=(Get-Date).Date
    $script:paySituation.Text='Mensualité : '+(Money $fee.Mensuel)+"
Total annuel versé : "+(Money $paidTotal)+"
Reste annuel : "+(Money ([Math]::Max(0,[decimal]$fee.Annuel-$paidTotal)))
    $script:payReste=Get-ITMPECPayingBalance $a
    if(Get-ITMPECRecord $a){$script:paySituation.Text='Régime : '+(Get-ITMPECStatus $a)+"
Mensualité : "+(Money $fee.Mensuel)+"
Total couvert (dix mois) : "+(Money (Get-ITMPECCoverage $a))+"
Paiements encaissés : "+(Money $paidTotal)+"
Reste payant : "+(Money $script:payReste)}
    Update-ITMPaymentContext $a $fee
    Update-PaymentButton
    Refresh-Payments
}

. (Join-Path $PSScriptRoot 'Lisibilite.ps1')
. (Join-Path $PSScriptRoot 'InterfaceResponsive.ps1')
. (Join-Path $PSScriptRoot 'Ergonomie.ps1')
. (Join-Path $PSScriptRoot 'AideUtilisation.ps1')
. (Join-Path $PSScriptRoot 'StockageClasseurs.ps1')
. (Join-Path $PSScriptRoot 'FonctionsGestion.ps1')
. (Join-Path $PSScriptRoot 'PerformanceInterface.ps1')
. (Join-Path $PSScriptRoot 'FicheModerne.ps1')
. (Join-Path $PSScriptRoot 'TableauDeBordModerne.ps1')
. (Join-Path $PSScriptRoot 'PagesModernes.ps1')
. (Join-Path $PSScriptRoot 'Raccourcis.ps1')
. (Join-Path $PSScriptRoot 'CommandesClavier.ps1')
. (Join-Path $PSScriptRoot 'StyleModerne.ps1')
. (Join-Path $PSScriptRoot 'ThemesAffichage.ps1')
. (Join-Path $PSScriptRoot 'PreferencesAffichage.ps1')
. (Join-Path $PSScriptRoot 'SonsApplication.ps1')
. (Join-Path $PSScriptRoot 'GestionDonnees.ps1')
. (Join-Path $PSScriptRoot 'Scolarite.ps1')
. (Join-Path $PSScriptRoot 'Personnalisation.ps1')
. (Join-Path $PSScriptRoot 'CotesDeliberation.ps1')
. (Join-Path $PSScriptRoot 'PrisesEnCharge.ps1')
. (Join-Path $PSScriptRoot 'Securite.ps1')
. (Join-Path $PSScriptRoot 'ExploitationProfessionnelle.ps1')
. (Join-Path $PSScriptRoot 'EtatEspace.ps1')
. (Join-Path $PSScriptRoot 'Comptes.ps1')
. (Join-Path $PSScriptRoot 'ScoliaIntegration.ps1')
. (Join-Path $PSScriptRoot 'PaiementsMultiplesV72.ps1')
. (Join-Path $PSScriptRoot 'FonctionnalitesV55.ps1')
. (Join-Path $PSScriptRoot 'InscriptionBrouillonsV55.ps1')
. (Join-Path $PSScriptRoot 'PresencesV55.ps1')
. (Join-Path $PSScriptRoot 'ReinscriptionV55.ps1')
. (Join-Path $PSScriptRoot 'CentreRechercheV55.ps1')
. (Join-Path $PSScriptRoot 'NettoyageSauvegardes.ps1')
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'SalairesV56.ps1')
. (Join-Path $PSScriptRoot 'ServicesPersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'InterfacePersonnelV56.ps1')
. (Join-Path $PSScriptRoot 'EnseignementsV59.ps1')
. (Join-Path $PSScriptRoot 'InterfaceEnseignementsV59.ps1')
. (Join-Path $PSScriptRoot 'DisciplineV60.ps1')
. (Join-Path $PSScriptRoot 'InterfaceDisciplineV60.ps1')
. (Join-Path $PSScriptRoot 'RapportRentreeV62.ps1')
. (Join-Path $PSScriptRoot 'SuiviApprenantsV56.ps1')
$script:security=[pscustomobject]@{Version=1;Enabled=$false;Password=$null;Recovery=$null;IdleMinutes=15;Minimize=$false;Failures=0;BlockedUntil='';Events=@()}
$script:initialSetupRequired=(-not (Read-ScoliaConfigurationWithRecovery))
if($script:initialSetupRequired){
    Update-ScoliaTransition 'Préparation de la configuration initiale...'
    if(-not (Show-ScoliaSetup)){Close-ScoliaTransition;return}
}
# L'indicateur reste visible jusqu'à l'affichage réel de la fenêtre suivante.
Update-ScoliaTransition 'Ouverture de la connexion sécurisée...'
[Windows.Forms.Application]::DoEvents()
if(-not (Show-ScoliaLogin)){Close-ScoliaTransition;return}
Update-ScoliaTransition 'Construction de votre espace de travail...'

$script:main=New-Object Windows.Forms.Form
$main.Text='SCOLIA | Secrétariat scolaire';$main.Size=New-Object Drawing.Size(1280,850);$main.MinimumSize=New-Object Drawing.Size(1100,730)
$workArea=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$main.MinimumSize=New-Object Drawing.Size([Math]::Min(360,$workArea.Width-24),[Math]::Min(360,$workArea.Height-24))
$main.Size=New-Object Drawing.Size([Math]::Min(1280,$workArea.Width-24),[Math]::Min(850,$workArea.Height-24))
$script:schoolIcon=New-Object Drawing.Icon((Join-Path $PSScriptRoot 'Assets/SCOLIA.ico'));$main.Icon=$script:schoolIcon
$main.StartPosition='CenterScreen';$main.Font=New-Object Drawing.Font('Segoe UI',10);$main.BackColor=[Drawing.Color]::FromArgb(243,243,243)
$main.AutoScaleMode='Dpi'
$script:navIcons=@{}
$bottom=New-Object Windows.Forms.StatusStrip;$bottom.BackColor=[Drawing.Color]::White
$script:status=New-Object Windows.Forms.ToolStripStatusLabel;$status.Text='Bienvenue. Préparez puis connectez votre classeur.';$status.Spring=$true;$status.TextAlign='MiddleLeft';$status.AutoToolTip=$true;$status.ToolTipText=$status.Text
$status.Add_TextChanged({param($sender,$e)$sender.ToolTipText=$sender.Text})
[void]$bottom.Items.Add($status);$main.Controls.Add($bottom)
$hostPanel=New-Object Windows.Forms.Panel;$hostPanel.Dock='Fill';$main.Controls.Add($hostPanel);$hostPanel.BringToFront()
$top=New-Object Windows.Forms.Panel;$top.Dock='Top';$top.Height=210;$top.BackColor=[Drawing.Color]::White;$hostPanel.Controls.Add($top)
$brand=New-Object Windows.Forms.Panel;$brand.Dock='Top';$brand.Height=72;$brand.BackColor=[Drawing.Color]::White;$top.Controls.Add($brand)
$script:brandLogo=New-Object Windows.Forms.PictureBox;$script:brandLogo.SizeMode='Zoom';$script:brandLogo.BackColor=[Drawing.Color]::Transparent;$script:brandLogo.AccessibleName='Logo SCOLIA';$brand.Controls.Add($script:brandLogo)
$logoPath=Join-Path $PSScriptRoot 'Assets/SCOLIA.png'
if(Test-Path -LiteralPath $logoPath){
    $sourceImage=[Drawing.Image]::FromFile($logoPath)
    try{$script:schoolLogo=New-Object Drawing.Bitmap($sourceImage)}finally{$sourceImage.Dispose()}
    $script:brandLogo.Image=$script:schoolLogo
}
$script:brandTitle=New-Label $brand 'SCOLIA' 110 12 370 32 20 $true
$script:brandNote=New-Label $brand ((Get-ITMInstitutionName)+' · Secrétariat scolaire') 110 48 390 36 10;$brandNote.ForeColor=[Drawing.Color]::FromArgb(90,106,134)
$script:brandSeparator=New-Object Windows.Forms.Panel;$script:brandSeparator.Dock='Bottom';$script:brandSeparator.Height=1;$script:brandSeparator.BackColor=[Drawing.Color]::FromArgb(224,229,237);$brand.Controls.Add($script:brandSeparator)
$actions=New-Object Windows.Forms.Panel;$actions.Dock='Right';$actions.Width=650;$brand.Controls.Add($actions);$script:actions=$actions
$connect=New-Button $actions 'Connecter un classeur' 0 18 210 $true
$prepare=New-Button $actions 'Préparer' 218 18 175
$refresh=New-Button $actions 'Actualiser' 401 18 115
$script:disconnect=New-Button $actions 'Déconnecter' 520 18 130
$disconnect.Add_Click({if($script:busy){return};Do-Work {Disconnect-Book;$script:fileLabel.Text='Aucun classeur connecté';$script:fileLabel.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$script:status.Text='Classeur fermé. Le choix mémorisé est conservé pour le prochain démarrage.'} 'Déconnexion...'})
$script:ribbonToggle=New-Button $brand 'Masquer les commandes' 0 10 215
$script:ribbonToggle.Add_Click({try{Toggle-ITMRibbonVisibility}catch{App-Error $_}})
$script:accountButton=New-Button $brand 'Compte' 0 10 120
$script:accountButton.Add_Click({if(-not $script:busy){try{[void](Show-ITMAccountWindow)}catch{App-Error $_}}})
Update-ITMAccountButton
$tabs=New-Object Windows.Forms.Panel;$tabs.AutoScroll=$false;$tabs.BackColor=[Drawing.Color]::FromArgb(249,250,251);$top.Controls.Add($tabs)
$tabsNames=@('Tableau de bord','Apprenants','Paiements','Exports','Diagnostic','Administration','Sécurité','Préférences','Données','Scolarité')
for($i=0;$i -lt $tabsNames.Count;$i++){
    $name=$tabsNames[$i]
    foreach($active in @($false, $true)){
        $navIconKey = [string]$name
        $navIconKey += '__'
        $navIconKey += [string]$active
        $script:navIcons[$navIconKey] = New-NavIcon $name $active
    }
    $b = New-Object ITMWorkspaceTab
    $b.Text = $name
    $b.Tag = $name
    $b.Font = New-Object -TypeName System.Drawing.Font -ArgumentList @('Segoe UI', 10)
    $b.BackColor=$tabs.BackColor
    $b.Cursor='Hand';$b.AccessibleName=$name;$b.TabIndex=$i
    $b.Add_Click({param($sender,$e)
        if($script:busy){return}
        $name=[string]$sender.Tag
        if($script:currentPage -eq $name -and $script:displayPreferences.RibbonMode -eq 'Onglets seuls'){
            $script:ribbonPeek=-not $script:ribbonPeek;Update-ITMNavigation
        }else{Show-Page $name}
    })
    $tabs.Controls.Add($b);$script:nav[$name]=$b
}
$script:pageTitle=New-Label $top 'Tableau de bord' 24 146 800 31 18 $true
$script:fileLabel=New-Label $top 'Aucun classeur connecté' 24 180 ($main.ClientSize.Width-48) 22 10
$fileLabel.Cursor=[Windows.Forms.Cursors]::Hand
$script:inputHelp.SetToolTip($fileLabel,'Cliquer pour ouvrir le dossier du classeur actuel.')
$fileLabel.Add_Click({try{$location=Get-ITMWorkbookLocation -Explicit $(if($script:book){[string]$script:book.FullName}else{''});Open-ITMWorkbookFolder $location}catch{App-Error $_}})
$fileLabel.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$fileLabel.AutoEllipsis=$true;$fileLabel.Anchor='Top,Left,Right'
$connect.Add_Click({if($script:busy){return};$d=New-Object Windows.Forms.OpenFileDialog;$d.Title='Choisir votre classeur ITM préparé';$d.Filter='Classeur Excel avec macros (*.xlsm)|*.xlsm';$location=Get-ITMWorkbookLocation -Explicit $(if($script:book){[string]$script:book.FullName}else{''});Set-ITMWorkbookDialogLocation $d $location;try{if($d.ShowDialog($script:main) -eq 'OK'){$script:connectPath=$d.FileName;Do-Work {Connect-Book $script:connectPath} 'Connexion au classeur...'}}finally{$d.Dispose()}})
$prepare.Add_Click({
    if(-not (Test-ITMPermission 'Administrer')){App-Error 'Préparation réservée à l''administrateur.';return}
    if($script:busy){return}
    Do-Work {
        $sourcePath=$(if($script:book){[string]$script:book.FullName}else{(Get-ITMWorkbookLocation -Purpose Installation).Path})
        Disconnect-Book;$script:fileLabel.Text='Préparation : choisissez ensuite le nouveau classeur corrigé.'
        $ps=Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $arguments='-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File "'+(Join-Path $PSScriptRoot 'Installer.ps1')+'"'
        if($sourcePath){$arguments+=' -Classeur "'+$sourcePath+'"'}
        Start-Process -FilePath $ps -ArgumentList $arguments
        $script:status.Text='L''installateur est ouvert. Après réussite, connectez le classeur corrigé.'
    }
})
$refresh.Add_Click({Do-Work {Refresh-Data;$script:status.Text='Données actualisées.'}})
$content=New-Object Windows.Forms.Panel;$content.Dock='Fill';$hostPanel.Controls.Add($content);$content.BringToFront()
foreach($name in $script:nav.Keys){$p=New-Object Windows.Forms.Panel;$p.Dock='Fill';$p.Size=$content.ClientSize;$p.Visible=$false;$p.BackColor=$script:main.BackColor;$p.AutoScroll=$true;$p.AutoScrollMinSize=New-Object Drawing.Size(1000,670);$content.Controls.Add($p);$script:pages[$name]=$p}

$p=$pages['Tableau de bord']
Build-ITMDashboard $p

$p=$pages['Apprenants']
$script:learnerSearchLabel=New-Label $p 'Matricule ou nom' 24 15 430 22 10 $true
$script:learnerPromotionLabel=New-Label $p 'Promotion' 462 15 245 22 10 $true
$script:learnerHint=New-Label $p 'Retrouvez un dossier, puis choisissez une action. La recherche filtre les résultats automatiquement.' 24 0 940 42 10
$script:search=New-Object Windows.Forms.TextBox;$search.SetBounds(24,43,420,28);$p.Controls.Add($search);$search.CharacterCasing='Upper'
$script:filterPromo=New-Combo $p 462 43 245
$new=New-Button $p 'Nouvelle inscription' 728 36 230 $true
$script:countLabel=New-Label $p 'Aucun dossier chargé' 24 86 380 24
$edit=New-Button $p 'Modifier le dossier' 24 120 225
$payFromList=New-Button $p 'Enregistrer un paiement' 265 120 270
$fiche=New-Button $p 'Fiche complète' 550 120 195
$recent=New-Button $p 'Dernier dossier' 759 120 198
$script:deleteLearner=New-Button $p 'Supprimer l''apprenant' 24 160 230;$script:deleteLearner.Destructive=$true
$clearSearch=New-Button $p 'Effacer la recherche' 462 82 245
$script:learnerReset=$clearSearch;$script:learnerActions=@($new,$edit,$payFromList,$fiche,$recent,$script:deleteLearner)
$script:learnerSurface=New-Object Windows.Forms.Panel;$script:learnerSurface.BackColor=$p.BackColor;$p.Controls.Add($script:learnerSurface);$script:learnerSurface.SendToBack()
foreach($label in @($script:learnerSearchLabel,$script:learnerPromotionLabel,$script:countLabel)){$label.BackColor=[Drawing.Color]::White}
$clearSearch.Add_Click({if(-not $script:busy){$script:search.Clear();$script:filterPromo.SelectedItem='Toutes les promotions';$script:search.Focus()}})
$fiche.Add_Click({try{if(-not $script:busy){Show-Fiche (Selected-Mat $script:learnersGrid)}}catch{App-Error $_}})
$recent.Add_Click({try{if(-not $script:busy){if(-not $script:lastMat){throw 'Aucun dossier consulté pendant cette session.'};Show-Fiche $script:lastMat}}catch{App-Error $_}})
$script:learnersGrid=New-Grid $p 24 174 946 460
$search.Add_TextChanged({Schedule-ITMSearch 'Learners'});$filterPromo.Add_SelectedIndexChanged({Schedule-ITMSearch 'Learners'})
$new.Add_Click({try{if(-not $script:busy){Show-Dossier}}catch{App-Error $_}})
$edit.Add_Click({try{if(-not $script:busy){Show-Dossier (Selected-Mat $script:learnersGrid)}}catch{App-Error $_}})
function Request-ITMReason([string]$title,[string]$prompt,[string]$default='') {
    $dlg = New-Object Windows.Forms.Form
    Set-ITMWindowIcon $dlg
    $dlg.Text = $title
    $dlg.Size = New-Object Drawing.Size(620,300)
    $dlg.MinimumSize = New-Object Drawing.Size(560,280)
    $dlg.StartPosition = 'CenterParent'
    $dlg.Font = $script:main.Font
    $dlg.BackColor = [Drawing.Color]::White

    [void](New-Label $dlg $prompt 22 20 560 54 11 $true)

    $box = New-Object Windows.Forms.TextBox
    $box.Multiline = $true
    $box.ScrollBars = 'Vertical'
    $box.SetBounds(22,82,560,95)
    $box.Text = $default
    $dlg.Controls.Add($box)

    $hint = New-Label $dlg 'Motif obligatoire : au moins 5 caracteres. Il sera inscrit dans le journal d audit.' 22 184 560 32 9
    $hint.ForeColor = [Drawing.Color]::FromArgb(100,116,139)

    $ok = New-Button $dlg 'Continuer' 330 222 120 $true
    $cancel = New-Button $dlg 'Annuler' 462 222 120
    $script:reasonResult = $null

    $ok.Add_Click({
        $reasonText = [string]$box.Text
        if([string]::IsNullOrWhiteSpace($reasonText)){
            [void][ITMMessageBox]::Show($dlg,'Saisissez un motif precis contenant au moins 5 caracteres.','Motif obligatoire','OK','Warning')
            return
        }
        $reasonText = $reasonText.Trim()
        if($reasonText.Length -lt 5){
            [void][ITMMessageBox]::Show($dlg,'Saisissez un motif precis contenant au moins 5 caracteres.','Motif obligatoire','OK','Warning')
            return
        }
        $script:reasonResult = $reasonText
        $dlg.DialogResult = 'OK'
        $dlg.Close()
    })

    $cancel.Add_Click({
        $dlg.DialogResult = 'Cancel'
        $dlg.Close()
    })

    $dlg.AcceptButton = $ok
    $dlg.CancelButton = $cancel

    try{
        [void]$dlg.ShowDialog($script:main)
        return $script:reasonResult
    }
    finally{
        $dlg.Dispose()
        $script:reasonResult = $null
    }
}

function Show-ITMDuplicateMerge {
    if($script:busy){return}
    Assert-ITMPermission 'Saisie'

    if($script:duplicatesGrid.SelectedRows.Count -ne 1){
        throw 'Selectionnez un doublon dans la liste du diagnostic.'
    }

    $row = $script:duplicatesGrid.SelectedRows[0]
    $matA = [string]$row.Cells['Matricule'].Value
    $matB = [string]$row.Cells['Autre matricule'].Value
    $a = @(Get-ITMLearnerMatches $matA)[0]
    $b = @(Get-ITMLearnerMatches $matB)[0]

    if(-not $a -or -not $b){
        throw 'Les deux dossiers ne sont plus disponibles. Actualisez les donnees.'
    }

    $dlg = New-Object Windows.Forms.Form
    Set-ITMWindowIcon $dlg
    $dlg.Text = 'SCOLIA | Fusion controlee des doublons'
    $dlg.Size = New-Object Drawing.Size(760,470)
    $dlg.MinimumSize = New-Object Drawing.Size(700,430)
    $dlg.StartPosition = 'CenterParent'
    $dlg.Font = $script:main.Font
    $dlg.BackColor = [Drawing.Color]::White

    [void](New-Label $dlg 'Fusionner deux dossiers apres verification' 24 18 680 32 18 $true)
    [void](New-Label $dlg 'Les paiements du dossier fusionne seront rattaches au matricule conserve. Le dossier fusionne sera retire et l operation sera inscrite au journal.' 24 58 680 50 10)
    [void](New-Label $dlg 'Dossier a conserver' 24 122 680 22 10 $true)

    $keep = New-Object Windows.Forms.ComboBox
    $keep.DropDownStyle = 'DropDownList'
    $keep.SetBounds(24,148,680,32)
    $dlg.Controls.Add($keep)

    [void](New-Label $dlg 'Dossier a fusionner' 24 188 680 22 10 $true)

    $merge = New-Object Windows.Forms.ComboBox
    $merge.DropDownStyle = 'DropDownList'
    $merge.SetBounds(24,214,680,32)
    $dlg.Controls.Add($merge)

    foreach($item in @($a,$b)){
        $display = [string]$item.Matricule
        $display += ' - '
        $display += [string]$item.Nom
        $display += ' '
        $display += [string]$item.PostNom
        $display += ' '
        $display += [string]$item.Prenom
        $display += ' - '
        $display += [string]$item.Promotion
        [void]$keep.Items.Add($display)
        [void]$merge.Items.Add($display)
    }

    $keep.SelectedIndex = 0
    $merge.SelectedIndex = 1

    [void](New-Label $dlg 'Motif de fusion obligatoire' 24 256 680 22 10 $true)
    $reason = New-Object Windows.Forms.TextBox
    $reason.SetBounds(24,282,680,30)
    $dlg.Controls.Add($reason)

    $hint = New-Label $dlg 'Verifiez les deux matricules et les paiements avant de confirmer.' 24 320 680 24 9
    $hint.ForeColor = [Drawing.Color]::FromArgb(100,116,139)

    $go = New-Button $dlg 'Fusionner les dossiers' 410 360 180 $true
    $cancel = New-Button $dlg 'Annuler' 602 360 102

    $go.Add_Click({
        try{
            if($keep.SelectedIndex -lt 0 -or $merge.SelectedIndex -lt 0){
                throw 'Choisissez les deux dossiers.'
            }
            if($keep.SelectedIndex -eq $merge.SelectedIndex){
                throw 'Choisissez deux dossiers differents.'
            }

            $mergeReason = [string]$reason.Text
            if([string]::IsNullOrWhiteSpace($mergeReason)){
                throw 'Indiquez un motif de fusion contenant au moins 5 caracteres.'
            }
            $mergeReason = $mergeReason.Trim()
            if($mergeReason.Length -lt 5){
                throw 'Indiquez un motif de fusion contenant au moins 5 caracteres.'
            }

            if($keep.SelectedIndex -eq 0){
                $keepMat = [string]$a.Matricule
            }
            else{
                $keepMat = [string]$b.Matricule
            }

            if($merge.SelectedIndex -eq 0){
                $mergeMat = [string]$a.Matricule
            }
            else{
                $mergeMat = [string]$b.Matricule
            }

            [void](Assert-ITMLearnerMerge $script:data $keepMat $mergeMat $mergeReason)

            $lines = @()
            $lines += 'Confirmer la fusion ?'
            $lines += ''
            $lines += ('Dossier conserve : ' + $keepMat)
            $lines += ('Dossier retire : ' + $mergeMat)
            $lines += ''
            $lines += 'Les paiements seront reaffectes au matricule conserve. Verifiez les informations avant confirmation.'
            $q = $lines -join [Environment]::NewLine

            $answer = [ITMMessageBox]::Show($dlg,$q,'Confirmation de fusion','YesNo','Warning',[Windows.Forms.MessageBoxDefaultButton]::Button2)
            if($answer -ne 'Yes'){
                return
            }

            $dlg.Tag = @($keepMat,$mergeMat,$mergeReason)
            $dlg.DialogResult = 'OK'
            $dlg.Close()
        }
        catch{
            [void][ITMMessageBox]::Show($dlg,$_.Exception.Message,'Fusion impossible','OK','Warning')
        }
    })

    $cancel.Add_Click({
        $dlg.DialogResult = 'Cancel'
        $dlg.Close()
    })
    $dlg.CancelButton = $cancel

    try{
        $result = $dlg.ShowDialog($script:main)
        if($result -eq 'OK' -and $dlg.Tag){
            $mergeRequest = @($dlg.Tag)
            Do-Work {
                $keepValue = [string]$mergeRequest[0]
                $mergeValue = [string]$mergeRequest[1]
                $reasonValue = [string]$mergeRequest[2]
                $reply = Invoke-ITMLearnerMerge $keepValue $mergeValue $reasonValue
                $script:committedThisAction = $true
                $auditDetail = [string]::Format('{0} / {1} / {2}', $keepValue, $mergeValue, $reasonValue)
                Write-ITMUserAudit 'Dossiers apprenants fusionnes' $auditDetail
                Refresh-Data
                Refresh-Administration
                $script:status.Text = [string]::Format('Fusion terminee : {0} rattache a {1}', $mergeValue, $keepValue)
                [void][ITMMessageBox]::Show($script:main,'Fusion terminee. Les paiements ont ete rattaches au matricule conserve.','SCOLIA','OK','Information')
            } 'Fusion et sauvegarde des dossiers...'
        }
    }
    finally{
        $dlg.Dispose()
    }
}

$script:deleteLearner.Add_Click({
    try{
        if($script:busy){return};Assert-ITMPermission 'Saisie'
        $mat=Selected-Mat $script:learnersGrid
        $snapshot=Invoke-ITMJsonDirect 'ITMAppSnapshot'
        $learner=@($snapshot.learners | Where-Object {(Key $_.Matricule) -eq (Key $mat)})
        if($learner.Count -ne 1){throw 'Dossier absent ou matricule en double : suppression bloquée.'};$learner=$learner[0]
        if(Get-ITMPECRecord $learner){throw 'Un historique de prise en charge existe. Conservez le dossier avec le statut Sorti ou Suspendu.'}
        $payments=@($snapshot.payments | Where-Object {(Key $_.Matricule) -eq (Key $mat)}).Count
        $identity=($learner.Nom+' '+$learner.PostNom+' '+$learner.Prenom+' · '+$learner.Promotion)
        $motif=Request-ITMReason 'SCOLIA | Motif de suppression' ("Indiquez pourquoi le dossier de $identity (matricule $mat) doit être supprimé.")
        if(-not $motif){return}
        $deleteLines = @()
        $deleteLines += 'Supprimer cet apprenant ?'
        $deleteLines += ''
        $deleteLines += [string]$identity
        $deleteLines += ('Matricule : ' + [string]$mat)
        $deleteLines += ('Paiements existants : ' + [string]$payments)
        $deleteLines += ''
        $deleteLines += 'Le dossier sera retire. Les paiements historiques seront conserves et la suppression sera journalisee.'
        $question = $deleteLines -join [Environment]::NewLine
        if([ITMMessageBox]::Show($script:main,$question,'Confirmer la suppression','YesNo','Warning',[Windows.Forms.MessageBoxDefaultButton]::Button2) -ne 'Yes'){return}
        $script:deleteMat=$mat;$script:deleteReason=$motif
        Do-Work {
            $reply=Invoke-ITMLearnerDeletion $script:deleteMat $script:deleteReason
            $script:committedThisAction=$true
            $deleteAudit = [string]::Format('{0} / {1}', [string]$script:deleteMat, [string]$script:deleteReason)
            Write-ITMUserAudit 'Apprenant supprime' $deleteAudit
            $script:payNeedsRefresh=$true
            if($script:lastMat -eq $script:deleteMat){$script:lastMat=''}
            Refresh-Data;$script:status.Text='Apprenant supprimé : '+$script:deleteMat
            [void][ITMMessageBox]::Show($script:main,'Dossier supprimé. Les paiements historiques éventuels sont conservés.','SCOLIA','OK','Information')
        } 'Suppression du dossier et sauvegarde...'
    }catch{App-Error $_}
})
$learnersGrid.Add_CellDoubleClick({param($sender,$e)if($e.RowIndex -ge 0 -and -not $script:busy){try{Show-Fiche (Selected-Mat $script:learnersGrid)}catch{App-Error $_}}})
$payFromList.Add_Click({try{if(-not $script:busy){Choose-Payment (Selected-Mat $script:learnersGrid);Show-Page 'Paiements'}}catch{App-Error $_}})

$p=$pages['Paiements']
$script:peManage=New-Button $p 'Prises en charge' 24 950 260
$script:peManage.Add_Click({try{if(-not $script:busy){Show-ITMPECList}}catch{App-Error $_}})
$p.AutoScrollMinSize=New-Object Drawing.Size(1000,960)
[void](New-Label $p 'Rechercher un apprenant' 24 15 450 22 10 $true)
$script:paySearch=New-Object Windows.Forms.TextBox;$paySearch.SetBounds(24,43,946,30);$paySearch.CharacterCasing='Upper';$p.Controls.Add($paySearch)
$script:payLearnerGrid=New-Grid $p 24 86 946 260;$payLearnerGrid.Anchor='Top,Left,Right'
$selectPay=New-Button $p 'Sélectionner cet apprenant' 24 359 300
$clearPay=New-Button $p 'Effacer la recherche' 770 359 198
$clearPay.Add_Click({if(-not $script:busy){$script:paySearch.Clear();Reset-PaymentSelection;$script:paySearch.Focus()}})
$script:payCount=New-Label $p '' 562 366 200 22 10
$allPayments=New-Button $p 'Tous les paiements' 340 359 204
$allPayments.Add_Click({if(-not $script:busy){Reset-PaymentSelection}})
$script:batchPayments=New-Button $p 'Paiements multiples' 560 359 200 $true
$script:batchPayments.Add_Click({try{if(-not $script:busy){Show-ITMBatchPayment -Source 'Paiements'}}catch{App-Error $_}})
$script:payIdentity=New-Label $p 'Sélectionnez un apprenant.' 24 410 480 65 13 $true
$script:paySituation=New-Label $p '' 24 460 480 145 11
[void](New-Label $p 'Montant du reçu validé à la caisse (FC)' 570 410 395 26 10 $true)
$script:payAmount=New-Object Windows.Forms.NumericUpDown;$payAmount.SetBounds(570,443,300,32);$payAmount.Maximum=100000000000;$payAmount.Minimum=0;$payAmount.Value=0;$payAmount.ThousandsSeparator=$true;$p.Controls.Add($payAmount)
$script:paySave=New-Button $p 'Enregistrer le paiement' 570 490 395 $true;$paySave.Enabled=$false
[void](New-Label $p 'Date du reçu validé à la caisse' 570 486 395 26 10 $true)
    $script:payDate=New-Object Windows.Forms.DateTimePicker;$script:payDate.Format='Custom';$script:payDate.CustomFormat='dd/MM/yyyy';$script:payDate.MinDate=New-Object DateTime(1900,1,1);$script:payDate.MaxDate=(Get-Date).Date;$script:payDate.Value=(Get-Date).Date;$script:payDate.SetBounds(570,516,395,34);$p.Controls.Add($script:payDate)
    $script:payDateHint=New-Label $p 'Date du reçu : aujourd''hui ou une date antérieure. Les mois sont affectés automatiquement.' 24 580 946 64 10
    $script:paySave.SetBounds(570,562,395,44)
[void](New-Label $p 'Historique des affectations' 24 620 500 28 12 $true)
$script:payFilterMonth=New-Combo $p 24 658 235
$correct=New-Button $p 'Corriger le paiement sélectionné' 285 652 400
$correct.Add_Click({try{if(-not $script:busy){Show-CorrectionPaiement}}catch{App-Error $_}})
$script:paymentGrid=New-Grid $p 24 704 946 230
$paySearch.Add_TextChanged({if(-not $script:loading -and -not $script:busy){Reset-PaymentSelection $false;Schedule-ITMSearch 'Payments'}});$payFilterMonth.Add_SelectedIndexChanged({Refresh-Payments})
$payAmount.Add_ValueChanged({Update-PaymentButton})
$payAmount.Add_TextChanged({Update-PaymentButton})
$payLearnerGrid.Add_CellDoubleClick({param($sender,$e)if($e.RowIndex -ge 0 -and -not $script:busy){try{Choose-Payment (Selected-Mat $script:payLearnerGrid)}catch{App-Error $_}}})
$payLearnerGrid.Add_KeyDown({param($sender,$e)if($e.KeyCode -eq 'Enter' -and -not $script:busy){$e.Handled=$true;$e.SuppressKeyPress=$true;try{Choose-Payment (Selected-Mat $script:payLearnerGrid)}catch{App-Error $_}}})
$selectPay.Add_Click({try{if(-not $script:busy){Choose-Payment (Selected-Mat $script:payLearnerGrid)}}catch{App-Error $_}})
$paySave.Add_Click({
    if($script:busy){return}
    if($script:scolia){try{Show-ScoliaEntry}catch{App-Error $_};return}
    if(-not $script:payMat){return}
    if($script:payNeedsRefresh){return}
    $typedAmount=Get-ScoliaTypedAmount $script:payAmount.Text $script:payAmount.Maximum
    if($null -eq $typedAmount -or $typedAmount -le 0 -or $typedAmount -gt $script:payReste){
        [void][ITMMessageBox]::Show($script:main,'Saisissez un montant positif inférieur ou égal au reste annuel : '+(Money $script:payReste),'Montant incorrect','OK','Warning')
        return
    }
    $script:payAmount.Value=$typedAmount
    $script:paymentAmountToSave=[decimal]$typedAmount
    if($script:payDate.Value.Date -gt (Get-Date).Date){[void][ITMMessageBox]::Show($script:main,'La date du reçu ne peut pas être future.','Date du reçu','OK','Warning');return}
    $script:payDateISO=$script:payDate.Value.ToString('yyyy-MM-dd',[Globalization.CultureInfo]::InvariantCulture)
    $answer=[ITMMessageBox]::Show($script:main,'Date du reçu : '+$script:payDate.Value.ToString('dd/MM/yyyy')+"
Enregistrer "+(Money $script:paymentAmountToSave)+' pour '+$script:payMat+' ?','Confirmation du paiement','YesNo','Question')
    if($answer -ne 'Yes'){return}
    $script:paySave.Enabled=$false;$script:payCommit=$false
    Do-Work {
        $arguments=@([string]$script:payMat,[double]$script:paymentAmountToSave);if($script:paymentDatesAvailable){$arguments+=([string]$script:payDateISO)}
        $similar=@(Invoke-ITMJson 'ITMAppDoublonsPaiement' $arguments)
        if($similar.Count -gt 0){
            $numbers=(@($similar | ForEach-Object {$_.Numero}) -join ', ')
            if([ITMMessageBox]::Show($script:main,'Un paiement du même montant existe déjà à la date du reçu : '+$numbers+'. Vérifiez le reçu. Enregistrer un autre versement malgré cette ressemblance ?','Doublon possible','YesNo','Warning') -ne 'Yes'){return}
        }
        $result=Invoke-ITMPayment ([string]$script:payMat) ([double]$script:paymentAmountToSave) ([string]$script:payDateISO)
        $reply=([string]$result | ConvertFrom-Json);if(-not $reply.ok){throw [string]$reply.error}
        $script:payCommit=$true;$script:committedThisAction=$true;Write-ITMUserAudit 'Paiement enregistré' ([string]$reply.numero);$script:payNeedsRefresh=$true;$script:payAmount.Value=0;$script:status.Text='Paiement enregistré : '+$reply.numero
        $script:payDate.MaxDate=(Get-Date).Date;$script:payDate.Value=(Get-Date).Date
        [void](Update-ITMAfterWrite ([string]$script:payMat))
        [ITMSuccessToast]::Show($script:main,'Paiement enregistré',((Money $reply.montant)+' · N° '+$reply.numero+' · '+$reply.mois+' mois concerné(s).'))
    } 'Enregistrement du paiement et sauvegarde...'
    Update-PaymentButton
})

$script:exportTabs=[pscustomobject]@{SelectedIndex=0}
$script:exportReportsPage=New-Object Windows.Forms.Panel;$script:exportReportsPage.Dock='Fill';$script:exportReportsPage.AutoScroll=$true
$script:exportCardsPage=New-Object Windows.Forms.Panel;$script:exportCardsPage.Dock='Fill';$script:exportCardsPage.AutoScroll=$true;$script:exportCardsPage.Visible=$false
$pages['Exports'].Controls.Add($script:exportReportsPage);$pages['Exports'].Controls.Add($script:exportCardsPage);$script:exportReportsPage.BringToFront()
$p=$script:exportReportsPage
[void](New-Label $p 'Documents et rapports' 24 20 800 38 20 $true)
$exportGuide = '1. Choisir les filtres  |  2. Verifier les effectifs  |  3. Enregistrer le document'
[void](New-Label -parent $p -text $exportGuide -x 24 -y 70 -w 900 -h 48 -size 11 -bold $true)
[void](New-Label $p 'Promotion' 24 132 240 22 10 $true);$script:exportPromo=New-Combo $p 24 162 320
[void](New-Label $p 'Mois' 374 132 240 22 10 $true);$script:exportMonth=New-Combo $p 374 162 260
[void](New-Label $p 'Rapport' 24 220 240 22 10 $true);$script:exportType=New-Combo $p 24 250 610
Set-Choices $exportType @("Liste dʼenregistrement",'Apprenants ayant soldé le mois','Apprenants ayant versé dans le mois','Litiges et retards','Rapport de la rentrée')
$exportPDF=New-Button $p 'Exporter en PDF' 24 320 285 $true;$exportPDF.Tag=$true
$exportExcel=New-Button $p 'Exporter en Excel' 330 320 304;$exportExcel.Tag=$false
[void](New-Label $p 'Contrôle avant export' 24 386 840 28 13 $true)
$script:reportRule=New-Label $p 'Choisissez le rapport à produire.' 24 426 840 78 11
$script:exportType.Add_SelectedIndexChanged({
    $script:exportMonth.Enabled=($script:exportType.SelectedIndex -notin @(0,4))
    $script:exportPromo.Enabled=($script:exportType.SelectedIndex -ne 4)
    if($script:exportExcel){$script:exportExcel.Text=$(if($script:exportType.SelectedIndex -eq 4){'Exporter en Word'}else{'Exporter en Excel'})}
    if($script:currentPage -eq 'Exports' -and $script:ribbonFlow){$script:ribbonPage='';Update-ITMRibbonState}
    $script:reportRule.Text=switch($script:exportType.SelectedIndex){
        0{'Enregistrement : dossiers valides de l''année scolaire, triés par identité. Aucun filtre mensuel.'}
        1{'Mois soldé : cumul affecté au mois au moins égal à la mensualité. Un dossier par apprenant.'}
        2{'Versements : cumul positif affecté au mois, y compris les paiements partiels. Cette synthèse ne remplace pas le registre des reçus.'}
        4{'Rapport complet de rentrée : institut, année scolaire, effectifs par sexe, listes nominatives et canevas du modèle. Tous les inscrits de lʼannée ; rubriques complémentaires modifiables.'}
        3{'Retards : uniquement les mois impayés devenus exigibles le 6 du mois suivant, jusqu''au mois choisi. Les mois soldés sont exclus.'}
    }
})
$exportMonth.Enabled=($exportType.SelectedIndex -ne 0);$reportRule.Text='Enregistrement : dossiers valides de l''année scolaire, triés par identité. Aucun filtre mensuel.'
$openReport=New-Button $p 'Ouvrir le dernier rapport' 24 528 390
$openReport.Add_Click({try{if(-not $script:busy){if(-not $script:lastReport -or -not (Test-Path -LiteralPath $script:lastReport)){throw "Générez un rapport avant de l'ouvrir."};Start-Process -FilePath $script:lastReport}}catch{App-Error $_}})
$exportAction={
    param($sender,$e)
    if($script:busy){return}
    if($script:exportType.SelectedIndex -eq 4){try{Show-ScoliaRentreeDialog ([bool]$sender.Tag)}catch{App-Error $_};return}
    $script:exportPDFFlag=[bool]$sender.Tag
    Do-Work {
        Assert-ITMPermission 'Exporter'
        $m=[string]$script:exportMonth.Text;$promo=[string]$script:exportPromo.Text;if($promo -eq 'Toutes les promotions'){$promo=''}
        $mode=switch($script:exportType.SelectedIndex){0{3};1{1};2{2};3{4};default{throw 'Choisissez un rapport.'}}
        if($mode -ne 3 -and $m -eq 'Tous les mois'){throw 'Choisissez un mois précis pour ce rapport.'}
        if($mode -eq 3){$m=''}
        $preview=Invoke-ITMJson 'ITMAppVerifierRapport' @($m,$promo,[int]$mode)
        if([int]$preview.rows -eq 0 -and [int]$preview.issues -eq 0){throw 'Aucun dossier ne correspond aux filtres. Modifiez le mois, la promotion ou le type de rapport.'}
        if(-not (Confirm-ITMReportPreview $preview ([string]$script:exportType.Text) $m $promo)){$script:status.Text='Rapport annulé. Ajustez les filtres si nécessaire.';return}
        $saveDialog=New-Object Windows.Forms.SaveFileDialog
        try {
            $saveDialog.Title='Enregistrer le rapport ITM CD'
            $exportFolder=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'exports';[void][IO.Directory]::CreateDirectory($exportFolder);$saveDialog.InitialDirectory=$exportFolder
            $saveDialog.Filter=$(if($script:exportPDFFlag){'Document PDF (*.pdf)|*.pdf'}else{'Classeur Excel (*.xlsx)|*.xlsx'})
            $saveDialog.DefaultExt=$(if($script:exportPDFFlag){'pdf'}else{'xlsx'})
            $saveDialog.AddExtension=$true;$saveDialog.OverwritePrompt=$true;$saveDialog.CheckPathExists=$true
            $reportName=@('Enregistrement','Mois_solde','Versements','Retards')[$script:exportType.SelectedIndex]
            $saveDialog.FileName='ITM_'+$reportName+'_'+$(if($promo){$promo}else{'Toutes_promotions'})+'_'+$(if($m){$m+'_'}else{''})+(Get-Date -Format 'yyyyMMdd_HHmmss')
            if($saveDialog.ShowDialog($script:main) -ne 'OK'){$script:status.Text='Export annulé. Aucun fichier créé.';return}
            $path=$saveDialog.FileName
        } finally {$saveDialog.Dispose()}
        $temporaryReport=''
        try {
            $enginePath=$path
            if($script:exportPDFFlag){
                $temporaryReport=Join-Path ([IO.Path]::GetTempPath()) ('SCOLIA_rapport_'+[guid]::NewGuid().ToString('N')+'.xlsx')
                $enginePath=$temporaryReport
            }
            # The existing engine controls the selection, sums and diagnostic entries.
            $result=$script:excel.Run((Api-Name 'ITMAppExporter'),$m,$promo,$false,[int]$mode,[string]$enginePath)
            $reply=([string]$result | ConvertFrom-Json);if(-not $reply.ok){throw [string]$reply.error}
            if($script:exportPDFFlag){
                Set-ITMActivityMessage 'Mise en page du document PDF…'
                $institution=Get-ITMInstitutionName
                [ITMExamCards]::ExportWorkbookPdf($temporaryReport,$path,(Join-Path $PSScriptRoot 'Assets'),$institution,[int]$mode,(Get-ITMInstitutionLogo))
                $reply.path=$path
            }
        } finally {if($temporaryReport -and [IO.File]::Exists($temporaryReport)){[IO.File]::Delete($temporaryReport)}}
        Write-ITMUserAudit 'Rapport exporté';$script:lastReport=[string]$reply.path;$script:status.Text='Rapport enregistré : '+$reply.path
        [void][ITMMessageBox]::Show($script:main,[string]$reply.rows+' dossier(s) et '+$reply.groups+' promotion(s) exportés.'+"
"+$reply.path+"
Observations : "+$reply.issues,'ITM CD','OK','Information')
    } 'Préparation du rapport...'
}
$exportPDF.Add_Click($exportAction);$exportExcel.Add_Click($exportAction)

$p=$pages['Diagnostic']
[void](New-Label $p 'Données à vérifier' 24 20 900 38 20 $true)
$script:issuesLabel=New-Label $p 'Les observations apparaîtront après connexion.' 24 73 920 65 11
$script:compatCheck=New-Button $p 'Compatibilité et sauvegardes' 24 134 370
$script:compatExport=New-Button $p 'Exporter le diagnostic' 414 134 350
$script:compatCheck.Add_Click({try{Show-ITMCompatibility}catch{App-Error $_}});$script:compatExport.Add_Click({try{Export-ITMCompatibility}catch{App-Error $_}})
$script:issuesGrid=New-Grid $p 24 192 946 442
$p=$pages['Administration']
$script:adminTabs=New-Object ITMModernTabs;$adminTabs.Dock='Fill';$adminTabs.Size=$p.ClientSize;$adminTabs.Font=New-Object Drawing.Font('Segoe UI',10);$adminTabs.Multiline=$true;$adminTabs.Padding=New-Object Drawing.Point(16,10);$p.Controls.Add($adminTabs)
$script:adminPages=@{}
foreach($label in @('Personnalisation','Promotions','Classeurs et données','Sauvegardes','Année scolaire','Journal','Doublons','Raccourcis')){
    $page=New-Object Windows.Forms.TabPage;$page.Text=$label;$page.Size=$p.ClientSize;$page.BackColor=[Drawing.Color]::White;$page.AutoScroll=$true
    [void]$adminTabs.TabPages.Add($page);$adminPages[$label]=$page
}
Build-ITMInstitutionPage $adminPages['Personnalisation']
$page=$adminPages['Promotions']
[void](New-Label $page 'Promotions et frais scolaires' 24 20 900 38 18 $true)
$addPromo=New-Button $page 'Nouvelle promotion' 24 75 270 $true;$editPromo=New-Button $page 'Modifier la promotion' 310 75 270
$script:promotionsGrid=New-Grid $page 24 140 946 400
$addPromo.Add_Click({try{if(-not $script:busy){Show-Promotion}}catch{App-Error $_}})
$editPromo.Add_Click({try{if(-not $script:busy){if($script:promotionsGrid.SelectedRows.Count -ne 1){throw 'Sélectionnez une promotion.'};Show-Promotion ([string]$script:promotionsGrid.SelectedRows[0].Cells['Code'].Value)}}catch{App-Error $_}})
Build-ITMStoragePage $adminPages['Classeurs et données']
$page=$adminPages['Sauvegardes']
[void](New-Label $page 'Sauvegarder et récupérer une copie' 24 20 946 38 18 $true)
[void](New-Label $page 'En mode data, la sauvegarde couvre tous les classeurs. La restauration crée un nouveau dossier et conserve les données actuelles.' 24 70 946 45 11)
$backupNow=New-Button $page 'Créer une sauvegarde' 24 125 280 $true;$restore=New-Button $page 'Restaurer la copie sélectionnée' 320 125 380
$script:autoBackupOptions=New-Button $page 'Sauvegardes automatiques' 24 174 390
$script:autoBackupOptions.Add_Click({try{Show-ITMAutoBackupOptions}catch{App-Error $_}})
$script:checkBackup=New-Button $page 'Vérifier la sauvegarde' 434 174 320
$script:checkBackup.Add_Click({Do-Work {Assert-ITMPermission 'Sauvegarder';if(-not $script:store -or $script:backupGrid.SelectedRows.Count -ne 1){throw 'Sélectionnez une sauvegarde complète du dossier data.'};$selected=[string]$script:backupGrid.SelectedRows[0].Cells['Chemin'].Value;$allowed=[IO.Path]::GetFullPath((Join-Path $script:storeFolder 'sauvegardes'))+[IO.Path]::DirectorySeparatorChar;if(-not [IO.Path]::GetFullPath($selected).StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Sauvegarde extérieure au dossier data.'};[void](Test-ITMBackupIntegrity $selected);$script:status.Text='Sauvegarde vérifiée : tous les fichiers correspondent à leurs empreintes.'} 'Vérification de la sauvegarde...'})
$script:backupGrid=New-Grid $page 24 222 946 333
$backupNow.Add_Click({Do-Work {$reply=Invoke-ITMJson 'ITMAppSauvegarde';Write-ITMUserAudit 'Sauvegarde manuelle';Refresh-Administration;$script:status.Text='Sauvegarde créée : '+$reply.path} 'Création de la sauvegarde...'})
$restore.Add_Click({
    if($script:busy){return}
    Do-Work {
        Assert-ITMPermission 'Administrer'
        if($script:backupGrid.SelectedRows.Count -ne 1){throw 'Sélectionnez une sauvegarde.'}
        $source=[string]$script:backupGrid.SelectedRows[0].Cells['Chemin'].Value
        if($script:store){$restored=Restore-ITMStore $source;Write-ITMUserAudit 'Restauration de sauvegarde';Connect-Book $restored;$script:status.Text='Sauvegarde complète restaurée dans un nouveau dossier.';return}
        $d=New-Object Windows.Forms.SaveFileDialog;$d.Filter='Classeur Excel (*.xlsm)|*.xlsm';$d.FileName='ITM_RESTAURE.xlsm'
        try{
            if($d.ShowDialog($script:main) -ne 'OK'){return}
            $reply=Invoke-ITMJson 'ITMAppRestaurer' @($source,$d.FileName);Write-ITMUserAudit 'Restauration de sauvegarde'
            if([ITMMessageBox]::Show($script:main,'Copie restaurée : '+$reply.path+"
Connecter cette copie ?",'Restauration','YesNo','Information') -eq 'Yes'){Connect-Book $reply.path}
        }finally{$d.Dispose()}
    } 'Restauration vers un nouveau fichier...'
})
$page=$adminPages['Année scolaire']
[void](New-Label $page 'Clôture et préparation de la prochaine année' 24 20 946 40 18 $true)
$script:yearState=New-Label $page 'Connectez un classeur pour consulter son année.' 24 80 946 70 12
[void](New-Label $page 'Nouvelle année (exemple : 2027-2028)' 24 180 700 26 10 $true)
$script:nextYear=New-Object Windows.Forms.TextBox;$nextYear.SetBounds(24,215,350,32);$page.Controls.Add($nextYear)
[void](New-Label $page 'Les dossiers et leurs statuts sont reconduits. Les paiements sont remis à zéro uniquement dans le NOUVEAU classeur. Les dettes précédentes restent dans les archives.' 24 280 946 80 12)
$closeYear=New-Button $page 'Archiver et préparer la nouvelle année' 24 400 600 $true
$closeYear.Add_Click({
    if($script:busy){return}
    Do-Work {
        if([ITMMessageBox]::Show($script:main,'Clôturer ce classeur, conserver une archive et préparer '+$script:nextYear.Text+' ?','Clôture annuelle','YesNo','Warning') -ne 'Yes'){return}
        $d=New-Object Windows.Forms.SaveFileDialog;$d.Filter='Classeur Excel (*.xlsm)|*.xlsm';$d.FileName='ITM_'+$script:nextYear.Text+'.xlsm'
        try{
            if($d.ShowDialog($script:main) -ne 'OK'){return}
            $reply=Invoke-ITMJson 'ITMAppCloture' @($script:nextYear.Text,$d.FileName)
            $script:committedThisAction=$true;Write-ITMUserAudit 'Clôture annuelle'
            [void][ITMMessageBox]::Show($script:main,'Année clôturée. Archive : '+$reply.archive+"
Nouveau classeur : "+$reply.path,'ITM CD','OK','Information')
            Connect-Book $reply.path
        }finally{$d.Dispose()}
    } 'Archivage et préparation de la nouvelle année...'
})
$page=$adminPages['Journal']
[void](New-Label $page 'Historique des opérations' 24 20 946 38 18 $true)
$exportJournal=New-Button $page 'Exporter le journal' 24 75 350
$script:namedJournal=New-Button $page 'Journal nominatif' 394 75 350
$script:namedJournal.Add_Click({try{Show-ITMNamedJournal}catch{App-Error $_}})
$script:journalGrid=New-Grid $page 24 135 946 410
$exportJournal.Add_Click({if(-not $script:busy){Do-Work {Refresh-Administration;Save-ITMCsv $script:admin.journal 'Journal_ITM.csv'} 'Export du journal actualisé...'}})
$journalGrid.Add_CellDoubleClick({param($sender,$e)
    try{if($e.RowIndex -ge 0 -and -not $script:busy){
        $id=[int]$sender.Rows[$e.RowIndex].Cells['ID journal'].Value
        $r=@($script:admin.journal | Where-Object {$_.Row -eq $id})[0]
        Show-TextDetail 'Détail de la modification' ('Date : '+$r.Date+"
Utilisateur : "+$r.Utilisateur+"
Motif : "+$r.Motif+"

AVANT
"+(Format-ITMAudit $r.Avant $r.Action)+"

APRÈS
"+(Format-ITMAudit $r.Apres $r.Action))
    }}catch{App-Error $_}
})
$page=$adminPages['Doublons']
[void](New-Label $page 'Dossiers similaires à vérifier' 24 20 946 38 18 $true)
[void](New-Label $page 'Même nom, post-nom, prénom et promotion. Les dossiers ne sont jamais fusionnés automatiquement.' 24 78 946 55 11)
$script:duplicatesGrid=New-Grid $page 24 155 946 350
$mergeDuplicates=New-Button $page 'Fusionner les dossiers sélectionnés' 24 520 320 $true
$mergeDuplicates.Add_Click({try{Show-ITMDuplicateMerge}catch{App-Error $_}})
[void](New-Label $page 'La fusion exige une identité et une promotion identiques. Les paiements sont rattachés au dossier conservé et la décision est journalisée.' 360 526 610 40 9)
$page=$adminPages['Raccourcis']
[void](New-Label $page 'Accès rapide à ITM CD' 24 20 946 38 18 $true)
[void](New-Label $page 'Ajoutez le logo de l''école sur votre Bureau et dans le menu Démarrer.' 24 78 946 52 11)
$createShortcut=New-Button $page 'Créer un raccourci sur le Bureau' 24 152 480 $true
$createShortcut.Add_Click({try{Create-ITMDesktopShortcut}catch{App-Error $_}})
[void](New-Label $page 'Barre des tâches : clic droit sur le raccourci ITM CD, puis Épingler à la barre des tâches. Selon Windows, cherchez cette option dans Afficher plus d''options, ou depuis le menu Démarrer.' 24 220 946 96 11)
[void](New-Label $page 'Si le dossier du programme est déplacé, ouvrez la nouvelle copie et recréez les raccourcis. Aucun classeur n''est modifié par cette opération.' 24 336 946 70 11)
Build-ITMSecurityPage $pages['Sécurité']
Build-ITMDisplayPreferences $pages['Préférences']
Build-ITMDataPage $pages['Données']
foreach($name in @('Paiements','Diagnostic','Sécurité','Données')){Enable-ITMPageDesign $pages[$name]}
Enable-ITMPageDesign $script:exportReportsPage
foreach($page in $adminPages.Values){Enable-ITMPageDesign $page}
$adminTabs.Font=New-Object Drawing.Font('Segoe UI',10,[Drawing.FontStyle]::Bold)
$adminTabs.Padding=New-Object Drawing.Point(20,12)
Build-ITMSchoolPage $script:exportCardsPage
Build-ScoliaSchoolOverview $script:pages['Scolarité']
Enable-ITMResponsivePanel $script:exportReportsPage 1000
Enable-ITMResponsivePanel $script:exportCardsPage 1000
$main.Add_FormClosing({param($sender,$e)if($script:busy){$e.Cancel=$true}})
foreach($panel in $script:pages.Values){if($panel -ne $script:pages['Administration'] -and $panel -ne $script:pages['Préférences'] -and $panel -ne $script:pages['Exports'] -and $panel -ne $script:pages['Scolarité']){Enable-ITMResponsivePanel $panel 1000}}
foreach($panel in $adminPages.Values){Enable-ITMResponsivePanel $panel 1000}
# The final resize pass is installed after all controls are built.
$main.Add_Shown({Update-ITMNavigation;foreach($panel in $script:pages.Values){Update-ITMResponsivePanel $panel}})
$script:new=$new
$script:edit=$edit
$script:fiche=$fiche
$script:recent=$recent
$script:payFromList=$payFromList
$script:selectPay=$selectPay
$script:allPayments=$allPayments
$script:correct=$correct
$script:clearPay=$clearPay
$script:exportPDF=$exportPDF
$script:exportExcel=$exportExcel
$script:openReport=$openReport
$script:adminTabs=$adminTabs
$script:adminPages=$adminPages
$script:backupNow=$backupNow
$script:restore=$restore
$script:addPromo=$addPromo;$script:editPromo=$editPromo;$script:closeYear=$closeYear;$script:exportJournal=$exportJournal
$script:createShortcut=$createShortcut
Build-ScoliaWorkspace
Build-ITMHealthCommands $script:pages['Diagnostic']
Initialize-ITMRibbon
Initialize-ITMFrequentCommands
Enable-ITMFluidTypography $script:main
. (Join-Path $PSScriptRoot 'DispositionUnifiee.ps1')
Enable-ITMWorkspaceLayout
Enable-ITMMainShortcuts
Initialize-ScoliaFeatureCommands
[ITMVisualTheme]::Apply($script:main)
Apply-ITMDisplayPreferences
Start-ITMThemeMonitor
Start-ITMDailyBackupMonitor
Update-ITMNavigation
$script:writeActions=@($new,$edit,$correct,$addPromo,$editPromo,$closeYear,$script:storageNext,$script:storageSave,$script:deleteLearner,$script:batchPayments,$script:batchLateButton)
Update-WorkspaceState
Show-Page 'Tableau de bord'
$script:rememberedPath=''
Update-WorkspaceState
$main.Add_Shown({
    if($script:scoliaSession.Role -eq 'Comptabilité'){return}
    Do-Work {
        $script:rememberedPath=Read-ConnectionChoice
        $script:connectPath=$(if($Classeur){(Get-ITMWorkbookLocation -Explicit $Classeur).Path}else{$script:rememberedPath})
        if(-not $script:connectPath){if($script:detectedLocation){$script:fileLabel.Text=$script:detectedLocation.Reason};return}
        if(-not (Test-Path -LiteralPath $script:connectPath -PathType Leaf) -and -not [IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName($script:connectPath)) 'registre.json')) -and -not [IO.File]::Exists((Join-Path ([IO.Path]::GetDirectoryName($script:connectPath)) 'transaction.json'))){
            $script:fileLabel.Text='Classeur mémorisé indisponible'
            throw 'Le classeur mémorisé est introuvable. Rebranchez son support ou choisissez son nouvel emplacement depuis Données. Votre choix mémorisé est conservé.'
        }
        Connect-Book $script:connectPath
    } 'Recherche et reconnexion au classeur mémorisé...'
})
Apply-ITMInstitution
Update-ScoliaTransition 'Finalisation de la page d accueil...';$main.Add_Shown({Close-ScoliaTransition})
try{$script:securityTimer.Start();Start-ScoliaMonitor;Start-ScoliaNetworkHost;[void]$main.ShowDialog()}finally{Close-ScoliaTransition;Stop-ScoliaNetworkHost;if($script:scoliaTimer){$script:scoliaTimer.Stop();$script:scoliaTimer.Dispose()};if($script:searchTimer){$script:searchTimer.Stop();$script:searchTimer.Dispose()};if($script:activity){$script:activity.Dispose()};if($script:securityTimer){$script:securityTimer.Stop();$script:securityTimer.Dispose()};Disconnect-Book;if($script:inputHelp){$script:inputHelp.Dispose()};Reset-ScoliaInlineViews;Clear-ScoliaRibbonViews;Save-ITMCommandCounts;Save-ScoliaPerformanceSamples;$main.Dispose();if($script:schoolLogo){$script:schoolLogo.Dispose()};if($script:schoolIcon){$script:schoolIcon.Dispose()};foreach($icon in $script:navIcons.Values){$icon.Dispose()};foreach($font in $script:adaptiveFonts.Values){$font.Dispose()}}
