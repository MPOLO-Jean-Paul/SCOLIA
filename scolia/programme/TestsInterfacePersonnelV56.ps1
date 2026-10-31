# Contrôles WinForms réels avec stockage simulé ; aucun Excel ni fichier métier.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
foreach($name in @('ActivityIndicator.cs','ChromeModerne.cs')){Add-Type -Path (Join-Path $PSScriptRoot $name) -ReferencedAssemblies System.Windows.Forms,System.Drawing}
foreach($pair in @(@('Application.ps1',@('New-Label','New-Button','New-Grid')),@('ScoliaConfiguration.ps1',@('New-ScoliaDialog','New-ScoliaTable','Add-ScoliaField','Add-ScoliaText')))){
    $tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $pair[0]),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$pair[0])}
    foreach($name in $pair[1]){$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name},$true);if(-not $node){throw ('Fonction absente : '+$name)};. ([scriptblock]::Create($node.Extent.Text))}
}
foreach($name in @('ThemesAffichage.ps1','FonctionnalitesV55.ps1','PresencesV55.ps1','PersonnelV56.ps1','SalairesV56.ps1','InterfacePersonnelV56.ps1')){. (Join-Path $PSScriptRoot $name)}
function Set-ITMWindowIcon($form){}
function Set-ITMGridReadability($grid){}
function Test-ITMPermission($name){return $name -notin @($script:deniedPermissions)}
function Assert-ITMPermission($name){if(-not (Test-ITMPermission $name)){throw ('Permission refusée : '+$name)}}
function Get-ScoliaFeatureContext {return $script:testContext}
function Get-ScoliaFeatureActor {return 'utilisateur-test'}
function Read-ScoliaFeatureStore([string]$Name){if($Name -eq 'personnel'){return $script:testPersonnel};if($Name -eq 'salaires'){return $script:testSalaries};throw 'Stockage inattendu.'}
function Invoke-ScoliaFeatureUpdate([string]$Name,[scriptblock]$Change){$document=Read-ScoliaFeatureStore $Name;[void](& $Change $document);$document.Revision++;$script:testWrites++;return $document}
function Assert-InterfacePersonnelV56([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
$now=[datetimeoffset]::Now;$today=$now.Date;$firstAcademicYear=$(if($today.Month -lt 9){$today.Year-1}else{$today.Year})
$script:testContext=[pscustomobject]@{Id='test-personnel';Year=($firstAcademicYear.ToString()+'-'+($firstAcademicYear+1));LocalKey='test-session'}
$script:displayPreferences=[pscustomobject]@{Theme='Clair'};$script:data=[pscustomobject]@{closed=$false};$script:scolia=$null;$script:deniedPermissions=@();$script:testWrites=0
$teacher=New-ScoliaTeacher -Nom 'Enseignant de test' -ActiveFrom $today.ToString('yyyy-MM-dd') -Now $now -Actor 'test'
$script:testPersonnel=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=0;Items=@($teacher)}
$calendar=New-ScoliaMonthlyCalendar -Month $today.ToString('yyyy-MM') -Year $script:testContext.Year -ContextId $script:testContext.Id -WorkDates @($today.ToString('yyyy-MM-dd')) -Actor 'test'
$calendar.Revision=1
$script:testSalaries=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=1;Items=@($calendar)}
$attendance=New-ScoliaTeacherAttendanceDialog -Context $script:testContext
$payroll=New-ScoliaMonthlyPayrollDialog -Context $script:testContext -Month $today
try{
    foreach($state in @($attendance,$payroll)){
        $state.Form.Show();[Windows.Forms.Application]::DoEvents()
        foreach($theme in @('Clair','Sombre')){
            $script:displayPreferences.Theme=$theme;Apply-ITMThemeForm $state.Form
            foreach($width in @(1200,600)){
                $state.Form.Size=New-Object Drawing.Size($width,540);[Windows.Forms.Application]::DoEvents()
                Assert-InterfacePersonnelV56 (-not $state.Header.Bounds.IntersectsWith($state.Toolbar.Bounds)) 'Les actions recouvrent le titre.'
                Assert-InterfacePersonnelV56 (-not $state.Toolbar.Bounds.IntersectsWith($state.Body.Bounds)) 'La barre des actions recouvre la liste.'
                Assert-InterfacePersonnelV56 (-not $state.Message.Bounds.IntersectsWith($state.Body.Bounds)) 'Le retour utilisateur recouvre les lignes.'
                Assert-InterfacePersonnelV56 ($state.Grid.Height -gt 75) 'La grille est inaccessible à petite taille.'
            }
        }
    }
    Assert-InterfacePersonnelV56 ($attendance.ClockTimer -is [Windows.Forms.Timer] -and $attendance.ClockTimer.Enabled -and $attendance.ClockTimer.Interval -le 30000) 'Le pointage ne surveille pas automatiquement le passage de 08:00.'
    Assert-InterfacePersonnelV56 (Update-ScoliaTeacherAttendanceDialog $attendance) 'Le pointage ne charge pas.'
    Assert-InterfacePersonnelV56 ($attendance.Grid.Rows.Count -eq 1) 'Enseignant actif manquant.'
    Assert-InterfacePersonnelV56 ($payroll.PolicyPicker.Checked -and -not $payroll.PolicyPicker.Enabled -and -not $payroll.PolicyPicker.Visible) 'La règle des absences justifiées reste modifiable.'
    Assert-InterfacePersonnelV56 ($payroll.PolicyNotice.Text -eq 'Absences justifiées : comptées, sans retenue') 'La règle de paiement n est pas expliquée.'
    Assert-InterfacePersonnelV56 ($payroll.Grid.Columns['Late'] -and $payroll.Grid.Columns['LateAbsences']) 'Les retards et absences équivalentes manquent du bilan.'
    Assert-InterfacePersonnelV56 ($attendance.Grid.Columns['Entry'] -is [Windows.Forms.DataGridViewCheckBoxColumn]) 'L entrée doit être une case à cocher.'
    Assert-InterfacePersonnelV56 ($attendance.Grid.Columns['Exit'] -is [Windows.Forms.DataGridViewCheckBoxColumn]) 'La sortie doit être une case à cocher.'
    Invoke-ScoliaTeacherPunchClick -State $attendance -RowIndex 0 -Direction 'Exit'
    Assert-InterfacePersonnelV56 ($script:testWrites -eq 0) 'Une sortie a été enregistrée sans entrée.'
    Invoke-ScoliaTeacherPunchClick -State $attendance -RowIndex 0 -Direction 'Entry'
    Assert-InterfacePersonnelV56 ($script:testWrites -eq 1) 'L entrée n est pas enregistrée immédiatement.'
    $punch=@($script:testPersonnel.Items|Where-Object {$_.Kind -eq 'Punch'})[0]
    Assert-InterfacePersonnelV56 ([bool]$punch.EntryAt -and [datetimeoffset]::Parse($punch.EntryAt).Date -eq $today) 'L heure automatique ou le jour enregistré est invalide.'
    Invoke-ScoliaTeacherPunchClick -State $attendance -RowIndex 0 -Direction 'Entry'
    Assert-InterfacePersonnelV56 ($script:testWrites -eq 1) 'Une signature existante a été modifiée.'
    Invoke-ScoliaTeacherPunchClick -State $attendance -RowIndex 0 -Direction 'Exit'
    Assert-InterfacePersonnelV56 ($script:testWrites -eq 2) 'La sortie n est pas enregistrée immédiatement.'
    Assert-InterfacePersonnelV56 ([bool]$attendance.Grid.Rows[0].Cells['Entry'].Value -and [bool]$attendance.Grid.Rows[0].Cells['Exit'].Value) 'Les deux cases ne reflètent pas les signatures persistées.'
    if(([datetimeoffset]::Now).TimeOfDay -ge [timespan]::FromHours(8)){
        Assert-InterfacePersonnelV56 ($attendance.Grid.Rows[0].Tag.Status.LateRecorded -and $attendance.Grid.Rows[0].Cells['Status'].Value -match 'retard') 'Une entrée après 08:00 ne signale pas le retard, malgré la sortie toujours accessible.'
    }
    $attendance.LoadedDate=$today.AddDays(-1)
    Invoke-ScoliaTeacherPunchClick -State $attendance -RowIndex 0 -Direction 'Entry'
    Assert-InterfacePersonnelV56 ($script:testWrites -eq 2) 'Une journée passée a été signée.'
    Assert-InterfacePersonnelV56 (Update-ScoliaMonthlyPayrollDialog $payroll) 'Le bilan doit se charger avant la saisie du salaire.'
    Assert-InterfacePersonnelV56 ($payroll.Grid.Rows.Count -eq 1 -and $payroll.PreviewRows[0].DailyRows.Count -eq [datetime]::DaysInMonth($today.Year,$today.Month)) 'Le détail doit couvrir tous les jours du mois.'
    Assert-InterfacePersonnelV56 (-not $payroll.FinalizeButton.Enabled) 'Un mois sans salaire doit rester non finalisable.'
    $payroll.AmountPicker.Value=300000
    Save-ScoliaTeacherMonthlySalary $payroll
    Assert-InterfacePersonnelV56 (@($script:testSalaries.Items|Where-Object {$_.Kind -eq 'Salary'}).Count -eq 1) 'Le salaire mensuel FC ne se sauvegarde pas.'
    Assert-InterfacePersonnelV56 ($payroll.PreviewRows[0].GrossFC -eq 300000) 'Le bilan ne reprend pas le montant saisi.'
    # Parcours réel des interfaces : trois retards traversant une fin de mois et leur justification.
    $savedContext=$script:testContext;$savedPersonnel=$script:testPersonnel;$savedSalaries=$script:testSalaries;$savedWrites=$script:testWrites
    $fixturePayroll=$null;$fixtureAttendance=$null
    try{
        $fixtureYear=($today.Year-2).ToString()+'-'+($today.Year-1).ToString();$fixtureBounds=Get-ScoliaAttendanceYearBounds $fixtureYear
        $fixtureFirst=$fixtureBounds.Start.AddMonths(1)
        while($fixtureFirst.DayOfWeek -notin @([DayOfWeek]::Wednesday,[DayOfWeek]::Thursday,[DayOfWeek]::Friday,[DayOfWeek]::Saturday)){$fixtureFirst=$fixtureFirst.AddMonths(1)}
        $fixtureDates=@($fixtureFirst.AddDays(-2),$fixtureFirst.AddDays(-1),$fixtureFirst)
        $script:testContext=[pscustomobject]@{Id='test-retards-interface';Year=$fixtureYear;LocalKey='test-retards-session'}
        $fixtureTeacher=New-ScoliaTeacher -Nom 'Enseignant retards' -ActiveFrom $fixtureDates[0].ToString('yyyy-MM-dd') -Now $now -Actor 'test'
        $script:testPersonnel=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=0;Items=@($fixtureTeacher)}
        foreach($day in $fixtureDates){
            $entryTime=New-Object datetimeoffset($day.AddHours(8).AddMinutes(1),$now.Offset);$exitTime=$entryTime.AddHours(8)
            Set-ScoliaTeacherPunch -Document $script:testPersonnel -TeacherId $fixtureTeacher.Id -Direction 'Entry' -Now $entryTime -Actor 'test' -ExpectedRevision $script:testPersonnel.Revision;$script:testPersonnel.Revision++
            Set-ScoliaTeacherPunch -Document $script:testPersonnel -TeacherId $fixtureTeacher.Id -Direction 'Exit' -Now $exitTime -Actor 'test' -ExpectedRevision $script:testPersonnel.Revision;$script:testPersonnel.Revision++
        }
        $previousCalendar=New-ScoliaMonthlyCalendar -Month $fixtureFirst.AddMonths(-1).ToString('yyyy-MM') -Year $fixtureYear -ContextId $script:testContext.Id -WorkDates @($fixtureDates[0].ToString('yyyy-MM-dd'),$fixtureDates[1].ToString('yyyy-MM-dd')) -Actor 'test'
        $currentCalendar=New-ScoliaMonthlyCalendar -Month $fixtureFirst.ToString('yyyy-MM') -Year $fixtureYear -ContextId $script:testContext.Id -WorkDates @($fixtureFirst.ToString('yyyy-MM-dd')) -Actor 'test'
        $fixtureSalary=New-ScoliaMonthlySalary -Month $fixtureFirst.ToString('yyyy-MM') -Year $fixtureYear -ContextId $script:testContext.Id -TeacherId $fixtureTeacher.Id -GrossFC 300000 -Actor 'test'
        $script:testSalaries=[pscustomobject]@{Schema=1;Context=$script:testContext;Revision=0;Items=@($previousCalendar,$currentCalendar,$fixtureSalary)}
        $fixturePayroll=New-ScoliaMonthlyPayrollDialog -Context $script:testContext -Month $fixtureFirst
        $fixtureAttendance=New-ScoliaTeacherAttendanceDialog -Context $script:testContext;$fixtureAttendance.DatePicker.Value=$fixtureFirst
        Assert-InterfacePersonnelV56 (Update-ScoliaMonthlyPayrollDialog $fixturePayroll) 'Le bilan des retards traverse mal la limite de mois.'
        $monthlyRow=$fixturePayroll.PreviewRows[0]
        Assert-InterfacePersonnelV56 ($monthlyRow.LateDays -eq 1 -and $monthlyRow.LateAbsenceDays -eq 1 -and $monthlyRow.PresentDays -eq 1) 'Le bilan doit conserver la double signature et compter une absence équivalente pour les trois retards de la même semaine.'
        Assert-InterfacePersonnelV56 ($monthlyRow.LateGroups[0].Dates.Count -eq 3 -and $monthlyRow.NetFC -eq 0) 'Les retards du mois précédent ne sont pas repris dans le groupe ou la retenue est incorrecte.'
        Assert-InterfacePersonnelV56 (Update-ScoliaTeacherAttendanceDialog $fixtureAttendance) 'La journée du troisième retard ne charge pas.'
        Assert-InterfacePersonnelV56 ($fixtureAttendance.Grid.Rows[0].Cells['Status'].Value -match 'absence équivalente') 'La troisième entrée tardive ne montre pas son absence équivalente dans le pointage journalier.'
        Set-ScoliaTeacherJustification -Document $script:testPersonnel -TeacherId $fixtureTeacher.Id -Date $fixtureFirst -Reason 'Situation exceptionnelle validée' -Now $now -Actor 'test' -ExpectedRevision $script:testPersonnel.Revision;$script:testPersonnel.Revision++
        Assert-InterfacePersonnelV56 (Update-ScoliaMonthlyPayrollDialog $fixturePayroll) 'La justification du troisième retard ne se recharge pas.'
        $monthlyRow=$fixturePayroll.PreviewRows[0]
        Assert-InterfacePersonnelV56 ($monthlyRow.LateAbsenceDays -eq 1 -and $monthlyRow.LateJustifiedAbsenceDays -eq 1 -and $monthlyRow.NetFC -eq 300000) 'L absence équivalente justifiée doit rester comptée sans retenue.'
        $converted=@($monthlyRow.DailyRows|Where-Object {$_.LatePenalty})
        Assert-InterfacePersonnelV56 ($converted.Count -eq 1 -and $converted[0].LatePenaltyJustified -and $converted[0].LatePenaltyReason -eq 'Situation exceptionnelle validée' -and $converted[0].ConvertedDates.Count -eq 3) 'Le motif et les dates de la conversion ne sont pas accessibles dans le détail.'
    }finally{
        if($fixturePayroll){$fixturePayroll.Form.Dispose()};if($fixtureAttendance){$fixtureAttendance.Form.Dispose()}
        $script:testContext=$savedContext;$script:testPersonnel=$savedPersonnel;$script:testSalaries=$savedSalaries;$script:testWrites=$savedWrites
    }
    $script:data.closed=$true
    Assert-InterfacePersonnelV56 (Update-ScoliaMonthlyPayrollDialog $payroll) 'Une année clôturée doit rester consultable.'
    Assert-InterfacePersonnelV56 (-not $payroll.SaveSalaryButton.Enabled -and -not $payroll.CalendarButton.Enabled -and -not $payroll.FinalizeButton.Enabled) 'Une année clôturée autorise encore des modifications.'
    $script:deniedPermissions=@('Administrer')
    Assert-InterfacePersonnelV56 (-not (Update-ScoliaMonthlyPayrollDialog $payroll)) 'Un rôle sans administration peut consulter les salaires.'
    Assert-InterfacePersonnelV56 ($payroll.Grid.Rows.Count -eq 0 -and $payroll.TeacherPicker.Items.Count -eq 0 -and $payroll.AmountPicker.Value -eq 0) 'Des salaires restent visibles après refus de permission.'
    Write-Output 'PASS : signatures accessibles après 08:00, règle justifiée fixe, retards intermensuels et absences équivalentes avec motif sans retenue, calendrier complet, thèmes, taille réduite, consultation clôturée et confidentialité.'
}finally{$attendance.Form.Dispose();$payroll.Form.Dispose()}
Assert-InterfacePersonnelV56 ($attendance.ClockTimerDisposed -and -not $attendance.ClockTimer.Enabled) 'Le timer du pointage reste actif après la fermeture.'
