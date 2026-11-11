using System;
using System.Runtime.InteropServices;
public static class ITMWindowsShortcuts {
    public const string AppId = "ItmCd.Secretariat";
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid format; public uint id; }
    [StructLayout(LayoutKind.Explicit,Size=24)]
    private struct PropVariant {
        [FieldOffset(0)] public ushort type;
        [FieldOffset(8)] public IntPtr pointer;
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index,out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key,out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key,ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern int SHGetPropertyStoreFromParsingName(string path,IntPtr bind,uint flags,ref Guid iid,out IPropertyStore store);
    [DllImport("shell32.dll")]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window,ref Guid iid,out IPropertyStore store);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    private static extern void SHChangeNotify(uint eventId,uint flags,string item1,IntPtr item2);
    public static void NotifyIconChange(string path){SHChangeNotify(0x2000,0x0005,path,IntPtr.Zero);}
    public static void SetWindowIconResource(IntPtr window,string iconPath){
        IPropertyStore store=null;var iid=new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        var key=new PropertyKey{format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),id=3};
        var value=new PropVariant{type=31,pointer=Marshal.StringToCoTaskMemUni(iconPath+",0")};
        try{Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window,ref iid,out store));Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));Marshal.ThrowExceptionForHR(store.Commit());}
        finally{Marshal.FreeCoTaskMem(value.pointer);if(store!=null)Marshal.FinalReleaseComObject(store);}
    }
    public static void IdentifyProcess() { Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppId)); }
    public static void IdentifyShortcut(string path) {
        IPropertyStore store=null;
        var iid=new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
        var key=new PropertyKey { format=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),id=5 };
        var value=new PropVariant { type=31,pointer=Marshal.StringToCoTaskMemUni(AppId) };
        try {
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path,IntPtr.Zero,2,ref iid,out store));
            Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));
            Marshal.ThrowExceptionForHR(store.Commit());
        } finally {
            Marshal.FreeCoTaskMem(value.pointer);
            if(store!=null)Marshal.FinalReleaseComObject(store);
        }
    }
}
