using System;
using System.Globalization;
using System.Reflection;
// Keep COM reflection entirely in managed code: PowerShell must not dispatch GetType/InvokeMember on an Office RCW.
public static class ITMExcelDispatch {
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
