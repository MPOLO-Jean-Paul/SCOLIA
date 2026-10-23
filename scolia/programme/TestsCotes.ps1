$ErrorActionPreference='Stop'
if(-not ('ITMGradeMatcher' -as [type])){Add-Type -Path (Join-Path $PSScriptRoot 'CotesMatching.cs')}
$learners=[ITMGradeLearner[]]@([ITMGradeLearner]@{Id='1';Name='Ngoy Émile Jean-Pierre'},[ITMGradeLearner]@{Id='2';Name='Ban Za Anne'})
$input=[ITMGradeInput[]]@([ITMGradeInput]@{Name=' NGOY emile Jean Pierre ';Grade=0;SourceRow=2},[ITMGradeInput]@{Name='Ban Za Anne';Grade=20;SourceRow=3},[ITMGradeInput]@{Name='Inconnu';Grade=10;SourceRow=4})
$r=[ITMGradeMatcher]::Match($learners,$input,20);if($r[0].Id -ne '1' -or $r[0].Grade -ne 0 -or $r[1].Id -ne '2' -or -not $r[2].Problem){throw 'Matching incorrect'}
$r=[ITMGradeMatcher]::Match($learners,[ITMGradeInput[]]@($input[0],$input[0]),20);if(@($r | Where-Object {$_.Problem}).Count -ne 2){throw 'Duplicate not blocked'}
$learners[1].Name=$learners[0].Name;$r=[ITMGradeMatcher]::Match($learners,[ITMGradeInput[]]@($input[0]),20);if($r[0].Problem -notlike '*Homonymes*'){throw 'Homonyms not blocked'}
$learners[1].Name='Ban Za Anne';$input[1].Grade=21;$r=[ITMGradeMatcher]::Match($learners,$input,20);if($r[1].Problem -notlike '*barème*'){throw 'Range not blocked'}
'PASS matching accents, separators, zero, unknown, duplicates, homonyms and range'
