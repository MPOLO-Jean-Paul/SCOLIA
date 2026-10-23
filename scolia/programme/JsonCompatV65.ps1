# Le chargement JSON conserve les chaînes d'horodatage et leurs décalages exacts.
function ConvertFrom-ScoliaJsonToken($Token){
 switch([string]($Token.get_Type())){
  'Object'{
   $values=[ordered]@{}
   foreach($property in $Token.Properties()){
    if($values.Contains([string]$property.Name)){throw 'Propriétés JSON dupliquées ou ambiguës.'}
    $values[[string]$property.Name]=ConvertFrom-ScoliaJsonToken $property.get_Value()
   }
   return [pscustomobject]$values
  }
  'Array'{
   $items=New-Object 'System.Collections.Generic.List[object]'
   foreach($child in $Token.Children()){$items.Add((ConvertFrom-ScoliaJsonToken $child))}
   return ,($items.ToArray())
  }
  'String'{return $Token.ToString()}
  'Null'{return $null}
  'Boolean'{return [bool]($Token.get_Value())}
  'Integer'{return $Token.get_Value()}
  'Float'{return $Token.get_Value()}
  default{throw ('Type JSON non pris en charge : '+$Token.get_Type())}
 }
}
function ConvertFrom-ScoliaJson {
 [CmdletBinding()]
 param([Parameter(Mandatory=$true,ValueFromPipeline=$true)][Alias('InputObject')][string]$Json)
 process{
  # Windows PowerShell 5.1 est le moteur natif de SCOLIA et conserve ces chaînes.
  if($PSVersionTable.PSVersion.Major -le 5){return (ConvertFrom-Json -InputObject $Json -ErrorAction Stop)}
  $command=Get-Command ConvertFrom-Json
  if($command.Parameters.ContainsKey('DateKind')){return (ConvertFrom-Json -InputObject $Json -DateKind String -ErrorAction Stop)}
  if(-not ('Newtonsoft.Json.JsonTextReader' -as [type])){Add-Type -Path (Join-Path $PSHOME 'Newtonsoft.Json.dll') -ErrorAction Stop}
  $inputReader=New-Object IO.StringReader $Json;$reader=New-Object Newtonsoft.Json.JsonTextReader $inputReader
  try{
   $reader.DateParseHandling=[Newtonsoft.Json.DateParseHandling]::None
   $token=[Newtonsoft.Json.Linq.JToken]::ReadFrom($reader)
   while($reader.Read()){if($reader.TokenType -ne [Newtonsoft.Json.JsonToken]::Comment){throw 'Contenu supplémentaire après le document JSON.'}}
   return ,(ConvertFrom-ScoliaJsonToken $token)
  }finally{$reader.Close();$inputReader.Dispose()}
 }
}
