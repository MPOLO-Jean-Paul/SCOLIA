using System.Reflection;using System.Globalization;
using System;using System.Collections.Generic;using System.Runtime.InteropServices;
// Typed facade keeps PowerShell away from the Office COM adapter/type-info issue.
public sealed class ITMGradeExcel : IDisposable {
 public object Native {get;private set;} private List<object> owned;private bool root;
 private ITMGradeExcel(object value,List<object> resources,bool isRoot){Native=value;owned=resources;root=isRoot;owned.Add(value);}
 public static ITMGradeExcel Create(){object app=Activator.CreateInstance(Type.GetTypeFromProgID("Excel.Application",true));return new ITMGradeExcel(app,new List<object>(),true);}
 private object Raw(object value){var proxy=value as ITMGradeExcel;return proxy==null?ITMGradeDispatch.Unwrap(value):proxy.Native;}
 private object Read(string name,params object[] args){return ITMGradeDispatch.Get(Native,name,args);}
 private ITMGradeExcel Wrap(object value){return new ITMGradeExcel(value,owned,false);}
 private void Write(string name,object value){ITMGradeDispatch.Set(Native,name,Raw(value));}
 private object Call(string name,params object[] args){for(int i=0;i<args.Length;i++)args[i]=Raw(args[i]);return ITMGradeDispatch.Call(Native,name,args);}
 public ITMGradeExcel Workbooks{get{return Wrap(Read("Workbooks"));}} public ITMGradeExcel Worksheets{get{return Wrap(Read("Worksheets"));}}
 public ITMGradeExcel Cells{get{return Wrap(Read("Cells"));}} public ITMGradeExcel Rows{get{return Wrap(Read("Rows"));}}public ITMGradeExcel Columns{get{return Wrap(Read("Columns"));}}
 public ITMGradeExcel UsedRange{get{return Wrap(Read("UsedRange"));}}public ITMGradeExcel MergeArea{get{return Wrap(Read("MergeArea"));}}
 public int Count{get{return Convert.ToInt32(Read("Count"));}}public int Row{get{return Convert.ToInt32(Read("Row"));}}public int Column{get{return Convert.ToInt32(Read("Column"));}}public string Name{get{return Convert.ToString(Read("Name"));}}
 public bool ReadOnly{get{return Convert.ToBoolean(Read("ReadOnly"));}}public bool HasFormula{get{return Convert.ToBoolean(Read("HasFormula"));}}public bool MergeCells{get{return Convert.ToBoolean(Read("MergeCells"));}}
 public string Text{get{return Convert.ToString(Read("Text"));}}public object Value2{get{return Read("Value2");}set{Write("Value2",value);}}
 public string Formula{get{return Convert.ToString(Read("Formula"));}set{Write("Formula",value);}}
 public object FormulaValues{get{return Read("FormulaR1C1");}set{Write("FormulaR1C1",value);}}
 public string FormulaR1C1{get{return Convert.ToString(Read("FormulaR1C1"));}set{Write("FormulaR1C1",value);}}
 public bool Visible{set{Write("Visible",value);}}public bool DisplayAlerts{set{Write("DisplayAlerts",value);}}public bool EnableEvents{set{Write("EnableEvents",value);}}public int AutomationSecurity{set{Write("AutomationSecurity",value);}}
 public ITMGradeExcel Item(object index){return Wrap(Read("Item",Raw(index)));}public ITMGradeExcel Item(object row,object col){return Wrap(Read("Item",Raw(row),Raw(col)));}
 public ITMGradeExcel Range(object first,object last){return Wrap(Read("Range",Raw(first),Raw(last)));}
 public ITMGradeExcel Open(string path,int updateLinks,bool readOnly){object[] args=new object[15];args[0]=System.IO.Path.GetFullPath(path);args[1]=updateLinks;args[2]=readOnly;for(int i=3;i<args.Length;i++)args[i]=System.Reflection.Missing.Value;return Wrap(Call("Open",args));}
 public void Close(bool save){Call("Close",save,System.Reflection.Missing.Value,System.Reflection.Missing.Value);}public void Save(){Call("Save");}public void Quit(){Call("Quit");}public void CalculateFull(){Call("CalculateFull");}
 public void ClearContents(){Call("ClearContents");}public void Delete(){Call("Delete");}public void Insert(){Call("Insert",System.Reflection.Missing.Value,System.Reflection.Missing.Value);}public void Copy(object destination){Call("Copy",destination);}
 public void Dispose(){if(!root)return;for(int i=owned.Count-1;i>=0;i--){try{if(Marshal.IsComObject(owned[i]))Marshal.ReleaseComObject(owned[i]);}catch{}}owned.Clear();Native=null;}
}




// Keep COM reflection entirely in managed code: PowerShell must not dispatch GetType/InvokeMember on an Office RCW.
public static class ITMGradeDispatch {
    // object[] arguments supplied by PowerShell can contain PSObject wrappers.
    // Marshal the underlying CLR values, never the wrapper, into Office VARIANTs.
    public static object Unwrap(object value){
        for(int depth=0;value!=null&&depth<32;depth++){
            Type type=value.GetType();
            if(type.FullName!="System.Management.Automation.PSObject")return value;
            object next=type.GetProperty("BaseObject").GetValue(value,null);
            if(Object.ReferenceEquals(next,value))throw new InvalidOperationException("Enveloppe PowerShell cyclique.");
            value=next;
        }
        if(value!=null)throw new InvalidOperationException("Enveloppes PowerShell imbriquées excessives.");
        return null;
    }
    private static object[] NativeArguments(object[] arguments){
        if(arguments==null)return new object[0];
        object[] native=new object[arguments.Length];
        for(int i=0;i<native.Length;i++)native[i]=Unwrap(arguments[i]);
        return native;
    }
    public static void SaveCopyAs(object workbook,object path){
        object nativePath=Unwrap(path);
        string fileName=nativePath as string;
        if(String.IsNullOrWhiteSpace(fileName))throw new ArgumentException("Chemin de copie Excel absent ou non textuel.");
        fileName=System.IO.Path.GetFullPath(fileName);
        Call(workbook,"SaveCopyAs",new object[]{fileName});
    }
    private static object Invoke(object target,string name,BindingFlags flags,object[] arguments){
        target=Unwrap(target);
        if(target==null)throw new ArgumentNullException("target","Objet Excel absent pour "+name+".");
        if(String.IsNullOrWhiteSpace(name))throw new ArgumentException("Nom de membre Excel absent.");
        try{return target.GetType().InvokeMember(name,flags,null,target,NativeArguments(arguments),null,CultureInfo.GetCultureInfo("en-US"),null);}
        catch(TargetInvocationException e){throw new InvalidOperationException("Excel : "+name+" : "+(e.InnerException??e).Message,e.InnerException??e);}
    }
    public static object Get(object target,string name,object[] index){return Invoke(target,name,BindingFlags.GetProperty,index);}
    public static void Set(object target,string name,object value){Invoke(target,name,BindingFlags.SetProperty,new object[]{value});}
    public static object Call(object target,string name,object[] arguments){return Invoke(target,name,BindingFlags.InvokeMethod,arguments);}
}
