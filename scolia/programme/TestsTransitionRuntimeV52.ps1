$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Path (Join-Path $PSScriptRoot 'ActivityIndicator.cs') -ReferencedAssemblies System.Windows.Forms,System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Windows.Forms;
public static class V52CardReader {
    public static string Read(Control card) {
        return (string)card.Invoke(new Func<string>(delegate {
            return (string)card.GetType().GetProperty("Message").GetValue(card,null);
        }));
    }
}
'@ -ReferencedAssemblies System.Windows.Forms,System.Drawing
$area=[Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$rect=New-Object Drawing.Rectangle(($area.Left+40),($area.Top+40),480,112)
$visual=New-Object ITMActivity('Étape initiale',$rect,[IntPtr]::Zero,'',$false,$true)
try{
    $visual.Update('Configuration enregistrée. Ouverture de la connexion sécurisée...')
    if(-not $visual.WaitUntilVisible(3000)){throw 'Le premier rendu de la transition n''a pas été confirmé.'}
    # Lire la carte uniquement sur son thread : vérifie aussi une Update précoce.
    $field=[ITMActivity].GetField('card',[Reflection.BindingFlags]'NonPublic,Instance')
    $card=$field.GetValue($visual)
    $actual=[V52CardReader]::Read($card)
    if($actual -notlike 'Configuration enregistrée*'){throw 'Message perdu avant création du handle.'}
}finally{$visual.Dispose()}
$stopped=New-Object ITMActivity('Test fermeture immédiate',$rect,[IntPtr]::Zero,'',$false,$true)
$stopped.Dispose()
if($stopped.WaitUntilVisible(100)){throw 'Une transition fermée est annoncée visible.'}
$stopped.Dispose()
'PASS : premier rendu, mise à jour précoce, arrêt immédiat et fermeture répétée.'
