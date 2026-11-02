# Domaine du pointage : heures locales exactes et cases toujours accessibles après 08 h.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'PersonnelV56.ps1')
function Assert-HourV57([bool]$ok,[string]$message){if(-not $ok){throw $message}}
$teacher=New-ScoliaTeacher -Nom 'Enseignant seuil' -ActiveFrom '2024-01-01' -Actor 'test'
$day='2024-10-07';$asOf=[datetimeoffset]::Parse($day+'T09:00:00+02:00');$work=@($day)
$before=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $day -WorkDates $work -AsOf ([datetimeoffset]::Parse($day+'T07:59:59+02:00'))
Assert-HourV57 (-not $before.IsLate -and $before.Code -eq 'Awaiting') 'Avant huit heures, la case non signée ne doit pas être en retard.'
$waiting=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $day -WorkDates $work -AsOf ([datetimeoffset]::Parse($day+'T08:00:00+02:00'))
Assert-HourV57 ($waiting.IsLate -and $waiting.LatePending -and -not $waiting.LateRecorded -and -not $waiting.IsAbsent) 'À huit heures, un retard signalé doit permettre la signature sans créer une absence anticipée.'
foreach($time in @('07:59:59','08:00:00','08:00:01','15:00:00')){
 $doc=[pscustomobject]@{Schema=1;Revision=0;Items=@($teacher)}
 $entry=[datetimeoffset]::Parse($day+'T'+$time+'+02:00');$late=$time -ne '07:59:59'
 Set-ScoliaTeacherPunch -Document $doc -TeacherId $teacher.Id -Direction Entry -Now $entry -Actor 'test' -ExpectedRevision 0
 $state=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $day -Punches @($doc.Items|Where-Object {$_.Kind -eq 'Punch'}) -WorkDates $work -AsOf $entry.AddMinutes(1)
 Assert-HourV57 ($state.LateRecorded -eq $late -and $state.SignatureCount -eq 1 -and -not $state.IsAbsent) 'L heure entrée doit déterminer le retard sans bloquer la sortie.'
 Set-ScoliaTeacherPunch -Document $doc -TeacherId $teacher.Id -Direction Exit -Now $entry.AddMinutes(30) -Actor 'test' -ExpectedRevision 0
 $state=Get-ScoliaTeacherDailyStatus -Teacher $teacher -Date $day -Punches @($doc.Items|Where-Object {$_.Kind -eq 'Punch'}) -WorkDates $work -AsOf $entry.AddMinutes(31)
 Assert-HourV57 ($state.SignatureCount -eq 2 -and $state.LateRecorded -eq $late -and -not $state.IsAbsent) 'Les deux signatures restent possibles après huit heures.'
 Assert-HourV57 (($late -and $state.Code -eq 'Late') -or (-not $late -and $state.Code -eq 'Present')) 'Le statut final doit distinguer présence ponctuelle et en retard.'
}
'PASS : frontière 08:00, heure locale, retard visible et signatures accessibles.'
