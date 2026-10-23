# Exécute la construction réelle de la table de grille sans fenêtre.
$ErrorActionPreference='Stop'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Application.ps1'),[ref]$tokens,[ref]$errors)
$node=$ast.Find({param($n)$n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq 'Set-Grid'},$true)
$source=$node.Extent.Text;$prefix=$source.Substring(0,$source.IndexOf('    # Keep identity'))
$prefix=$prefix.Replace('function Set-Grid','function New-TestCourseTable')+"    return ,`$table`n    } finally {}`n}"
. ([scriptblock]::Create($prefix))
$rows=@([pscustomobject]@{Course='Cours 10';Number=10;Hours=[decimal]10.5},[pscustomobject]@{Course='Cours 2';Number=2;Hours=[decimal]2.5},[pscustomobject]@{Course='Sans valeur';Number=$null;Hours=$null})
$table=New-TestCourseTable $null $rows @(@('Course','Cours'),@('Number','Numéro',[int]),@('Hours','Heures',[decimal]))
try{
 if($table.Columns['Heures'].DataType -ne [decimal] -or $table.Columns['Numéro'].DataType -ne [int]){throw 'Colonnes numériques converties en texte'}
 $table.DefaultView.RowFilter='Heures IS NOT NULL';$table.DefaultView.Sort='Heures ASC'
 if($table.DefaultView[0]['Cours'] -ne 'Cours 2'){throw 'Tri horaire lexicographique'}
 $table.DefaultView.Sort='Numéro ASC';if($table.DefaultView[0]['Numéro'] -ne 2){throw 'Tri des numéros incorrect'}
 if($table.Rows[2]['Heures'] -ne [DBNull]::Value){throw 'Valeur absente changée en zéro'}
}finally{$table.Dispose()}
$legacy=New-TestCourseTable $null $rows @(@('Course','Cours'),@('Number','Numéro'))
try{if($legacy.Columns['Numéro'].DataType -ne [string] -or $legacy.Rows[2]['Numéro'] -ne ''){throw 'Comportement des anciennes grilles modifié'}}finally{$legacy.Dispose()}
'PASS : volumes horaires et numéros numériques, tri correct, valeur absente préservée et anciennes grilles compatibles.'
