$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot 'NotificationsModernes.cs'
if(-not (Test-Path -LiteralPath $source -PathType Leaf)){throw 'NotificationsModernes.cs introuvable.'}
$text=[IO.File]::ReadAllText($source)
if($text -match '\bITMToastButton\b'){throw 'Ancienne référence ITMToastButton encore présente.'}
if($text -notmatch 'public sealed class ITMNotificationButton\s*:\s*Button'){throw 'ITMNotificationButton est absent.'}
if($text -notmatch 'new ITMNotificationButton\s*\{Text=Label\(result\)'){throw 'Les boutons de dialogue ne reposent pas sur ITMNotificationButton.'}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path $source -ReferencedAssemblies System.Windows.Forms,System.Drawing -IgnoreWarnings -ErrorAction Stop
if(-not ('ITMMessageBox' -as [type]) -or -not ('ITMNotificationButton' -as [type])){throw 'Compilation des notifications incomplète.'}
Write-Host 'OK - Correctif NotificationsModernes V72.1 valide.' -ForegroundColor Green
