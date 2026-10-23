# SCOLIA configuration is shared only through a folder explicitly chosen by the user.
$script:scoliaPointer=Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'configuration\scolia.json'
$script:scolia=$null;$script:scoliaSession=$null;$script:scoliaRoot='';$script:scoliaProfileFile=''
function Write-ScoliaJson([string]$path,$value){
    if([string]::IsNullOrWhiteSpace($path)){throw 'Chemin de configuration vide.'}
    $directory=[IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path))
    [void][IO.Directory]::CreateDirectory($directory)
    $temp=$path+'.'+[guid]::NewGuid().ToString('N')+'.tmp'
    $backup=$path+'.bak'
    try {
        $json=ConvertTo-Json -InputObject $value -Depth 12 -ErrorAction Stop
        $bytes=[Text.UTF8Encoding]::new($false).GetBytes($json)
        $stream=New-Object IO.FileStream($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
        try {$stream.Write($bytes,0,$bytes.Length);$stream.Flush($true)} finally {$stream.Dispose()}
        if([IO.File]::Exists($path)) {
            # File.Replace can fail if a stale backup already exists on the target PC.
            if([IO.File]::Exists($backup)){[IO.File]::Delete($backup)}
            [IO.File]::Replace($temp,$path,$backup,$true)
        } else {
            [IO.File]::Move($temp,$path)
        }
    } finally {
        if([IO.File]::Exists($temp)){[IO.File]::Delete($temp)}
    }
}
function Get-ScoliaAccountName($account){if($account.PSObject.Properties['Name'] -and -not [string]::IsNullOrWhiteSpace([string]$account.Name)){return [string]$account.Name};return [string]$account.Role}
function Get-ScoliaAccountId($account){if($account.PSObject.Properties['Id'] -and $account.Id){return [string]$account.Id};return [string]$account.Role}
function Get-ScoliaAccountLabel($account){$name=Get-ScoliaAccountName $account;if($name -ceq $account.Role){return $name};return ($name+' · '+$account.Role)}
function Test-ScoliaAccountActive($a){return ($a -and (-not $a.PSObject.Properties['Active'] -or $a.Active))}
function Test-ScoliaAccountAdmin($a){return ($a -and (-not $a.PSObject.Properties['IsAdmin'] -or $a.IsAdmin))}
function Test-ScoliaProfile($profile){
 if(([string]$profile.Revision -notmatch '^[1-9][0-9]*$') -or [decimal]$profile.Revision -gt [int]::MaxValue -or $profile.Version -ne 1 -or [string]$profile.Id -notmatch '^[a-f0-9]{32}$' -or [string]::IsNullOrWhiteSpace([string]$profile.School) -or ([string]$profile.School).Length -gt 160){throw 'Configuration SCOLIA invalide.'}
 if([int]$profile.Months -lt 1 -or [int]$profile.Months -gt 12 -or [int]$profile.StartMonth -lt 1 -or [int]$profile.StartMonth -gt 12 -or [string]$profile.Year -notmatch '^\d{4}\s*-\s*\d{4}$'){throw 'Année ou calendrier scolaire invalide.'}
 $years=[regex]::Matches([string]$profile.Year,'\d{4}');if([int]$years[1].Value -ne [int]$years[0].Value+1){throw 'Lʼannée scolaire doit couvrir deux années consécutives.'}
 if(@($profile.Classes).Count -eq 0 -or @($profile.Classes).Count -gt 150){throw 'Ajoutez au moins une classe.'};$seen=@{}
 foreach($c in $profile.Classes){$key=Promo $c.Code;if($key -notmatch '^[A-Z0-9]{4}$' -or $seen.ContainsKey($key) -or [decimal]$c.Fee -le 0 -or [decimal]$c.Fee -gt 100000000000 -or [decimal]$c.Fee -ne [Math]::Truncate([decimal]$c.Fee)){throw 'Code de classe : 4 caractères alphanumériques uniques ; tarif positif en FC entiers.'};$seen[$key]=$true}
 if(@($profile.Accounts).Count -eq 0 -or @($profile.Accounts).Count -gt 100){throw 'Choisissez les comptes.'};$seen=@{};$seenIds=@{}
 foreach($a in $profile.Accounts){if($a.PSObject.Properties['Name'] -and ([string]::IsNullOrWhiteSpace([string]$a.Name) -or ([string]$a.Name).Length -gt 160)){throw 'Nom de compte obligatoire, limité à 160 caractères.'};if($a.PSObject.Properties['Id'] -and [string]$a.Id -notmatch '^[a-f0-9]{32}$'){throw 'Identité de compte invalide.'};if($a.PSObject.Properties['Id']){if($seenIds.ContainsKey([string]$a.Id)){throw 'Identité de compte dupliquée.'};$seenIds[[string]$a.Id]=$true};if($a.Role -notin @('Secrétariat','Comptabilité','Mixte') -or $a.Locked -isnot [bool]){throw 'Compte métier invalide.'};$seen[[string]$a.Role]=$true;if($a.Locked){if(-not $a.Secret -or [Convert]::FromBase64String($a.Secret.Salt).Length -ne 32 -or [Convert]::FromBase64String($a.Secret.Hash).Length -ne 32){throw 'Protection de compte invalide.'}}}
 $keys=@{};foreach($a in $profile.Accounts){$id=Get-ScoliaAccountId $a;if($keys.ContainsKey($id)){throw 'Identités de compte dupliquées. Attribuez un identifiant individuel.'};$keys[$id]=$true;foreach($flag in @('Active','IsAdmin','CanCorrect','CanManageFees')){if($a.PSObject.Properties[$flag] -and $a.$flag -isnot [bool]){throw 'Droit de compte invalide.'}}}
 if(-not @($profile.Accounts|Where-Object {(Test-ScoliaAccountActive $_) -and (Test-ScoliaAccountAdmin $_)}).Count){throw 'Conservez au moins un administrateur actif.'}
}
function Read-ScoliaConfiguration {
 if(-not [IO.File]::Exists($script:scoliaPointer)){return $false}
 $pointer=[IO.File]::ReadAllText($script:scoliaPointer)|ConvertFrom-Json -ErrorAction Stop
 $script:scoliaRoot=[IO.Path]::GetFullPath([string]$pointer.Root);$script:scoliaProfileFile=Join-Path $script:scoliaRoot 'profil-scolia.json'
 if(-not [IO.File]::Exists($script:scoliaProfileFile)){throw 'La configuration SCOLIA mémorisée est inaccessible. Reconnectez le réseau ou le support ; la configuration existante est conservée.'}
 $profile=[IO.File]::ReadAllText($script:scoliaProfileFile)|ConvertFrom-Json -ErrorAction Stop;Test-ScoliaProfile $profile
 if($profile.Id -cne $pointer.Id){throw 'Ce dossier appartient à une autre installation SCOLIA.'};$script:scolia=$profile;return $true
}
function Set-ScoliaLocation([string]$root,$profile){
 Test-ScoliaProfile $profile;$script:scoliaRoot=[IO.Path]::GetFullPath($root);$script:scoliaProfileFile=Join-Path $script:scoliaRoot 'profil-scolia.json'
 Write-ScoliaJson $script:scoliaPointer ([pscustomobject]@{Root=$script:scoliaRoot;Id=$profile.Id});$script:scolia=$profile
}
function New-ScoliaDialog([string]$title,[int]$width=680,[int]$height=530){
 $d=New-Object Windows.Forms.Form;$d.Text='SCOLIA | '+$title;$d.StartPosition='CenterScreen';$d.Size=New-Object Drawing.Size($width,$height);$d.MinimumSize=New-Object Drawing.Size(380,330);$d.Font=New-Object Drawing.Font('Segoe UI',10);$d.AutoScaleMode='Dpi';Set-ITMWindowIcon $d;return $d
}
function New-ScoliaTable($parent){$t=New-Object Windows.Forms.TableLayoutPanel;$t.Dock='Fill';$t.AutoScroll=$true;$t.Padding=New-Object Windows.Forms.Padding(18);$t.ColumnCount=2;$t.AutoSize=$false;[void]$t.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,35)));[void]$t.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,65)));$parent.Controls.Add($t)
 if($parent -is [Windows.Forms.Form]){$parent.Add_Shown({param($sender,$e)foreach($child in @($sender.Controls)){if($child -is [Windows.Forms.TableLayoutPanel] -and $child.Dock -eq 'Fill' -and $child.Tag -ne 'scolia:cash-layout' -and (Get-Command Enable-ITMFluidTable -ErrorAction SilentlyContinue)){Enable-ITMFluidTable $child}}})}
 return $t}
function Add-ScoliaField($table,[string]$caption,$control){$control.AccessibleName=$caption;$row=$table.RowCount;$table.RowCount++;[void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));$label=New-Label $table $caption 0 0 100 32;$label.AutoSize=$true;$label.Dock='Top';$label.Margin=New-Object Windows.Forms.Padding(0,8,10,10);$control.Dock='Top';$control.Margin=New-Object Windows.Forms.Padding(0,5,0,8);$table.Controls.Add($control,1,$row);$table.SetCellPosition($label,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,$row)));return $control}
function Add-ScoliaText($table,[string]$caption,[string]$value=''){ $c=New-Object Windows.Forms.TextBox;$c.Text=$value;[void](Add-ScoliaField $table $caption $c);return $c}
function Add-ScoliaButton($parent,[string]$caption,[scriptblock]$action){$b=New-Button $parent $caption 0 0 210;$b.AutoSize=$true;$b.Margin=New-Object Windows.Forms.Padding(6);$b.Add_Click($action);return $b}
function New-ScoliaCardContent($parent,[string]$title,[string]$hint,[int]$headerHeight=56,[Drawing.Color]$backColor=[Drawing.Color]::White){
 # AutoSize keeps wrapped headings and native editors in separate rows at every DPI.
 $layout=New-Object Windows.Forms.TableLayoutPanel
 $layout.Dock='Top';$layout.AutoSize=$true;$layout.AutoSizeMode='GrowAndShrink';$layout.Margin=New-Object Windows.Forms.Padding(0);$layout.Padding=New-Object Windows.Forms.Padding(16,12,16,12);$layout.BackColor=$backColor
 $layout.ColumnCount=1;$layout.RowCount=3
 foreach($i in 1..3){[void]$layout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))}
 $parent.Controls.Add($layout)
 $heading=New-Object Windows.Forms.Label
 $heading.Text=$title;$heading.AutoSize=$true;$heading.Dock='Top';$heading.Margin=New-Object Windows.Forms.Padding(0,0,0,4)
 $heading.Font=New-Object Drawing.Font('Segoe UI Semibold',10.5,[Drawing.FontStyle]::Bold);$heading.ForeColor=[Drawing.Color]::FromArgb(30,41,59);$heading.Tag='scolia:heading'
 $layout.Controls.Add($heading,0,0)
 $note=New-Object Windows.Forms.Label
 $note.Text=$hint;$note.AutoSize=$true;$note.Dock='Top';$note.Margin=New-Object Windows.Forms.Padding(0,0,0,12)
 $note.Font=New-Object Drawing.Font('Segoe UI',8.5);$note.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$note.Tag='scolia:field-label'
 $layout.Controls.Add($note,0,1)
 $body=New-Object Windows.Forms.Panel
 $body.Dock='Top';$body.AutoSize=$true;$body.AutoSizeMode='GrowAndShrink';$body.Margin=New-Object Windows.Forms.Padding(0);$body.BackColor=$backColor
 $layout.Controls.Add($body,0,2)
 $layout.Add_SizeChanged({param($sender,$e)
  $width=[Math]::Max(1,$sender.ClientSize.Width-$sender.Padding.Horizontal)
  foreach($child in $sender.Controls){if($child -is [Windows.Forms.Label]){$child.MaximumSize=New-Object Drawing.Size($width,0)}}
 })
 return [pscustomobject]@{Layout=$layout;Header=$layout;Title=$heading;Hint=$note;Body=$body}
}
function New-ScoliaSetupFields($parent){
 $table=New-Object Windows.Forms.TableLayoutPanel;$table.Dock='Top';$table.AutoSize=$true;$table.AutoSizeMode='GrowAndShrink';$table.Margin=New-Object Windows.Forms.Padding(0);$table.Padding=New-Object Windows.Forms.Padding(0);$table.ColumnCount=1
 [void]$table.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)))
 $parent.Controls.Add($table)
 $table.Add_SizeChanged({param($sender,$e)
  $width=[Math]::Max(1,$sender.ClientSize.Width-$sender.Padding.Horizontal)
  foreach($child in $sender.Controls){if($child -is [Windows.Forms.Label]){$child.MaximumSize=New-Object Drawing.Size($width,0)}}
 })
 return $table
}
function Add-ScoliaSetupField($table,[string]$caption,$control){
 $row=$table.RowCount;$table.RowCount+=2
 [void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
 [void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
 $label=New-Object Windows.Forms.Label;$label.Text=$caption;$label.AutoSize=$true;$label.Dock='Top';$label.Margin=New-Object Windows.Forms.Padding(0,0,0,4);$label.Font=New-Object Drawing.Font('Segoe UI Semibold',8.75,[Drawing.FontStyle]::Bold);$label.ForeColor=[Drawing.Color]::FromArgb(71,85,105);$label.Tag='scolia:field-label'
 $table.Controls.Add($label,0,$row);$control.Dock='Top';$control.Margin=New-Object Windows.Forms.Padding(0,0,0,8);$control.AccessibleName=$caption
 $table.Controls.Add($control,0,($row+1));return $control
}
function Add-ScoliaSetupText($table,[string]$caption,[string]$value=''){
 $control=New-Object Windows.Forms.TextBox;$control.Text=$value;[void](Add-ScoliaSetupField $table $caption $control);return $control
}
function Add-ScoliaSetupNote($table,$control){
 $row=$table.RowCount;$table.RowCount++
 [void]$table.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
 $control.Dock='Top';$control.AutoSize=$true;$control.Margin=New-Object Windows.Forms.Padding(0,0,0,8);$table.Controls.Add($control,0,$row)
}
function ConvertFrom-ScoliaClassText([string]$text){
 $items=New-Object 'System.Collections.Generic.List[object]';$seen=@{};$lineNumber=0
 foreach($raw in ($text -split '\r?\n')){
  $lineNumber++;$line=$raw.Trim();if(-not $line){continue}
  $parts=$line.Split(';');if($parts.Count -ne 2){throw ('Classe ligne '+$lineNumber+' : utilisez CODE;montant FC, par exemple INFA;25000.')}
  $code=([string]$parts[0]).Trim().ToUpperInvariant().Replace(' ','').Replace('-','');if($code -eq 'TML1'){$code='TLM1'};if($code -notmatch '^[A-Z0-9]{4}$'){throw ('Classe ligne '+$lineNumber+' : le code doit contenir exactement 4 lettres ou chiffres.')}
  if($seen.ContainsKey($code)){throw ('Classe ligne '+$lineNumber+' : le code '+$code+' est déjà présent.')}
  $fee=[decimal]0
  if(-not [decimal]::TryParse($parts[1].Trim(),[Globalization.NumberStyles]::Integer,[Globalization.CultureInfo]::InvariantCulture,[ref]$fee) -or $fee -le 0 -or $fee -gt 100000000000){throw ('Classe ligne '+$lineNumber+' : saisissez un montant FC entier positif.')}
  $seen[$code]=$true;$items.Add([pscustomobject]@{Code=$code;Fee=$fee})
 }
 if($items.Count -eq 0){throw 'Ajoutez au moins une classe.'}
 return $items.ToArray()
}
function Test-ScoliaDataRootCandidate([string]$root,[bool]$requireWritable=$false){
 if([string]::IsNullOrWhiteSpace($root)){throw 'Choisissez le dossier qui contiendra les données SCOLIA.'}
 try{$full=[IO.Path]::GetFullPath($root.Trim())}catch{throw 'Le chemin du dossier de données est invalide.'}
 if([IO.File]::Exists($full)){throw 'Le chemin des données désigne un fichier. Choisissez un dossier.'}
 if($requireWritable){
  $probe=$null
  try{
   [void][IO.Directory]::CreateDirectory($full)
   $probe=Join-Path $full ('.scolia-ecriture-'+[Guid]::NewGuid().ToString('N')+'.tmp')
   [IO.File]::WriteAllText($probe,'SCOLIA',[Text.Encoding]::ASCII)
  }catch{throw ('SCOLIA ne peut pas écrire dans ce dossier : '+$_.Exception.Message)}
  finally{if($probe -and [IO.File]::Exists($probe)){try{[IO.File]::Delete($probe)}catch{}}}
 }
 return $full
}
function Show-ScoliaLaunchTransition {
 Show-ScoliaTransition 'Initialisation de votre espace de travail...'
 return [pscustomobject]@{Active=$true}
}
function Set-ScoliaLaunchTransition($state,[string]$text){Update-ScoliaTransition $text}
function Close-ScoliaLaunchTransition($state){Close-ScoliaTransition}
function Show-ScoliaSetup {
 param([switch]$LayoutOnly)
 $d=New-ScoliaDialog 'Configuration initiale' 1120 760
 $d.MinimumSize=New-Object Drawing.Size(560,440)
 $d.BackColor=[Drawing.Color]::FromArgb(243,246,252)
 $d.AutoScaleDimensions=New-Object Drawing.SizeF(96,96);$d.KeyPreview=$true
 $d.Add_Shown({param($sender,$e)
  Close-ScoliaTransition
  try{
   $area=[Windows.Forms.Screen]::FromControl($sender).WorkingArea
   $q=[Math]::Max(1.0,$sender.CurrentAutoScaleDimensions.Height/96.0)
   $sender.MinimumSize=New-Object Drawing.Size([Math]::Min([int](560*$q),$area.Width-24),[Math]::Min([int](440*$q),$area.Height-24))
   $targetWidth=[Math]::Min($sender.Width,[Math]::Max(320,$area.Width-24))
   $targetHeight=[Math]::Min($sender.Height,[Math]::Max(300,$area.Height-24))
   if($sender.Width -ne $targetWidth -or $sender.Height -ne $targetHeight){$sender.Size=New-Object Drawing.Size($targetWidth,$targetHeight)}
   $sender.Location=New-Object Drawing.Point(($area.Left+[Math]::Max(0,[int](($area.Width-$sender.Width)/2))),($area.Top+[Math]::Max(0,[int](($area.Height-$sender.Height)/2))))
  }catch{}
 })

 $shell=New-Object Windows.Forms.TableLayoutPanel
 $shell.Dock='Fill';$shell.Padding=New-Object Windows.Forms.Padding(0);$shell.Margin=New-Object Windows.Forms.Padding(0)
 $shell.ColumnCount=1;$shell.RowCount=4
 [void]$shell.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,80)))
 [void]$shell.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,48)))
 [void]$shell.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)))
 [void]$shell.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,78)))
 $d.Controls.Add($shell)

 # En-tête professionnel, sans coordonnées rigides.
 $header=New-Object Windows.Forms.TableLayoutPanel;$header.Dock='Fill';$header.Padding=New-Object Windows.Forms.Padding(28,14,28,12);$header.Margin=New-Object Windows.Forms.Padding(0);$header.BackColor=[Drawing.Color]::FromArgb(248,251,255)
 $header.ColumnCount=3;$header.RowCount=1
 [void]$header.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,64)))
 [void]$header.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)))
 [void]$header.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,164)))
 $shell.Controls.Add($header,0,0)
 $logoPath=Join-Path $PSScriptRoot 'Assets\SCOLIA.png'
 $logo=$null
 if(Test-Path -LiteralPath $logoPath){
  try{
   $logoImage=[Drawing.Image]::FromFile($logoPath)
   $logo=New-Object Windows.Forms.PictureBox;$logo.Image=$logoImage;$logo.SizeMode='Zoom';$logo.Dock='Fill';$logo.Margin=New-Object Windows.Forms.Padding(0,2,14,2);$logo.TabStop=$false;$header.Controls.Add($logo,0,0)
   $logo.Add_Disposed({param($sender,$e)if($sender.Image){$sender.Image.Dispose();$sender.Image=$null}})
  }catch{}
 }
 $identity=New-Object Windows.Forms.TableLayoutPanel;$identity.Dock='Fill';$identity.ColumnCount=1;$identity.RowCount=2;$identity.Margin=New-Object Windows.Forms.Padding(0);$identity.BackColor=[Drawing.Color]::FromArgb(248,251,255)
 [void]$identity.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,58)));[void]$identity.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,42)));$header.Controls.Add($identity,1,0)
 $title=New-Object Windows.Forms.Label;$title.Text='Configurer SCOLIA';$title.Dock='Fill';$title.TextAlign='BottomLeft';$title.AutoEllipsis=$true;$title.Font=New-Object Drawing.Font('Segoe UI Semibold',18,[Drawing.FontStyle]::Bold);$title.ForeColor=[Drawing.Color]::FromArgb(15,23,42);$title.Margin=New-Object Windows.Forms.Padding(0);$identity.Controls.Add($title,0,0)
 $subtitle=New-Object Windows.Forms.Label;$subtitle.Text='Établissement et accès, en deux étapes.';$subtitle.Dock='Fill';$subtitle.TextAlign='TopLeft';$subtitle.AutoEllipsis=$true;$subtitle.Font=New-Object Drawing.Font('Segoe UI',9.25);$subtitle.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$subtitle.Margin=New-Object Windows.Forms.Padding(0,4,0,0);$identity.Controls.Add($subtitle,0,1)
 $badge=New-Object Windows.Forms.Label;$badge.Text='PREMIÈRE CONNEXION';$badge.Dock='Fill';$badge.TextAlign='MiddleCenter';$badge.Font=New-Object Drawing.Font('Segoe UI Semibold',8.25,[Drawing.FontStyle]::Bold);$badge.ForeColor=[Drawing.Color]::FromArgb(37,99,235);$badge.BackColor=[Drawing.Color]::FromArgb(232,240,254);$badge.Margin=New-Object Windows.Forms.Padding(14,16,0,16);$header.Controls.Add($badge,2,0)

 # Étapes : grille adaptative qui ne chevauche jamais le statut.
 $steps=New-Object Windows.Forms.TableLayoutPanel;$steps.Dock='Fill';$steps.Padding=New-Object Windows.Forms.Padding(26,10,26,8);$steps.Margin=New-Object Windows.Forms.Padding(0);$steps.BackColor=[Drawing.Color]::FromArgb(243,246,252);$steps.ColumnCount=3;$steps.RowCount=1
 [void]$steps.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,40)));[void]$steps.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,42)));[void]$steps.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,18)));$shell.Controls.Add($steps,0,1)
 $step1=New-Object ITMActionButton;$step1.Text='1   Établissement';$step1.Primary=$true;$step1.Dock='Fill';$step1.Margin=New-Object Windows.Forms.Padding(0,0,8,0);$steps.Controls.Add($step1,0,0)
 $step2=New-Object ITMActionButton;$step2.Text='2   Comptes et sécurité';$step2.Dock='Fill';$step2.Margin=New-Object Windows.Forms.Padding(0,0,8,0);$steps.Controls.Add($step2,1,0)
 $stepStatus=New-Object Windows.Forms.Label;$stepStatus.Text='Étape 1 sur 2';$stepStatus.Dock='Fill';$stepStatus.TextAlign='MiddleRight';$stepStatus.Font=New-Object Drawing.Font('Segoe UI Semibold',9);$stepStatus.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$stepStatus.Margin=New-Object Windows.Forms.Padding(0);$steps.Controls.Add($stepStatus,2,0)

 $contentHost=New-Object Windows.Forms.Panel;$contentHost.Dock='Fill';$contentHost.Padding=New-Object Windows.Forms.Padding(24,8,24,10);$contentHost.BackColor=[Drawing.Color]::FromArgb(243,246,252);$shell.Controls.Add($contentHost,0,2)
 $pageOne=New-Object ITMSectionPanel;$pageOne.Dock='Fill';$pageOne.Padding=New-Object Windows.Forms.Padding(0);$pageOne.BackColor=[Drawing.Color]::White;$pageOne.HeaderHeight=0;$contentHost.Controls.Add($pageOne)
 $pageTwo=New-Object ITMSectionPanel;$pageTwo.Dock='Fill';$pageTwo.Padding=New-Object Windows.Forms.Padding(0);$pageTwo.BackColor=[Drawing.Color]::White;$pageTwo.HeaderHeight=0;$pageTwo.Visible=$false;$contentHost.Controls.Add($pageTwo)

 $pageOneLayout=New-Object Windows.Forms.TableLayoutPanel;$pageOneLayout.Dock='Fill';$pageOneLayout.ColumnCount=1;$pageOneLayout.RowCount=2;$pageOneLayout.Padding=New-Object Windows.Forms.Padding(0);$pageOneLayout.Margin=New-Object Windows.Forms.Padding(0);[void]$pageOneLayout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,60)));[void]$pageOneLayout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)));$pageOne.Controls.Add($pageOneLayout)
 $pageOneHeader=New-Object Windows.Forms.TableLayoutPanel;$pageOneHeader.Dock='Fill';$pageOneHeader.ColumnCount=1;$pageOneHeader.RowCount=2;$pageOneHeader.Padding=New-Object Windows.Forms.Padding(22,10,22,6);$pageOneHeader.Margin=New-Object Windows.Forms.Padding(0);[void]$pageOneHeader.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,58)));[void]$pageOneHeader.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,42)));$pageOneLayout.Controls.Add($pageOneHeader,0,0)
 $p1Title=New-Object Windows.Forms.Label;$p1Title.Text='Établissement et année scolaire';$p1Title.Dock='Fill';$p1Title.TextAlign='BottomLeft';$p1Title.Font=New-Object Drawing.Font('Segoe UI Semibold',15.5,[Drawing.FontStyle]::Bold);$p1Title.ForeColor=[Drawing.Color]::FromArgb(15,23,42);$p1Title.Margin=New-Object Windows.Forms.Padding(0);$pageOneHeader.Controls.Add($p1Title,0,0)
 $p1Hint=New-Object Windows.Forms.Label;$p1Hint.Text='Identité, calendrier, classes et dossier de données.';$p1Hint.Dock='Fill';$p1Hint.TextAlign='TopLeft';$p1Hint.AutoEllipsis=$true;$p1Hint.Font=New-Object Drawing.Font('Segoe UI',8.75);$p1Hint.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$p1Hint.Margin=New-Object Windows.Forms.Padding(0);$pageOneHeader.Controls.Add($p1Hint,0,1)

 # Étape 1 en deux cartes : elle tient dans une fenêtre standard sans défilement global.
 $pageOneBody=New-Object Windows.Forms.TableLayoutPanel;$pageOneBody.Dock='Fill';$pageOneBody.Padding=New-Object Windows.Forms.Padding(18,6,18,8);$pageOneBody.Margin=New-Object Windows.Forms.Padding(0);$pageOneBody.ColumnCount=2;$pageOneBody.RowCount=1;$pageOneBody.AutoScroll=$true
 [void]$pageOneBody.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,43)));[void]$pageOneBody.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,57)));[void]$pageOneBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));$pageOneLayout.Controls.Add($pageOneBody,0,1)
 $basicCard=New-Object ITMFormSection;$basicCard.Dock='Top';$basicCard.AutoSize=$true;$basicCard.AutoSizeMode='GrowAndShrink';$basicCard.Margin=New-Object Windows.Forms.Padding(0,0,10,0);$basicCard.AccessibleName='Informations générales de l''établissement';$pageOneBody.Controls.Add($basicCard,0,0)
 $dataCard=New-Object ITMFormSection;$dataCard.Dock='Top';$dataCard.AutoSize=$true;$dataCard.AutoSizeMode='GrowAndShrink';$dataCard.Margin=New-Object Windows.Forms.Padding(10,0,0,0);$dataCard.AccessibleName='Classes, données et réseau';$pageOneBody.Controls.Add($dataCard,1,0)
 $basicCardParts=New-ScoliaCardContent $basicCard 'Identité de l''établissement' 'Nom de l''école, année scolaire et calendrier de paiement.' 56; $basicTable=New-ScoliaSetupFields $basicCardParts.Body
 $dataCardParts=New-ScoliaCardContent $dataCard 'Classes, données et réseau' 'Classes et emplacement des données.' 56; $dataTable=New-ScoliaSetupFields $dataCardParts.Body

 $name=Add-ScoliaSetupText $basicTable 'Nom de l''école' (Get-ITMInstitutionName)
 $year=Add-ScoliaSetupText $basicTable 'Année scolaire' ((Get-Date).Year.ToString()+' - '+((Get-Date).Year+1))
 $months=New-Object Windows.Forms.NumericUpDown;$months.Minimum=1;$months.Maximum=12;$months.Value=10;$months.Height=36;[void](Add-ScoliaSetupField $basicTable 'Mois à payer' $months)
 $start=New-Object Windows.Forms.ComboBox;$start.DropDownStyle='DropDownList';$start.Items.AddRange([object[]][ScoliaLedger]::Months);$start.SelectedIndex=8;$start.Height=36;[void](Add-ScoliaSetupField $basicTable 'Premier mois' $start)

 $classes=New-Object Windows.Forms.TextBox;$classes.Multiline=$true;$classes.Height=56;$classes.ScrollBars='Vertical';$classes.AcceptsReturn=$true;$classes.Text='';[void](Add-ScoliaSetupField $dataTable 'Classes et mensualités' $classes)
 $quickClass=New-Object Windows.Forms.TableLayoutPanel;$quickClass.AutoSize=$true;$quickClass.AutoSizeMode='GrowAndShrink';$quickClass.Dock='Top';$quickClass.ColumnCount=3;$quickClass.RowCount=1;$quickClass.Margin=New-Object Windows.Forms.Padding(0,0,0,4)
 [void]$quickClass.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,24)));[void]$quickClass.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,34)));[void]$quickClass.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,42)))
 $quickCode=New-Object Windows.Forms.TextBox;$quickCode.Dock='Fill';$quickCode.Height=34;$quickCode.MaxLength=4;$quickCode.CharacterCasing='Upper';$quickCode.AccessibleName='Code de classe sur quatre caractères';$quickCode.Margin=New-Object Windows.Forms.Padding(0,0,6,0);$quickClass.Controls.Add($quickCode,0,0)
 $quickFee=New-Object Windows.Forms.NumericUpDown;$quickFee.Dock='Fill';$quickFee.Height=34;$quickFee.Minimum=0;$quickFee.Maximum=100000000000;$quickFee.ThousandsSeparator=$true;$quickFee.AccessibleName='Montant mensuel en francs congolais';$quickFee.Margin=New-Object Windows.Forms.Padding(0,0,6,0);$quickClass.Controls.Add($quickFee,1,0)
 $quickAdd=New-Object ITMActionButton;$quickAdd.Text='Ajouter la classe';$quickAdd.Dock='Fill';$quickAdd.Height=38;$quickAdd.Cursor='Hand';$quickAdd.Margin=New-Object Windows.Forms.Padding(0);$quickClass.Controls.Add($quickAdd,2,0)
 [void](Add-ScoliaSetupField $dataTable 'Ajout rapide' $quickClass)
 $classInfo=New-Object Windows.Forms.Label;$classInfo.Text='Format : CODE;montant FC — exemple : INFA;25000';$classInfo.AutoSize=$true;$classInfo.MaximumSize=New-Object Drawing.Size(430,0);$classInfo.Font=New-Object Drawing.Font('Segoe UI',8.25);$classInfo.ForeColor=[Drawing.Color]::FromArgb(100,116,139);Add-ScoliaSetupNote $dataTable $classInfo
 $root=Add-ScoliaSetupText $dataTable 'Dossier de données' (Join-Path ([IO.Path]::GetDirectoryName($PSScriptRoot)) 'data\scolia')
 $storageInfo=New-Object Windows.Forms.Label;$storageInfo.Text='Emplacement local sélectionné.';$storageInfo.AutoSize=$true;$storageInfo.MaximumSize=New-Object Drawing.Size(430,0);$storageInfo.Font=New-Object Drawing.Font('Segoe UI',8.25);$storageInfo.ForeColor=[Drawing.Color]::FromArgb(100,116,139);Add-ScoliaSetupNote $dataTable $storageInfo
 $networkActions=New-Object Windows.Forms.TableLayoutPanel;$networkActions.AutoSize=$true;$networkActions.AutoSizeMode='GrowAndShrink';$networkActions.Dock='Top';$networkActions.ColumnCount=3;$networkActions.RowCount=1;$networkActions.Margin=New-Object Windows.Forms.Padding(0,0,0,4)
 foreach($i in 1..3){[void]$networkActions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,33.333)))}
 $pick=New-Object ITMActionButton;$pick.Text='Choisir le dossier';$pick.Dock='Fill';$pick.Height=38;$pick.Cursor='Hand';$pick.Margin=New-Object Windows.Forms.Padding(0,0,5,0);$networkActions.Controls.Add($pick,0,0)
 $join=New-Object ITMActionButton;$join.Text='Rejoindre une base';$join.Dock='Fill';$join.Height=38;$join.Cursor='Hand';$join.Margin=New-Object Windows.Forms.Padding(0,0,5,0);$networkActions.Controls.Add($join,1,0)
 $automatic=New-Object ITMActionButton;$automatic.Text='Détecter le réseau';$automatic.Dock='Fill';$automatic.Height=38;$automatic.Cursor='Hand';$automatic.Margin=New-Object Windows.Forms.Padding(0);$networkActions.Controls.Add($automatic,2,0)
 Add-ScoliaSetupNote $dataTable $networkActions

 # Étape 2 : organisation en haut, comptes en cartes côte à côte.
 $pageTwoLayout=New-Object Windows.Forms.TableLayoutPanel;$pageTwoLayout.Dock='Fill';$pageTwoLayout.ColumnCount=1;$pageTwoLayout.RowCount=2;$pageTwoLayout.Padding=New-Object Windows.Forms.Padding(0);$pageTwoLayout.Margin=New-Object Windows.Forms.Padding(0);[void]$pageTwoLayout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,58)));[void]$pageTwoLayout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)));$pageTwo.Controls.Add($pageTwoLayout)
 $pageTwoHeader=New-Object Windows.Forms.TableLayoutPanel;$pageTwoHeader.Dock='Fill';$pageTwoHeader.ColumnCount=1;$pageTwoHeader.RowCount=2;$pageTwoHeader.Padding=New-Object Windows.Forms.Padding(22,10,22,4);$pageTwoHeader.Margin=New-Object Windows.Forms.Padding(0);[void]$pageTwoHeader.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,58)));[void]$pageTwoHeader.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,42)));$pageTwoLayout.Controls.Add($pageTwoHeader,0,0)
 $p2Title=New-Object Windows.Forms.Label;$p2Title.Text='Comptes et sécurité';$p2Title.Dock='Fill';$p2Title.TextAlign='BottomLeft';$p2Title.Font=New-Object Drawing.Font('Segoe UI Semibold',15,[Drawing.FontStyle]::Bold);$p2Title.ForeColor=[Drawing.Color]::FromArgb(15,23,42);$p2Title.Margin=New-Object Windows.Forms.Padding(0);$pageTwoHeader.Controls.Add($p2Title,0,0)
 $p2Hint=New-Object Windows.Forms.Label;$p2Hint.Text='Choisissez les comptes et leur protection par mot de passe.';$p2Hint.Dock='Fill';$p2Hint.TextAlign='TopLeft';$p2Hint.AutoEllipsis=$true;$p2Hint.Font=New-Object Drawing.Font('Segoe UI',8.75);$p2Hint.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$p2Hint.Margin=New-Object Windows.Forms.Padding(0);$pageTwoHeader.Controls.Add($p2Hint,0,1)
 $pageTwoBody=New-Object Windows.Forms.TableLayoutPanel;$pageTwoBody.Dock='Fill';$pageTwoBody.Padding=New-Object Windows.Forms.Padding(18,4,18,8);$pageTwoBody.Margin=New-Object Windows.Forms.Padding(0);$pageTwoBody.ColumnCount=1;$pageTwoBody.RowCount=2;$pageTwoBody.AutoScroll=$true
 [void]$pageTwoBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));[void]$pageTwoBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));$pageTwoLayout.Controls.Add($pageTwoBody,0,1)
 $orgCard=New-Object ITMFormSection;$orgCard.Dock='Top';$orgCard.AutoSize=$true;$orgCard.AutoSizeMode='GrowAndShrink';$orgCard.Padding=New-Object Windows.Forms.Padding(14,10,14,10);$orgCard.Margin=New-Object Windows.Forms.Padding(0,0,0,12);$pageTwoBody.Controls.Add($orgCard,0,0)
 $orgTable=New-Object Windows.Forms.TableLayoutPanel;$orgTable.Dock='Top';$orgTable.AutoSize=$true;$orgTable.AutoSizeMode='GrowAndShrink';$orgTable.ColumnCount=2;$orgTable.RowCount=1;$orgTable.Margin=New-Object Windows.Forms.Padding(0)
 [void]$orgTable.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,35)));[void]$orgTable.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,65)));[void]$orgTable.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));$orgCard.Controls.Add($orgTable)
 $orgLabel=New-Object Windows.Forms.Label;$orgLabel.Text='Organisation des comptes';$orgLabel.Dock='Fill';$orgLabel.AutoSize=$true;$orgLabel.TextAlign='MiddleLeft';$orgLabel.Margin=New-Object Windows.Forms.Padding(0,0,12,0);$orgLabel.Tag='scolia:heading';$orgLabel.Font=New-Object Drawing.Font('Segoe UI Semibold',9.5,[Drawing.FontStyle]::Bold);$orgTable.Controls.Add($orgLabel,0,0)
 $mode=New-Object Windows.Forms.ComboBox;$mode.DropDownStyle='DropDownList';$mode.Dock='Top';$mode.Margin=New-Object Windows.Forms.Padding(0);$mode.AccessibleName='Organisation des comptes';$mode.Items.AddRange([object[]]@('Deux comptes séparés','Compte mixte','Secrétariat seul','Comptabilité seule'));$mode.SelectedIndex=0;$orgTable.Controls.Add($mode,1,0)
 $accountsGrid=New-Object Windows.Forms.TableLayoutPanel;$accountsGrid.Dock='Top';$accountsGrid.AutoSize=$true;$accountsGrid.AutoSizeMode='GrowAndShrink';$accountsGrid.ColumnCount=2;$accountsGrid.RowCount=1;$accountsGrid.Margin=New-Object Windows.Forms.Padding(0);[void]$accountsGrid.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)));[void]$accountsGrid.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)));[void]$accountsGrid.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));$pageTwoBody.Controls.Add($accountsGrid,0,1)
 $accountSlot1=New-Object Windows.Forms.Panel;$accountSlot1.Dock='Top';$accountSlot1.AutoSize=$true;$accountSlot1.AutoSizeMode='GrowAndShrink';$accountSlot1.Margin=New-Object Windows.Forms.Padding(0,0,8,0);$accountsGrid.Controls.Add($accountSlot1,0,0)
 $accountSlot2=New-Object Windows.Forms.Panel;$accountSlot2.Dock='Top';$accountSlot2.AutoSize=$true;$accountSlot2.AutoSizeMode='GrowAndShrink';$accountSlot2.Margin=New-Object Windows.Forms.Padding(8,0,0,0);$accountsGrid.Controls.Add($accountSlot2,1,0)
 $controls=@{};$layoutState=[pscustomobject]@{Compact=$false;Step=0;Updating=$false;FooterStack=$false;FooterUpdating=$false}
 foreach($role in @('Secrétariat','Comptabilité','Mixte')){
  $card=New-Object ITMFormSection;$card.Dock='Top';$card.AutoSize=$true;$card.AutoSizeMode='GrowAndShrink';$card.Visible=$false
  $accountLayout=New-Object Windows.Forms.TableLayoutPanel;$accountLayout.Dock='Top';$accountLayout.AutoSize=$true;$accountLayout.AutoSizeMode='GrowAndShrink';$accountLayout.ColumnCount=1;$accountLayout.RowCount=2;$accountLayout.Margin=New-Object Windows.Forms.Padding(0);[void]$accountLayout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,34)));[void]$accountLayout.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));$card.Controls.Add($accountLayout)
  $roleLabel=New-Object Windows.Forms.Label;$roleLabel.Text=$role;$roleLabel.Dock='Fill';$roleLabel.TextAlign='MiddleLeft';$roleLabel.Font=New-Object Drawing.Font('Segoe UI Semibold',10.75,[Drawing.FontStyle]::Bold);$roleLabel.ForeColor=[Drawing.Color]::FromArgb(30,41,59);$roleLabel.Padding=New-Object Windows.Forms.Padding(14,0,8,0);$roleLabel.Margin=New-Object Windows.Forms.Padding(0);$accountLayout.Controls.Add($roleLabel,0,0)
  # Carte de compte dédiée : aucune ligne ne partage une hauteur insuffisante avec les champs mot de passe.
  $accountBody=New-Object Windows.Forms.TableLayoutPanel;$accountBody.Dock='Top';$accountBody.AutoSize=$true;$accountBody.AutoSizeMode='GrowAndShrink';$accountBody.Margin=New-Object Windows.Forms.Padding(0);$accountBody.Padding=New-Object Windows.Forms.Padding(14,6,14,8);$accountBody.ColumnCount=2;$accountBody.RowCount=5
  [void]$accountBody.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)));[void]$accountBody.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)))
  [void]$accountBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
  [void]$accountBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,38)))
  [void]$accountBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
  [void]$accountBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
  [void]$accountBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,52)))
  $accountLayout.Controls.Add($accountBody,0,1)

  $nameLabel=New-Object Windows.Forms.Label;$nameLabel.Text='Nom de la personne';$nameLabel.AutoSize=$true;$nameLabel.Dock='Fill';$nameLabel.TextAlign='BottomLeft';$nameLabel.Font=New-Object Drawing.Font('Segoe UI',8.75);$nameLabel.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$nameLabel.Margin=New-Object Windows.Forms.Padding(0,0,0,4);$accountBody.Controls.Add($nameLabel,0,0);$accountBody.SetColumnSpan($nameLabel,2)
  $person=New-Object Windows.Forms.TextBox;$person.Text=$role;$person.Dock='Fill';$person.AutoSize=$false;$person.Height=34;$person.Margin=New-Object Windows.Forms.Padding(0,0,0,6);$accountBody.Controls.Add($person,0,1);$accountBody.SetColumnSpan($person,2)
  $locked=New-Object ITMReadableCheckBox;$locked.Text='Demander un mot de passe';$locked.AutoSize=$true;$locked.Dock='Left';$locked.Margin=New-Object Windows.Forms.Padding(0,2,0,6);$accountBody.Controls.Add($locked,0,2);$accountBody.SetColumnSpan($locked,2)

  $passwordLabel=New-Object Windows.Forms.Label;$passwordLabel.Text='Mot de passe';$passwordLabel.AutoSize=$true;$passwordLabel.Dock='Fill';$passwordLabel.TextAlign='BottomLeft';$passwordLabel.Font=New-Object Drawing.Font('Segoe UI',8.75);$passwordLabel.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$passwordLabel.Margin=New-Object Windows.Forms.Padding(0,0,6,4);$accountBody.Controls.Add($passwordLabel,0,3)
  $confirmLabel=New-Object Windows.Forms.Label;$confirmLabel.Text='Confirmation';$confirmLabel.AutoSize=$true;$confirmLabel.Dock='Fill';$confirmLabel.TextAlign='BottomLeft';$confirmLabel.Font=New-Object Drawing.Font('Segoe UI',8.75);$confirmLabel.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$confirmLabel.Margin=New-Object Windows.Forms.Padding(6,0,0,4);$accountBody.Controls.Add($confirmLabel,1,3)
  $password=New-Object ScoliaPasswordBox;$password.Dock='Fill';$password.Margin=New-Object Windows.Forms.Padding(0,0,6,0);$password.MinimumSize=New-Object Drawing.Size(120,46);$accountBody.Controls.Add($password,0,4)
  $confirm=New-Object ScoliaPasswordBox;$confirm.Dock='Fill';$confirm.Margin=New-Object Windows.Forms.Padding(6,0,0,0);$confirm.MinimumSize=New-Object Drawing.Size(120,46);$accountBody.Controls.Add($confirm,1,4)
  $controls[$role]=[pscustomobject]@{Card=$card;Layout=$accountLayout;Body=$accountBody;RoleLabel=$roleLabel;Name=$person;Locked=$locked;Password=$password;Confirm=$confirm;PasswordLabel=$passwordLabel;ConfirmLabel=$confirmLabel}
  $locked.Tag=$controls[$role]
  $locked.Add_CheckedChanged({param($sender,$e)$sender.Tag.Password.Enabled=$sender.Checked;$sender.Tag.Confirm.Enabled=$sender.Checked;if($sender.Checked){$sender.Tag.Password.Focus()}})
  $password.Enabled=$false;$confirm.Enabled=$false
 }
 $layoutAccounts={
  $roles=@(switch($mode.SelectedIndex){0{@('Secrétariat','Comptabilité')}1{@('Mixte')}2{@('Secrétariat')}3{@('Comptabilité')}})
  $accountsGrid.SuspendLayout()
  try{
   $accountSlot1.Controls.Clear();$accountSlot2.Controls.Clear()
   foreach($v in $controls.Values){$v.Card.Visible=$false}
   $first=$controls[$roles[0]];$first.Card.Dock='Top';$first.Card.Visible=$true;$accountSlot1.Controls.Add($first.Card)
   $accountsGrid.ColumnStyles[0].SizeType='Percent';$accountsGrid.ColumnStyles[1].SizeType='Percent'
   $accountsGrid.RowStyles.Clear()
   [void]$accountsGrid.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
   $accountSlot1.MinimumSize=New-Object Drawing.Size(0,0);$accountSlot2.MinimumSize=New-Object Drawing.Size(0,0)
   $accountSlot2.Visible=($roles.Count -gt 1)
   if($roles.Count -gt 1){
    $second=$controls[$roles[1]];$second.Card.Dock='Top';$second.Card.Visible=$true;$accountSlot2.Controls.Add($second.Card)
    if($layoutState.Compact){
     $accountsGrid.RowCount=2;$accountsGrid.ColumnStyles[0].Width=100;$accountsGrid.ColumnStyles[1].Width=0
     [void]$accountsGrid.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
     $accountsGrid.SetCellPosition($accountSlot1,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,0)));$accountsGrid.SetCellPosition($accountSlot2,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,1)))
     $accountSlot1.Margin=New-Object Windows.Forms.Padding(0,0,0,10);$accountSlot2.Margin=New-Object Windows.Forms.Padding(0)
    }else{
     $accountsGrid.RowCount=1;$accountsGrid.ColumnStyles[0].Width=50;$accountsGrid.ColumnStyles[1].Width=50
     $accountsGrid.SetCellPosition($accountSlot1,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,0)));$accountsGrid.SetCellPosition($accountSlot2,(New-Object Windows.Forms.TableLayoutPanelCellPosition(1,0)))
     $accountSlot1.Margin=New-Object Windows.Forms.Padding(0,0,8,0);$accountSlot2.Margin=New-Object Windows.Forms.Padding(8,0,0,0)
    }
   }else{
    $accountsGrid.RowCount=1;$accountsGrid.ColumnStyles[0].Width=100;$accountsGrid.ColumnStyles[1].Width=0
    $accountsGrid.SetCellPosition($accountSlot1,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,0)));$accountSlot1.Margin=New-Object Windows.Forms.Padding(0)
   }
  }finally{$accountsGrid.ResumeLayout($true)}
 }.GetNewClosure()
 $mode.Add_SelectedIndexChanged($layoutAccounts);& $layoutAccounts

 # Fixed footer with a dedicated feedback row, so validation errors remain visible.
 $footer=New-Object Windows.Forms.TableLayoutPanel;$footer.Dock='Fill';$footer.ColumnCount=1;$footer.RowCount=2;$footer.Padding=New-Object Windows.Forms.Padding(24,6,24,10);$footer.Margin=New-Object Windows.Forms.Padding(0);$footer.BackColor=[Drawing.Color]::FromArgb(250,251,253)
 [void]$footer.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)));[void]$footer.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)));$shell.Controls.Add($footer,0,3)
 $feedback=New-Object Windows.Forms.TableLayoutPanel;$feedback.Dock='Top';$feedback.ColumnCount=2;$feedback.RowCount=1;$feedback.AutoSize=$true;$feedback.AutoSizeMode='GrowAndShrink';$feedback.Margin=New-Object Windows.Forms.Padding(0,0,0,6)
 [void]$feedback.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,84)));[void]$feedback.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)));$footer.Controls.Add($feedback,0,0)
 $busy=New-Object ScoliaBusySpinner;$busy.Size=New-Object Drawing.Size(24,24);$busy.Visible=$false;$busy.Dock='Top';$busy.Margin=New-Object Windows.Forms.Padding(0,1,10,0);$feedback.Controls.Add($busy,0,0)
 $progressText=New-Object Windows.Forms.Label;$progressText.Text='Prêt à configurer';$progressText.AutoSize=$true;$progressText.Dock='Top';$progressText.Font=New-Object Drawing.Font('Segoe UI',8.75);$progressText.ForeColor=[Drawing.Color]::FromArgb(100,116,139);$progressText.Margin=New-Object Windows.Forms.Padding(0);$feedback.Controls.Add($progressText,1,0)
 $actions=New-Object Windows.Forms.TableLayoutPanel;$actions.Dock='Fill';$actions.ColumnCount=4;$actions.RowCount=1;$actions.Margin=New-Object Windows.Forms.Padding(0)
 [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)))
 [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)))
 [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)))
 [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::AutoSize)))
 [void]$actions.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)));$footer.Controls.Add($actions,0,1)
 $back=New-Object ITMActionButton;$back.Text='Précédent';$back.Width=140;$back.Dock='Fill';$back.Margin=New-Object Windows.Forms.Padding(0,0,10,0);$back.Visible=$false;$actions.Controls.Add($back,0,0)
 $cancel=New-Object ITMActionButton;$cancel.Text='Annuler';$cancel.Width=130;$cancel.Dock='Fill';$cancel.Margin=New-Object Windows.Forms.Padding(0,0,10,0);$cancel.Add_Click({$d.DialogResult='Cancel';$d.Close()}.GetNewClosure());$actions.Controls.Add($cancel,2,0)
 $primaryActions=New-Object Windows.Forms.Panel;$primaryActions.Dock='Fill';$primaryActions.Width=250;$primaryActions.Margin=New-Object Windows.Forms.Padding(0);$actions.Controls.Add($primaryActions,3,0)
 $next=New-Object ITMActionButton;$next.Text='Continuer';$next.Primary=$true;$next.Dock='Fill';$next.Margin=New-Object Windows.Forms.Padding(0);$primaryActions.Controls.Add($next)
 $finish=New-Object ITMActionButton;$finish.Text='Terminer la configuration';$finish.Primary=$true;$finish.Dock='Fill';$finish.Margin=New-Object Windows.Forms.Padding(0);$finish.Visible=$false;$primaryActions.Controls.Add($finish)
 $d.CancelButton=$cancel
 $layoutFooter={
  if($d.IsDisposed -or $layoutState.FooterUpdating){return}
  $layoutState.FooterUpdating=$true
  try{
   $q=[Math]::Max(1.0,$d.CurrentAutoScaleDimensions.Height/96.0)
   $logicalWidth=$d.ClientSize.Width/$q
   $stackFooter=($logicalWidth -lt 540)
   $backWidth=[int]($(if($logicalWidth -lt 700){105}else{140})*$q)
   $cancelWidth=[int]($(if($logicalWidth -lt 700){95}else{130})*$q)
   $primaryWidth=[int]($(if($logicalWidth -lt 700){190}else{250})*$q)
   $finish.Text=$(if($logicalWidth -lt 700){'Terminer'}else{'Terminer la configuration'})
   $actions.SuspendLayout()
   try{
    $actions.Controls.Clear();$actions.ColumnStyles.Clear();$actions.RowStyles.Clear()
    if($stackFooter){
     $actions.ColumnCount=3;$actions.RowCount=2
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)))
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,[int](10*$q))))
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,50)))
     [void]$actions.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,[int](38*$q))))
     [void]$actions.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,[int](44*$q))))
     $back.Margin=New-Object Windows.Forms.Padding(0,0,0,6);$cancel.Margin=New-Object Windows.Forms.Padding(0,0,0,6)
     $actions.Controls.Add($back,0,0);$actions.Controls.Add($cancel,2,0);$actions.Controls.Add($primaryActions,0,1);$actions.SetColumnSpan($primaryActions,3)
    }else{
     $actions.ColumnCount=4;$actions.RowCount=1
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,$(if($back.Visible){$backWidth+[int](10*$q)}else{0}))))
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Percent,100)))
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,($cancelWidth+[int](10*$q)))))
     [void]$actions.ColumnStyles.Add((New-Object Windows.Forms.ColumnStyle([Windows.Forms.SizeType]::Absolute,$primaryWidth)))
     [void]$actions.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Percent,100)))
     $back.Margin=New-Object Windows.Forms.Padding(0,0,10,0);$cancel.Margin=New-Object Windows.Forms.Padding(0,0,10,0)
     $actions.Controls.Add($back,0,0);$actions.Controls.Add($cancel,2,0);$actions.Controls.Add($primaryActions,3,0);$actions.SetColumnSpan($primaryActions,1)
    }
   }finally{$actions.ResumeLayout($true)}
   $layoutState.FooterStack=$stackFooter
   $feedback.ColumnStyles[0].Width=$(if($busy.Visible){[int](36*$q)}else{0})
   $progressText.MaximumSize=New-Object Drawing.Size([Math]::Max(1,$feedback.ClientSize.Width-[int]$feedback.ColumnStyles[0].Width),0)
   $messageHeight=$progressText.GetPreferredSize((New-Object Drawing.Size([Math]::Max(1,$progressText.MaximumSize.Width),0))).Height
   $extraHeight=$(if($stackFooter){[int](44*$q)}else{0})
   $shell.RowStyles[3].Height=[Math]::Max([int](82*$q)+$extraHeight,($messageHeight+[int](58*$q)+$extraHeight))
  }finally{$layoutState.FooterUpdating=$false}
 }.GetNewClosure()
 $progressText.Add_TextChanged($layoutFooter);$feedback.Add_SizeChanged($layoutFooter)
 $layoutInlineControls={
  $q=[Math]::Max(1.0,$d.CurrentAutoScaleDimensions.Height/96.0)
  $compactInline=($dataCard.ClientSize.Width/$q -lt 460)
  # These short labels keep all three network actions on one line in normal windows.
  $pick.Text='Dossier';$join.Text='Rejoindre';$automatic.Text='Réseau';$quickAdd.Text='Ajouter'
  $quickClass.RowStyles.Clear();[void]$quickClass.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,[int](36*$q))))
  $networkActions.RowStyles.Clear();[void]$networkActions.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,[int](38*$q))))
  $quickClass.Height=[int](36*$q);$networkActions.Height=[int](38*$q)
  $quickCode.MinimumSize=New-Object Drawing.Size(0,0);$quickFee.MinimumSize=New-Object Drawing.Size(0,0)
 }.GetNewClosure()
 $styleInput={param($c)
  if($c -is [Windows.Forms.TextBox]){$c.BorderStyle='FixedSingle';$c.Font=New-Object Drawing.Font('Segoe UI',9.5);if(-not $c.Multiline){$c.AutoSize=$false;$c.Height=36}}
  elseif($c -is [Windows.Forms.ComboBox]){$c.FlatStyle='Flat';$c.Font=New-Object Drawing.Font('Segoe UI',9.5);$c.Height=36}
  elseif($c -is [Windows.Forms.NumericUpDown]){$c.BorderStyle='FixedSingle';$c.Font=New-Object Drawing.Font('Segoe UI',9.5);$c.Height=36}
 }
 foreach($c in @($name,$year,$months,$start,$classes,$root,$mode,$quickCode,$quickFee)){& $styleInput $c}
 foreach($v in $controls.Values){& $styleInput $v.Name}

 # Les handlers fermés avec GetNewClosure s'exécutent dans un module dynamique.
 # On capture donc explicitement les helpers nécessaires afin qu'ils restent disponibles
 # pendant toute la configuration, sans dépendre de la visibilité des fonctions du script.
 $parseClassText=${function:ConvertFrom-ScoliaClassText}
 $testDataRoot=${function:Test-ScoliaDataRootCandidate}
 $normalizeClassCode={param($value)$v=([string]$value).Trim().ToUpperInvariant().Replace(' ','').Replace('-','');if($v -eq 'TML1'){$v='TLM1'};return $v}

 $updateClassInfo={
  try{$parsed=@(& $parseClassText $classes.Text);$count=$parsed.Count;$classInfo.Text=$(if($count -eq 1){'✓ 1 classe valide'}else{'✓ '+$count.ToString()+' classes valides'});$classInfo.ForeColor=[Drawing.Color]::FromArgb(22,125,72)}
  catch{$classInfo.Text=$(if([string]::IsNullOrWhiteSpace($classes.Text)){'Ajoutez vos classes : CODE;montant FC — exemple INFA;25000'}else{$_.Exception.Message});$classInfo.ForeColor=$(if([string]::IsNullOrWhiteSpace($classes.Text)){[Drawing.Color]::FromArgb(100,116,139)}else{[Drawing.Color]::FromArgb(185,28,28)})}
 }.GetNewClosure()
 $classes.Add_TextChanged($updateClassInfo)
 $quickAdd.Add_Click({
  $code=& $normalizeClassCode $quickCode.Text
  if($code -notmatch '^[A-Z0-9]{4}$'){$classInfo.Text='Le code rapide doit contenir exactement 4 lettres ou chiffres.';$classInfo.ForeColor=[Drawing.Color]::FromArgb(185,28,28);$quickCode.Focus();return}
  if($quickFee.Value -le 0){$classInfo.Text='Saisissez le montant mensuel de la classe en FC.';$classInfo.ForeColor=[Drawing.Color]::FromArgb(185,28,28);$quickFee.Focus();return}
  if(@($classes.Text -split '\r?\n' | Where-Object {(& $normalizeClassCode (($_ -split ';')[0])) -eq $code}).Count){$classInfo.Text='La classe '+$code+' existe déjà.';$classInfo.ForeColor=[Drawing.Color]::FromArgb(185,28,28);$quickCode.Focus();$quickCode.SelectAll();return}
  $line=$code+';'+([decimal]$quickFee.Value).ToString('0',[Globalization.CultureInfo]::InvariantCulture)
  $classes.Text=$(if([string]::IsNullOrWhiteSpace($classes.Text)){$line}else{$classes.Text.TrimEnd()+"`r`n"+$line});$classes.SelectionStart=$classes.Text.Length;$classes.ScrollToCaret();$quickCode.Clear();$quickFee.Value=0;$quickCode.Focus()
 }.GetNewClosure())
 $updateStorageInfo={
  $value=$root.Text.Trim()
  if(-not $value){$storageInfo.Text='Choisissez un dossier de données.';$storageInfo.ForeColor=[Drawing.Color]::FromArgb(185,28,28)}
  elseif($value.StartsWith('\\')){$storageInfo.Text='Dossier réseau · les deux postes utiliseront la même configuration.';$storageInfo.ForeColor=[Drawing.Color]::FromArgb(38,91,155)}
  else{$storageInfo.Text='Dossier local · partage réseau configurable plus tard.';$storageInfo.ForeColor=[Drawing.Color]::FromArgb(100,116,139)}
 }.GetNewClosure()
 $root.Add_TextChanged($updateStorageInfo);& $updateStorageInfo

 $showStep={param([int]$index)
  $layoutState.Step=$index;$pageOne.Visible=($index -eq 0);$pageTwo.Visible=($index -eq 1)
  if($pageOne.Visible){$pageOne.BringToFront()}else{$pageTwo.BringToFront()}
  $step1.Primary=($index -eq 0);$step2.Primary=($index -eq 1);$step1.Invalidate();$step2.Invalidate()
  $back.Visible=($index -eq 1);$next.Visible=($index -eq 0);$finish.Visible=($index -eq 1);$d.AcceptButton=$(if($index -eq 0){$next}else{$finish})
  $stepStatus.Text='Étape '+($index+1).ToString()+' sur 2'
  $progressText.Text=$(if($index -eq 0){'Établissement et données'}else{'Comptes et sécurité'});$progressText.ForeColor=[Drawing.Color]::FromArgb(100,116,139)
  if($d.Tag -and $d.Tag.PSObject.Properties['ApplyLayout']){& ($d.Tag.ApplyLayout)}
 }.GetNewClosure()
 $validateStepOne={
  if([string]::IsNullOrWhiteSpace($name.Text)){throw 'Renseignez le nom de l''établissement.'}
  if($year.Text.Trim() -notmatch '^\d{4}\s*-\s*\d{4}$'){throw 'Année scolaire : utilisez le format 2026 - 2027.'}
  $years=[regex]::Matches($year.Text,'\d{4}');if($years.Count -ne 2 -or [int]$years[1].Value -ne [int]$years[0].Value+1){throw 'L''année scolaire doit couvrir deux années consécutives.'}
  $null=& $parseClassText $classes.Text
  $null=& $testDataRoot $root.Text $false
 }.GetNewClosure()
 $step1.Add_Click({& $showStep 0}.GetNewClosure())
 $step2.Add_Click({try{& $validateStepOne;& $showStep 1}catch{$progressText.Text=$_.Exception.Message;$progressText.ForeColor=[Drawing.Color]::FromArgb(185,28,28)}}.GetNewClosure())
 $back.Add_Click({& $showStep 0}.GetNewClosure())
 $next.Add_Click({try{& $validateStepOne;& $showStep 1}catch{$progressText.Text=$_.Exception.Message;$progressText.ForeColor=[Drawing.Color]::FromArgb(185,28,28)}}.GetNewClosure())

 $pick.Add_Click({$f=New-Object Windows.Forms.FolderBrowserDialog;try{if($f.ShowDialog($d) -eq 'OK'){$root.Text=$f.SelectedPath}}finally{$f.Dispose()}})
 $join.Add_Click({
  try{
   $f=New-Object Windows.Forms.OpenFileDialog;$f.Filter='Configuration SCOLIA|profil-scolia.json'
   if($f.ShowDialog($d) -eq 'OK'){$p=[IO.File]::ReadAllText($f.FileName)|ConvertFrom-Json;Set-ScoliaLocation ([IO.Path]::GetDirectoryName($f.FileName)) $p;$progressText.Text='Configuration trouvée';Show-ScoliaTransition 'Configuration trouvée. Ouverture de la connexion sécurisée...';$d.DialogResult='OK';$d.Close()}
  }catch{$progressText.Text=$_.Exception.Message;[void][ITMMessageBox]::Show($d,$_.Exception.Message,'SCOLIA')}
  finally{if($f){$f.Dispose()}}
 })
 $automatic.Add_Click({try{$r=Show-ScoliaNetwork -Setup -InitialPath $root.Text;if($r){if($r.Mode -eq 'Join'){$progressText.Text='Configuration réseau trouvée';Show-ScoliaTransition 'Configuration réseau trouvée. Ouverture de la connexion sécurisée...';$d.DialogResult='OK';$d.Close()}else{$root.Text=$r.Root}}}catch{$progressText.Text=$_.Exception.Message;[void][ITMMessageBox]::Show($d,$_.Exception.Message,'SCOLIA réseau')}})

 $finish.Add_Click({
  if(-not $finish.Enabled){return}
  $finish.Enabled=$false;$back.Enabled=$false;$cancel.Enabled=$false;$step1.Enabled=$false;$step2.Enabled=$false;$busy.Visible=$true;$busy.Start();$progressText.Text='Enregistrement…';$d.Cursor=[Windows.Forms.Cursors]::WaitCursor;$d.Refresh();[Windows.Forms.Application]::DoEvents()
  try{
   & $validateStepOne
   $roles=switch($mode.SelectedIndex){0{@('Secrétariat','Comptabilité')}1{@('Mixte')}2{@('Secrétariat')}3{@('Comptabilité')}}
   $accounts=@()
   foreach($r in $roles){
    $v=$controls[$r]
    if([string]::IsNullOrWhiteSpace($v.Name.Text)){throw ('Renseignez le nom associé au compte '+$r+'.')}
    if($v.Locked.Checked -and ($v.Password.Text.Length -lt 8 -or $v.Password.Text -cne $v.Confirm.Text)){throw ('Mot de passe de '+$r+' : minimum 8 caractères, confirmation identique.')}
    $accounts+=[pscustomobject]@{Role=$r;Name=$v.Name.Text.Trim();Id=[guid]::NewGuid().ToString('N');Locked=[bool]$v.Locked.Checked;Secret=$(if($v.Locked.Checked){New-ITMSecret $v.Password.Text}else{$null})}
   }
   $list=@(& $parseClassText $classes.Text)
   $dataRoot=& $testDataRoot $root.Text $true;$root.Text=$dataRoot
   $profile=[pscustomobject]@{Version=1;Revision=1;Id=[guid]::NewGuid().ToString('N');School=$name.Text.Trim();Year=$year.Text.Trim();Months=[int]$months.Value;StartMonth=$start.SelectedIndex+1;Classes=$list;Accounts=$accounts}
   Test-ScoliaProfile $profile
   $path=Join-Path $dataRoot 'profil-scolia.json'
   $gate=[IO.File]::Open($path+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
   try{if([IO.File]::Exists($path)){throw 'Une configuration existe déjà ici. Utilisez « Rejoindre une configuration ».'};Write-ScoliaJson $path $profile}finally{$gate.Dispose()}
   Set-ScoliaLocation $dataRoot $profile
   $progressText.Text='Configuration terminée · ouverture de la connexion…';$d.Refresh();[Windows.Forms.Application]::DoEvents()
   Show-ScoliaTransition 'Configuration enregistrée. Ouverture de la connexion sécurisée...'
   $d.DialogResult='OK';$d.Close()
   if($script:main -and -not $script:main.IsDisposed -and $script:main.Visible){Close-ScoliaTransition}
  }catch{
   $progressText.Text=$_.Exception.Message;$progressText.ForeColor=[Drawing.Color]::FromArgb(185,28,28)
   if($busy){$busy.Stop();$busy.Visible=$false}
   $d.Cursor=[Windows.Forms.Cursors]::Default;$finish.Enabled=$true;$back.Enabled=$true;$cancel.Enabled=$true;$step1.Enabled=$true;$step2.Enabled=$true
  }
 })

 # Resize changes columns only; measured card heights are never compressed.
 $applySetupResponsive={
  if($d.IsDisposed -or $layoutState.Updating){return}
  $layoutState.Updating=$true
  try{
   $q=[Math]::Max(1.0,$d.CurrentAutoScaleDimensions.Height/96.0)
   $logicalWidth=$d.ClientSize.Width/$q
   $stackSetupCards=($logicalWidth -lt 820)
   $layoutState.Compact=($logicalWidth -lt 820)
   $shell.RowStyles[0].Height=[int](80*$q);$shell.RowStyles[1].Height=[int](48*$q)
   foreach($v in $controls.Values){
    $v.Layout.RowStyles[0].Height=[int](34*$q)
    $narrowPasswords=($logicalWidth -lt 440)
    $v.Body.SuspendLayout()
    try{
     $v.Body.RowStyles.Clear();$v.Body.RowCount=$(if($narrowPasswords){7}else{5})
     for($row=0;$row -lt $v.Body.RowCount;$row++){
      $height=$(if($row -eq 1){[int](38*$q)}elseif($row -eq 4 -or $row -eq 6){[int](52*$q)}else{0})
      if($height){[void]$v.Body.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::Absolute,$height)))}else{[void]$v.Body.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))}
     }
     $v.Body.ColumnStyles[0].Width=$(if($narrowPasswords){100}else{50});$v.Body.ColumnStyles[1].Width=$(if($narrowPasswords){0}else{50})
     $v.Body.SetCellPosition($v.ConfirmLabel,(New-Object Windows.Forms.TableLayoutPanelCellPosition($(if($narrowPasswords){0}else{1}),$(if($narrowPasswords){5}else{3}))))
     $v.Body.SetCellPosition($v.Confirm,(New-Object Windows.Forms.TableLayoutPanelCellPosition($(if($narrowPasswords){0}else{1}),$(if($narrowPasswords){6}else{4}))))
     $v.Password.Margin=New-Object Windows.Forms.Padding(0,0,$(if($narrowPasswords){0}else{6}),0);$v.Confirm.Margin=New-Object Windows.Forms.Padding($(if($narrowPasswords){0}else{6}),0,0,0)
    }finally{$v.Body.ResumeLayout($true)}
   }
   if($logicalWidth -lt 540){$step1.Text='1  École';$step2.Text='2  Comptes';$stepStatus.Text=($layoutState.Step+1).ToString()+' / 2';$title.Font=New-Object Drawing.Font('Segoe UI Semibold',14,[Drawing.FontStyle]::Bold);$subtitle.Text='Deux étapes pour démarrer.'}else{$step1.Text='1   Établissement';$step2.Text='2   Comptes et sécurité';$stepStatus.Text='Étape '+($layoutState.Step+1).ToString()+' sur 2';$title.Font=New-Object Drawing.Font('Segoe UI Semibold',18,[Drawing.FontStyle]::Bold);$subtitle.Text='Établissement et accès, en deux étapes.'}
   $p1Title.Text=$(if($logicalWidth -lt 540){'Établissement'}else{'Établissement et année scolaire'})
   $header.ColumnStyles[0].Width=[int](64*$q)
   $header.ColumnStyles[2].Width=$(if($logicalWidth -ge 1000){[int](164*$q)}else{0});$badge.Visible=($logicalWidth -ge 1000)
   $pageOneBody.SuspendLayout()
   try{
    $pageOneBody.RowStyles.Clear();[void]$pageOneBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
    $pageOneBody.ColumnStyles[0].SizeType='Percent';$pageOneBody.ColumnStyles[1].SizeType='Percent'
    if($stackSetupCards){
     $pageOneBody.RowCount=2;$pageOneBody.ColumnStyles[0].Width=100;$pageOneBody.ColumnStyles[1].Width=0
     [void]$pageOneBody.RowStyles.Add((New-Object Windows.Forms.RowStyle([Windows.Forms.SizeType]::AutoSize)))
     $pageOneBody.SetCellPosition($basicCard,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,0)));$pageOneBody.SetCellPosition($dataCard,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,1)))
     $basicCard.Margin=New-Object Windows.Forms.Padding(0,0,0,10);$dataCard.Margin=New-Object Windows.Forms.Padding(0)
    }else{
     $pageOneBody.RowCount=1;$pageOneBody.ColumnStyles[0].Width=42;$pageOneBody.ColumnStyles[1].Width=58
     $pageOneBody.SetCellPosition($basicCard,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,0)));$pageOneBody.SetCellPosition($dataCard,(New-Object Windows.Forms.TableLayoutPanelCellPosition(1,0)))
     $basicCard.Margin=New-Object Windows.Forms.Padding(0,0,10,0);$dataCard.Margin=New-Object Windows.Forms.Padding(10,0,0,0)
    }
   }finally{$pageOneBody.ResumeLayout($true)}
   & $layoutAccounts
   & $layoutInlineControls
   & $layoutFooter
  }finally{$layoutState.Updating=$false}
 }.GetNewClosure()
 $d.Add_Resize($applySetupResponsive)
 $d.Add_Shown($applySetupResponsive)

 $tip=New-Object Windows.Forms.ToolTip;$tip.AutoPopDelay=12000
 $tip.SetToolTip($quickCode,'Code unique : 4 lettres ou chiffres.');$tip.SetToolTip($quickFee,'Montant mensuel en francs congolais (FC).');$tip.SetToolTip($pick,'Choisir le dossier de données');$tip.SetToolTip($join,'Rejoindre une configuration existante');$tip.SetToolTip($automatic,'Ouvrir l assistant réseau')
 $d.Add_Disposed({param($sender,$e)if($sender.Tag -and $sender.Tag.PSObject.Properties['ToolTip']){$sender.Tag.ToolTip.Dispose()}})
 $d.Tag=[pscustomobject]@{ToolTip=$tip;ApplyLayout=$applySetupResponsive}
 & $showStep 0
 & $applySetupResponsive
 Apply-ITMThemeForm $d
 if($LayoutOnly){
  return [pscustomobject]@{Form=$d;Shell=$shell;Header=$header;Steps=$steps;Footer=$footer;Feedback=$progressText;ClassInfo=$classInfo;StorageInfo=$storageInfo;ContentHost=$contentHost;PageOne=$pageOne;PageTwo=$pageTwo;PageOneBody=$pageOneBody;PageTwoBody=$pageTwoBody;BasicCard=$basicCard;DataCard=$dataCard;Organisation=$mode;Accounts=$controls;AccountsGrid=$accountsGrid;Next=$next;Back=$back;Finish=$finish;Cancel=$cancel;ApplyLayout=$applySetupResponsive;Name=$name;Year=$year;Classes=$classes;Root=$root;QuickCode=$quickCode;QuickFee=$quickFee;QuickAdd=$quickAdd;Pick=$pick;Join=$join;Network=$automatic;Busy=$busy}
 }
 try{return ($d.ShowDialog() -eq 'OK')}
 finally{
  foreach($v in $controls.Values){$v.Password.Clear();$v.Confirm.Clear();if($v.PSObject.Properties['Current']){$v.Current.Clear()}}
  if($busy){$busy.Stop()};$d.Dispose()
 }
}

function Get-ScoliaAttemptFile([string]$role){
 $code=switch($role){'Secrétariat'{'secretariat'};'Comptabilité'{'comptabilite'};'Mixte'{'mixte'};default{if($role -match '^[a-f0-9]{32}$'){$role}else{throw 'Compte métier inconnu.'}}}
 return (Join-Path ([IO.Path]::GetDirectoryName($script:scoliaPointer)) ('tentatives-scolia-'+$script:scolia.Id+'-'+$code+'.json'))
}
function Get-ScoliaAttemptUntil($timestamp){
 if($timestamp -is [datetime]){return $timestamp.ToUniversalTime()}
 return [datetime]::Parse([string]$timestamp,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind).ToUniversalTime()
}
function Connect-ScoliaAccount([string]$role,[string]$password){
 if(-not (Read-ScoliaConfiguration)){throw 'Configuration absente.'};$a=@($script:scolia.Accounts|Where-Object {(Get-ScoliaAccountId $_) -ceq $role});if(-not $a.Count){$a=@($script:scolia.Accounts|Where-Object {$_.Role -ceq $role})};if($a.Count -ne 1 -or -not (Test-ScoliaAccountActive $a[0])){throw 'Compte inconnu, ambigu ou désactivé.'}
 if($a[0].Locked -and [string]::IsNullOrWhiteSpace($password)){throw 'Renseignez le mot de passe.'}
 $attemptFile=Get-ScoliaAttemptFile (Get-ScoliaAccountId $a[0]);$attempt=[pscustomobject]@{Count=0;Until=''};if([IO.File]::Exists($attemptFile)){$attempt=[IO.File]::ReadAllText($attemptFile)|ConvertFrom-Json}
 if($a[0].Locked -and $attempt.Until -and [datetime]::UtcNow -lt (Get-ScoliaAttemptUntil $attempt.Until)){throw 'Trop de tentatives. Réessayez dans cinq minutes.'}
 if($a[0].Locked -and -not (Test-ITMSecret $password $a[0].Secret)){$attempt.Count++;if($attempt.Count -ge 5){$attempt.Until=[datetime]::UtcNow.AddMinutes(5).ToString('o');$attempt.Count=0};Write-ScoliaJson $attemptFile $attempt;throw 'Mot de passe incorrect.'}
 Write-ScoliaJson $attemptFile ([pscustomobject]@{Count=0;Until=''})
 $script:scoliaSession=[pscustomobject]@{Role=$a[0].Role;Name=(Get-ScoliaAccountName $a[0]);AccountId=(Get-ScoliaAccountId $a[0]);Revision=$script:scolia.Revision;SchoolId=$script:scolia.Id};$script:accountSession=[pscustomobject]@{Kind='SCOLIA';Name=(Get-ScoliaAccountName $a[0]);Id=(Get-ScoliaAccountId $a[0]);Role=$a[0].Role}
 if($script:security){$script:security.Enabled=[bool]$a[0].Locked};if($script:main){Set-ScoliaWorkspace};Write-ITMUserAudit 'Connexion SCOLIA' $role
 if(Get-Command Set-ITMLocationPreference -ErrorAction SilentlyContinue){[void](Set-ITMLocationPreference 'DerniereConnexionUtc' ([DateTime]::UtcNow.ToString('o',[Globalization.CultureInfo]::InvariantCulture)))}
}
function Show-ScoliaLogin {
 if(-not (Read-ScoliaConfiguration)){throw 'Configuration absente.'}
 $resumeMonitor=($script:scoliaTimer -and $script:scoliaTimer.Enabled);if($resumeMonitor){$script:scoliaTimer.Stop()}
 $d=New-Object ScoliaConnectionForm;Set-ITMWindowIcon $d
 $d.Add_Shown({Close-ScoliaTransition})
 $logoImage=$null;$logoPath=Join-Path $PSScriptRoot 'Assets\SCOLIA.png'
 if(Test-Path -LiteralPath $logoPath){try{$logoImage=[Drawing.Image]::FromFile($logoPath)}catch{}}
 $d.Configure($logoImage,[string]$script:scolia.School)
 $account=$d.Account;$password=$d.Password;$message=$d.MessageLabel;$login=$d.Submit
 $account.Items.AddRange([object[]]@($script:scolia.Accounts|ForEach-Object {Get-ScoliaAccountLabel $_}))
 $login.Add_Click({
  if(-not $login.Enabled){return}
  if($account.SelectedIndex -lt 0){$message.Text='Sélectionnez un compte.';return}
  $d.SetBusy($true);$message.Text='';$d.Refresh()
  try{
   # Premier rendu avant tout accès disque/réseau ou vérification du mot de passe.
   Show-ScoliaTransition 'Vérification de votre compte…'
   Connect-ScoliaAccount (Get-ScoliaAccountId $script:scolia.Accounts[$account.SelectedIndex]) $password.Text
   Show-ScoliaTransition 'Connexion réussie. Ouverture de votre espace de travail…'
   $d.DialogResult='OK';$d.Close()
   # Lors d’un déverrouillage ou changement de compte, l’accueil est déjà affiché.
   if($script:main -and -not $script:main.IsDisposed -and $script:main.Visible){Close-ScoliaTransition}
  }catch{
   Close-ScoliaTransition
   $failure=$_.Exception.Message;$password.Clear();& $refreshAccounts;$message.Text=$failure;$password.Focus()
  }finally{
   $d.SetBusy($false);if($message.Text){$password.Focus()};$message.AccessibleDescription=$message.Text
  }
 })
 $d.AcceptButton=$login;$account.Add_SelectedIndexChanged({if($account.SelectedIndex -lt 0){return};$a=$script:scolia.Accounts[$account.SelectedIndex];$d.RequirePassword([bool]$a.Locked);if($a.Locked){$password.Focus()}})
 $refreshAccounts={$selected=$account.Text;$account.Items.Clear();$account.Items.AddRange([object[]]@($script:scolia.Accounts|ForEach-Object {Get-ScoliaAccountLabel $_}));$index=$account.Items.IndexOf($selected);$account.SelectedIndex=$(if($index -ge 0){$index}else{0})}; & $refreshAccounts
 Apply-ITMThemeForm $d;try{return ($d.ShowDialog() -eq 'OK')}finally{$password.Clear();$d.Dispose();if($resumeMonitor){$script:scoliaTimer.Start()}}
}
function Test-ScoliaPermission([string]$permission){
 if(-not $script:scoliaSession){return $false};$r=$script:scoliaSession.Role
 $own=@($script:scolia.Accounts|Where-Object {(Get-ScoliaAccountId $_) -ceq $script:scoliaSession.AccountId})
 if($own.Count -ne 1 -or -not (Test-ScoliaAccountActive $own[0])){return $false};$admin=Test-ScoliaAccountAdmin $own[0]
 if($permission -in @('Administrer','GererComptes')){return $admin}
 if($permission -eq 'CorrigerPaiements'){return ($admin -or ($own[0].PSObject.Properties['CanCorrect'] -and $own[0].CanCorrect))}
 if($permission -eq 'GererFrais'){return ($admin -or ($own[0].PSObject.Properties['CanManageFees'] -and $own[0].CanManageFees))}
 if($permission -eq 'Encaisser'){return $r -in @('Secrétariat','Comptabilité','Mixte')}
 if($permission -eq 'Recevoir'){return $r -in @('Secrétariat','Mixte')}
 if($r -eq 'Mixte'){return $true}
 if($r -eq 'Comptabilité'){return $permission -in @('Lire','Exporter','Sauvegarder','Diagnostic','Administrer','Comptabilite')}
 return $permission -in @('Lire','Saisie','Exporter','Sauvegarder','Diagnostic','Administrer','Recevoir','Scolarite')
}
function New-ScoliaSettingsCandidate($baseline,[string]$school,[string]$year,[int]$months,[int]$start,[string]$classes,$controls){
 # Every save attempt starts from the original profile; failed validation cannot alter it.
 $candidate=$baseline|ConvertTo-Json -Depth 12|ConvertFrom-Json
 $candidate.School=$school.Trim();$candidate.Year=$year.Trim();$candidate.Months=$months;$candidate.StartMonth=$start;$candidate.Classes=@(ConvertFrom-ScoliaClassText $classes)
 foreach($a in $candidate.Accounts){
  $v=$controls[(Get-ScoliaAccountId $a)];if(-not $v){$v=$controls[$a.Role]};if($v -and $v.PSObject.Properties['Name']){$a|Add-Member -NotePropertyName Name -NotePropertyValue $v.Name.Text.Trim() -Force};if(-not $v){throw 'Contrôles de compte incomplets.'}
  if($a.Locked -and (-not $v.Locked.Checked -or $v.Password.Text) -and -not (Test-ITMSecret $v.Current.Text $a.Secret)){throw ('Le mot de passe actuel du compte '+$a.Role+' est requis pour modifier sa protection.')}
  $a.Locked=[bool]$v.Locked.Checked
  if(-not $a.Locked){$a.Secret=$null}elseif($v.Password.Text){if($v.Password.Text.Length -lt 8 -or $v.Password.Text -cne $v.Confirm.Text){throw 'Nouveau mot de passe trop court ou confirmations différentes.'};$a.Secret=New-ITMSecret $v.Password.Text}elseif(-not $a.Secret){throw ('Définissez le mot de passe du compte '+$a.Role)}
 };Test-ScoliaProfile $candidate;return $candidate
}
function Assert-ScoliaTariffChange($current,$candidate,$entries,$roster){
 $year=[ScoliaSharing]::YearKey($current.Year)
 $hasPayments=(@($entries|Where-Object {([ScoliaSharing]::YearKey($_.Annee)) -eq $year}).Count -gt 0 -or @($roster.Learners|Where-Object {@($_.Paid|Where-Object {$_.Montant -gt 0}).Count -or @($_.Covered|Where-Object {$_.Montant -gt 0}).Count}).Count -gt 0)
 if($hasPayments -and ([ScoliaSharing]::YearKey($candidate.Year)) -eq $year -and (($candidate.Classes|ConvertTo-Json -Compress) -cne ($current.Classes|ConvertTo-Json -Compress) -or $candidate.Months -ne $current.Months -or $candidate.StartMonth -ne $current.StartMonth)){throw 'Cette année comporte des écritures ou prises en charge : conservez son calendrier et ses tarifs ; configurez la nouvelle année.'}
}
function Show-ScoliaSettings {
 Assert-ITMPermission 'Administrer';[void](Read-ScoliaConfiguration);$before=$script:scolia|ConvertTo-Json -Depth 12;$editing=$before|ConvertFrom-Json
 $d=New-ScoliaDialog 'Paramètres de lʼétablissement et des comptes' 820 740;$t=New-ScoliaTable $d
 $school=Add-ScoliaText $t 'Nom de lʼécole' $editing.School;$year=Add-ScoliaText $t 'Année scolaire' $editing.Year
 $months=New-Object Windows.Forms.NumericUpDown;$months.Minimum=1;$months.Maximum=12;$months.Value=$editing.Months;[void](Add-ScoliaField $t 'Nombre de mensualités' $months)
 $start=New-Object Windows.Forms.ComboBox;$start.DropDownStyle='DropDownList';$start.Items.AddRange([object[]][ScoliaLedger]::Months);$start.SelectedIndex=$editing.StartMonth-1;[void](Add-ScoliaField $t 'Premier mois scolaire' $start)
 $classes=New-Object Windows.Forms.TextBox;$classes.Multiline=$true;$classes.Height=120;$classes.Text=(@($editing.Classes|ForEach-Object {$_.Code+';'+([decimal]$_.Fee).ToString('0',[Globalization.CultureInfo]::InvariantCulture)}) -join "`r`n");[void](Add-ScoliaField $t 'Classes (code de 4 caractères) : CODE;FC/mois' $classes)
 $controls=@{};foreach($account in $editing.Accounts){$person=Add-ScoliaText $t ('Nom de la personne · '+$account.Role) (Get-ScoliaAccountName $account);$lock=New-Object ITMReadableCheckBox;$lock.Text='Compte protégé par mot de passe';$lock.AutoSize=$true;$lock.Checked=$account.Locked;[void](Add-ScoliaField $t $account.Role $lock);$pass=New-Object ScoliaPasswordBox;[void](Add-ScoliaField $t 'Nouveau mot de passe (vide : conserver)' $pass);$confirm=New-Object ScoliaPasswordBox;[void](Add-ScoliaField $t 'Confirmer le nouveau mot de passe' $confirm);$current=New-Object ScoliaPasswordBox;[void](Add-ScoliaField $t 'Mot de passe actuel (pour modifier la protection)' $current);$current.Enabled=$account.Locked;$controls[(Get-ScoliaAccountId $account)]=[pscustomobject]@{Name=$person;Locked=$lock;Password=$pass;Confirm=$confirm;Current=$current}}
 $message=New-Label $t 'Les tarifs dʼune année ayant des écritures sont protégés. Le dossier partagé contient la même configuration pour les deux postes.' 0 0 600 60;$t.RowCount++;$t.SetCellPosition($message,(New-Object Windows.Forms.TableLayoutPanelCellPosition(0,($t.RowCount-1))));$t.SetColumnSpan($message,2);$message.Dock='Top'
 $bar=New-Object Windows.Forms.FlowLayoutPanel;$bar.Dock='Bottom';$bar.AutoSize=$true;$bar.WrapContents=$true;$d.Controls.Add($bar)
 [void](Add-ScoliaButton $bar 'Motifs / catégories de paiement' {try{Show-ScoliaFees}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Comptes nominatifs' {try{Show-ScoliaAccounts}catch{App-Error $_}})
 [void](Add-ScoliaButton $bar 'Enregistrer les paramètres' {try{
  Assert-ITMPermission 'Administrer';$candidate=New-ScoliaSettingsCandidate $editing $school.Text $year.Text ([int]$months.Value) ($start.SelectedIndex+1) $classes.Text $controls;$gate=$null;$journalGate=$null;$gate=[IO.File]::Open($script:scoliaProfileFile+'.lock',[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
  try{$current=[IO.File]::ReadAllText($script:scoliaProfileFile)|ConvertFrom-Json;if($current.Revision -ne $candidate.Revision){throw 'Configuration modifiée sur un autre poste. Rouvrez les paramètres.'}
   $journalGate=[IO.File]::Open((Join-Path $script:scoliaRoot 'journal.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
   $all=[ScoliaLedger]::Read($script:scoliaRoot,$script:scolia.Id)
   $roster=[ScoliaSharing]::ReadRoster($script:scoliaRoot,$script:scolia.Id,$current.Year)
   Assert-ScoliaTariffChange $current $candidate $all $roster
   $candidate.Revision++;Write-ScoliaJson $script:scoliaProfileFile $candidate
  }finally{if($journalGate){$journalGate.Dispose()};if($gate){$gate.Dispose()}}
  $script:scolia=$candidate;$script:scoliaSession.Revision=$candidate.Revision;if($script:security){$own=@($candidate.Accounts|Where-Object {(Get-ScoliaAccountId $_) -ceq $script:scoliaSession.AccountId})[0];$script:security.Enabled=[bool]$own.Locked};$own=@($candidate.Accounts|Where-Object {(Get-ScoliaAccountId $_) -ceq $script:scoliaSession.AccountId})[0];$script:scoliaSession.Name=Get-ScoliaAccountName $own;$script:accountSession.Name=$script:scoliaSession.Name;Apply-ITMInstitution;Set-ScoliaWorkspace;Start-ScoliaNetworkHost;if($script:book -and $script:data -and -not @($script:data.payments).Count -and ([ScoliaSharing]::YearKey($candidate.Year)) -eq ([ScoliaSharing]::YearKey($current.Year))){Do-Work {Apply-ScoliaWorkbookConfiguration} 'Mise à jour de la base scolaire sans paiements...'};Write-ITMUserAudit 'Paramètres SCOLIA modifiés';$d.Close()
 }catch{$message.Text=$_.Exception.Message}})
 [void](Add-ScoliaButton $bar 'Fermer' {$d.Close()});Apply-ITMThemeForm $d;try{[void]$d.ShowDialog($script:main)}finally{foreach($v in $controls.Values){$v.Password.Clear();$v.Confirm.Clear();if($v.PSObject.Properties['Current']){$v.Current.Clear()}};$d.Dispose()}
}
