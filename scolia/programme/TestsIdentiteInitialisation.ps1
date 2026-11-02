# Windows PowerShell 5.1 : runs actual property functions on strict CLR doubles.
# No Excel, user files or registry modification.
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'StockageClasseurs.ps1')
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
public sealed class ITMIdentity85Property { public string Name {get;set;} public object Value {get;set;} }
public sealed class ITMIdentity85Collection {
    private List<ITMIdentity85Property> list=new List<ITMIdentity85Property>();
    public int Count {get{return list.Count;}}
    public ITMIdentity85Property this[int i] {get{return list[i-1];}}
    public int Adds; public bool Fail;
    public object Add(string name,bool linked,int kind,object value,[System.Runtime.InteropServices.Optional] object source) {
        Adds++;
        if(source!=Type.Missing||linked)throw new InvalidOperationException("Optional argument not explicit");
        if(Fail)throw new NullReferenceException("COM failure simulation");
        var item=new ITMIdentity85Property{Name=name,Value=value};list.Add(item);return item;
    }
}
public sealed class ITMIdentity85Book {public ITMIdentity85Collection CustomDocumentProperties {get;set;} public ITMIdentity85Book(){CustomDocumentProperties=new ITMIdentity85Collection();}}
'@
$book=New-Object ITMIdentity85Book
if((Get-ITMBookProperty $book 'ITM_DataSet') -ne ''){throw 'Absent identity not empty.'}
Set-ITMBookProperty $book 'ITM_DataSet' 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'
Set-ITMBookProperty $book 'ITM_SegmentCount' ([int]2)
if($book.CustomDocumentProperties.Adds -ne 2){throw 'Unexpected number of writes.'}
if((Get-ITMBookProperty $book 'ITM_DataSet') -ne 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'){throw 'Identity not persisted.'}
Set-ITMBookProperty $book 'ITM_SegmentCount' ([int]3)
if((Get-ITMBookProperty $book 'ITM_SegmentCount') -ne '3' -or $book.CustomDocumentProperties.Adds -ne 2){throw 'Existing property recreated instead of updated.'}
$broken=New-Object ITMIdentity85Book;$broken.CustomDocumentProperties.Fail=$true
$failed=$false
try{Set-ITMBookProperty $broken 'ITM_DataSet' 'test'}catch{
    $failed=$true
    if($_.Exception.Message -notmatch 'Add'){throw 'Missing contextual operation.'}
}
if(-not $failed -or $broken.CustomDocumentProperties.Adds -ne 1){throw 'Write failure hidden or retried.'}
$book.CustomDocumentProperties=$null
$failed=$false
try{Set-ITMBookProperty $book 'ITM_DataSet' 'test'}catch{$failed=$true}
if(-not $failed){throw 'Absent property collection not rejected.'}
Write-Output 'PASS : creation/update/read identity; five explicit Add slots; failure not retried; null collection guarded.'
