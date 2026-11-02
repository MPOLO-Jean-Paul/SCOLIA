# Parse the text being edited, never a stale NumericUpDown.Value.
function Get-ScoliaTypedAmount([string]$Text,[decimal]$Maximum=100000000000,[Globalization.CultureInfo]$Culture=[Globalization.CultureInfo]::CurrentCulture){
    $raw=$Text.Trim()
    if(-not $raw){return $null}
    $group=$Culture.NumberFormat.NumberGroupSeparator
    if([string]::IsNullOrWhiteSpace($group)){$raw=$raw -replace '[\u0020\u00a0\u202f]', $group}
    $g=[regex]::Escape($group);$d=[regex]::Escape($Culture.NumberFormat.NumberDecimalSeparator)
    $whole='[0-9]+'
    if($group){$whole='(?:[0-9]+|[0-9]{1,3}(?:'+$g+'[0-9]{3})+)'}
    if($raw -notmatch ('^'+$whole+'(?:'+$d+'0+)?$')){return $null}
    $value=[decimal]0
    if(-not [decimal]::TryParse($raw,[Globalization.NumberStyles]::Number,$Culture,[ref]$value)){return $null}
    if($value -lt 0 -or $value -gt $Maximum -or [decimal]::Truncate($value) -ne $value){return $null}
    return $value
}

# Match the complete path, never just a workbook name.
function Find-ScoliaOpenWorkbook($Application,[string]$Path){
    $expected=[IO.Path]::GetFullPath($Path)
    $books=Get-ITMExcelProperty $Application 'Workbooks'
    $count=[int](Get-ITMExcelProperty $books 'Count')
    for($i=1;$i -le $count;$i++){
        $candidate=Get-ITMExcelProperty $books 'Item' @($i)
        $actual=[string](Get-ITMExcelProperty $candidate 'FullName')
        if($actual -and [IO.Path]::GetFullPath($actual).Equals($expected,[StringComparison]::OrdinalIgnoreCase)){return $candidate}
    }
    return $null
}
function Restore-ScoliaBorrowedExcelSettings {
    if(-not $script:excelBorrowed -or -not $script:excel -or -not $script:excelOriginalSettings){return}
    foreach($name in $script:excelOriginalSettings.Keys){Set-ITMExcelProperty $script:excel $name $script:excelOriginalSettings[$name]}
}
