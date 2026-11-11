$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot 'CompilationCache.ps1')
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ChromeModerne.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
Import-ITMCompiledSource -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
$spinner=New-Object ScoliaBusySpinner
$progress=New-Object ScoliaProgressBar
try{
    if($spinner.Width -lt 20 -or $spinner.Height -lt 20){throw 'Dimensions du spinner invalides.'}
    $spinner.Start();$spinner.Stop()
    $progress.Value=0;$progress.Start();$progress.Value=55;$progress.Stop();$progress.Value=100
    if($progress.Value -ne 100){throw 'Progression invalide.'}
    if(-not ('ITMActivity' -as [type])){throw 'Indicateur global indisponible.'}
    'OK - indicateurs SCOLIA V40 compilés et instanciés.'
}finally{
    $spinner.Dispose();$progress.Dispose()
}
