$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $PSScriptRoot 'PaiementsFiabiliteV61.ps1')
$c=New-Object Windows.Forms.ComboBox
$n=New-Object Windows.Forms.NumericUpDown
try{
 $c.DropDownStyle='DropDownList';$c.AutoCompleteSource='ListItems';$c.AutoCompleteMode='SuggestAppend';[void]$c.Items.Add('Apprenant de test')
 if($c.AutoCompleteMode -ne 'SuggestAppend'){throw 'Autocomplete not active'}
 $n.Maximum=10000;$n.Minimum=0;$n.Value=0;$state=@{Amount=$null;Events=0}
 $n.Add_TextChanged({$state.Events++;$state.Amount=Get-ScoliaTypedAmount $n.Text $n.Maximum})
 $n.Text='1500'
 if($state.Events -lt 1 -or $state.Amount -ne 1500){throw 'TextChanged fails to validate without leaving the field'}
 'PASS: real WinForms ComboBox initialization and immediate amount edit events.'
}finally{$c.Dispose();$n.Dispose()}
