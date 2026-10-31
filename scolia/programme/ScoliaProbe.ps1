param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference='Stop'
try{
 $request=[IO.File]::ReadAllText($RequestPath)|ConvertFrom-Json
 if($request.Nonce -notmatch '^[a-f0-9]{32}$'){throw 'Identité de vérification invalide.'}
 Add-Type -Path (Join-Path $PSScriptRoot 'ScoliaNetwork.cs') -ReferencedAssemblies System,System.Xml,System.Core,System.Runtime.Serialization -IgnoreWarnings -ErrorAction Stop
 $probe=[ScoliaNetwork]::Probe([string]$request.Root)
 $result=[pscustomobject]@{Nonce=$request.Nonce;Success=$true;Root=$probe.Root;Profile=$probe.Profile;Error=''}
}catch{$result=[pscustomobject]@{Nonce=$(if($request){$request.Nonce}else{''});Success=$false;Error=$_.Exception.Message}}
[IO.File]::WriteAllText(($RequestPath+'.result'),($result|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
