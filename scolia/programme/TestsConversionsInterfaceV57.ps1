# Régression de l'erreur numérique visible dans le pointage et des messages de compteurs.
# Les expressions évaluées sont extraites du vrai code d'interface ; aucune donnée scolaire n'est ouverte.
$ErrorActionPreference='Stop'
function Assert-ConversionV57([bool]$Condition,[string]$Message){if(-not $Condition){throw $Message}}
function Read-ConversionSourceV57([string]$Name){
    $tokens=$null;$errors=$null
    $ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot $Name),[ref]$tokens,[ref]$errors)
    if($errors.Count){throw ('Syntaxe invalide : '+$Name)}
    return $ast
}
$expressions=@()
foreach($spec in @(
    @('InterfacePersonnelV56.ps1','Update-ScoliaTeacherAttendanceDialog','enseignant(s) ·'),
    @('InterfacePersonnelV56.ps1','Update-ScoliaTeacherManager','Pour archiver'),
    @('PresencesV55.ps1','Update-ScoliaAttendanceSummaryDialog','statut(s) enregistrés')
)){
    $ast=Read-ConversionSourceV57 $spec[0];$functionName=$spec[1];$needle=$spec[2]
    $definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $functionName},$true)
    Assert-ConversionV57 ([bool]$definition) ('Fonction absente : '+$functionName)
    $command=$definition.Find({param($node)$node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -in @('Set-ScoliaPersonnelInterfaceMessage','Set-ScoliaAttendanceMessage') -and $node.Extent.Text.Contains($needle)},$true)
    Assert-ConversionV57 ([bool]$command -and $command.CommandElements.Count -ge 3) ('Message réel introuvable : '+$functionName)
    $expressions+=,[scriptblock]::Create($command.CommandElements[2].Extent.Text)
}
$ast=Read-ConversionSourceV57 'SuiviApprenantsV56.ps1'
$detail=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq 'Show-ScoliaPupilAttendanceDetail'},$true)
$assignment=$detail.Find({param($node)$node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left.Extent.Text -ceq '$detail.Message.Text'},$true)
Assert-ConversionV57 ([bool]$assignment) 'Message du détail apprenant introuvable.'
$expressions+=,[scriptblock]::Create($assignment.Right.Extent.Text)
foreach($count in @(0,1,58)){
    $teachers=@(for($i=0;$i -lt $count;$i++){[pscustomobject]@{Id=$i}})
    $State=[pscustomobject]@{Teachers=$teachers};$rows=$teachers;$calls=$count
    $day=[datetime]'2024-10-07';$note='Pointage disponible.'
    $row=[pscustomobject]@{Presents=$count;Absents=2;Justifies=1;Alert='Suivi disponible.'};$legacyNote=''
    foreach($expression in $expressions){
        $message=& $expression
        Assert-ConversionV57 ($message -is [string] -and $message.StartsWith($count.ToString()+' ')) ('Le compteur '+$count+' empêche encore le chargement d''une interface.')
    }
}

# Horloge WinForms : simulation sans attente, contrôle de 08:00 et du changement de jour.
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
$ast=Read-ConversionSourceV57 'InterfacePersonnelV56.ps1'
foreach($name in @('Update-ScoliaTeacherAttendanceClock','Stop-ScoliaTeacherAttendanceClock')){
    $definition=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -ceq $name},$true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$script:clockRefreshesV57=0;$script:busy=$false
$teacher=New-ScoliaTeacher -Nom 'Test horloge' -ActiveFrom '2024-10-01' -Actor 'test'
function Update-ScoliaTeacherAttendanceDialog([object]$State){
    $script:clockRefreshesV57++
    $State.Status=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $State.DatePicker.Value.Date -WorkDates @('2024-10-07','2024-10-08') -AsOf $script:clockAsOfV57
    return $true
}
$form=New-Object Windows.Forms.Form
$picker=New-Object Windows.Forms.DateTimePicker;$picker.MinDate=[datetime]'2024-01-01';$picker.MaxDate=[datetime]'2024-12-31';$picker.Value=[datetime]'2024-10-07';$form.Controls.Add($picker)
$grid=New-Object Windows.Forms.DataGridView;$grid.AllowUserToAddRows=$false;[void]$grid.Columns.Add('Entry','Entrée');$form.Controls.Add($grid)
$timer=New-Object Windows.Forms.Timer;$timer.Interval=15000
$State=[pscustomobject]@{Form=$form;Grid=$grid;DatePicker=$picker;Loading=$false;ClockMinute='2024-10-07 07:59';ClockDate=[datetime]'2024-10-07';ClockTimer=$timer;ClockTimerDisposed=$false;Status=$null}
try{
    $form.Show();[Windows.Forms.Application]::DoEvents();$timer.Start()
    $script:clockAsOfV57=[datetimeoffset]'2024-10-07T08:00:00+02:00'
    Update-ScoliaTeacherAttendanceClock -State $State -Now $script:clockAsOfV57
    Assert-ConversionV57 ($script:clockRefreshesV57 -eq 1 -and $State.Status.LatePending -and -not $State.Status.IsAbsent) 'Le passage à 08:00 ne signale pas le retard sans actualisation manuelle.'
    Update-ScoliaTeacherAttendanceClock -State $State -Now $script:clockAsOfV57.AddSeconds(5)
    Assert-ConversionV57 ($script:clockRefreshesV57 -eq 1) 'L''horloge recharge inutilement plusieurs fois la même minute.'
    $script:clockAsOfV57=[datetimeoffset]'2024-10-08T00:00:00+02:00'
    Update-ScoliaTeacherAttendanceClock -State $State -Now $script:clockAsOfV57
    Assert-ConversionV57 ($State.DatePicker.Value.Date -eq [datetime]'2024-10-08' -and $script:clockRefreshesV57 -eq 2) 'La journée de pointage ne suit pas minuit.'
    $State.DatePicker.Value=[datetime]'2024-10-07'
    $script:clockAsOfV57=[datetimeoffset]'2024-10-09T00:00:00+02:00'
    Update-ScoliaTeacherAttendanceClock -State $State -Now $script:clockAsOfV57
    Assert-ConversionV57 ($State.DatePicker.Value.Date -eq [datetime]'2024-10-07') 'L''horloge remplace une consultation historique par la date actuelle.'
    Stop-ScoliaTeacherAttendanceClock $State
    Assert-ConversionV57 ($State.ClockTimerDisposed -and -not $timer.Enabled) 'Le timer ne se libère pas après fermeture.'
    Stop-ScoliaTeacherAttendanceClock $State
}finally{Stop-ScoliaTeacherAttendanceClock $State;$form.Dispose()}
Write-Output 'PASS : compteurs 0/1/58 des quatre interfaces, retard à 08:00 sans actualisation manuelle, changement de jour, consultation historique préservée et timer libéré.'
