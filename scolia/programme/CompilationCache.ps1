# Reuse compiled components; cache keys include source, references, CLR and process architecture.
function Import-ITMCompiledSource {
    param([Parameter(Mandatory=$true)][string]$Path,[string[]]$ReferencedAssemblies=@())
    $compile=@{Path=$Path;ErrorAction='Stop';IgnoreWarnings=$true};if($ReferencedAssemblies.Count){$compile.ReferencedAssemblies=$ReferencedAssemblies}
    $sourceHash=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    $identity='ITM-cache-1|'+$sourceHash+'|'+[Environment]::Version.ToString()+'|'+$PSVersionTable.PSVersion.ToString()+'|'+[Environment]::Is64BitProcess+'|'+($ReferencedAssemblies -join '|')
    $sha=[Security.Cryptography.SHA256]::Create();try{$key=([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity)))).Replace('-','')}finally{$sha.Dispose()}
    $base=[Environment]::GetFolderPath('LocalApplicationData');if(-not $base){$base=[IO.Path]::GetTempPath()}
    $dir=Join-Path $base 'ITMCD/cache/compilation'
    try{[void][IO.Directory]::CreateDirectory($dir)}catch{Add-Type @compile;return}
    $dll=Join-Path $dir ($key+'.dll');$stamp=Join-Path $dir ($key+'.sha256');$temp='';$gate=$null;$acquired=$false
    try{
        $gate=New-Object Threading.Mutex($false,('ITMCD_Compilation_'+$key))
        try{$acquired=$gate.WaitOne(15000)}catch [Threading.AbandonedMutexException]{$acquired=$true}
        if(-not $acquired){Add-Type @compile;return}
        if([IO.File]::Exists($dll) -and [IO.File]::Exists($stamp)){
            try{
                $expected=[IO.File]::ReadAllText($stamp).Trim()
                if($expected -eq (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash){Add-Type -Path $dll -ErrorAction Stop;return}
            }catch{}
        }
        $temp=Join-Path $dir ([Guid]::NewGuid().ToString('N')+'.pending.dll')
        try{Add-Type @compile -OutputAssembly $temp -OutputType Library}catch{Add-Type @compile;return}
        if($sourceHash -ne (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash){throw 'Le programme a changé pendant la compilation. Relancez après la fin de la mise à jour.'}
        # Publish only a complete binary, then record its digest; other launches hold the mutex.
        $load=$temp
        try{
            [IO.File]::Copy($temp,$dll,$true)
            [IO.File]::WriteAllText($stamp,(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash,[Text.Encoding]::ASCII)
            $load=$dll
        }catch{}
        Add-Type -Path $load -ErrorAction Stop
    }finally{
        if($temp -and [IO.File]::Exists($temp)){try{[IO.File]::Delete($temp)}catch{}}
        if($gate){if($acquired){$gate.ReleaseMutex()};$gate.Dispose()}
    }
}
