using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Threading;
public static class ITMApplicationInstances {
    private static FileStream lease;
    private static string leasePath;
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window,int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    public static bool Enter(bool multiple) {
        if(lease!=null)return true;
        string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ITMCD","fenetres");
        Directory.CreateDirectory(folder);
        using(var gate=new Mutex(false,"Local\\ITMCD.Fenetres."+WindowsIdentity.GetCurrent().User.Value)) {
            bool acquired=false;
            try {
                try{acquired=gate.WaitOne(3000);}catch(AbandonedMutexException){acquired=true;}
                if(!acquired)throw new IOException("Une autre fenêtre démarre. Réessayez dans quelques secondes.");
                var active=new List<Process>();
                foreach(string file in Directory.GetFiles(folder,"*.session")) {
                    bool locked=false;
                    try{using(var probe=new FileStream(file,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }
                    catch(IOException){locked=true;}
                    if(!locked){try{File.Delete(file);}catch(IOException){}continue;}
                    try {
                        string[] record=File.ReadAllText(file).Split('|');
                        int id=int.Parse(record[0]);long started=long.Parse(record[1]);
                        var process=Process.GetProcessById(id);
                        if(!process.HasExited && process.StartTime.ToUniversalTime().Ticks==started)active.Add(process);
                        else process.Dispose();
                    }catch(IOException){}catch(ArgumentException){}catch(FormatException){}catch(InvalidOperationException){}
                }
                try {
                    if(!multiple && active.Count>0) {
                        var process=active[0];process.Refresh();IntPtr window=process.MainWindowHandle;
                        if(window!=IntPtr.Zero){if(IsIconic(window))ShowWindowAsync(window,9);SetForegroundWindow(window);}
                        return false;
                    }
                    leasePath=Path.Combine(folder,Guid.NewGuid().ToString("N")+".session");
                    lease=new FileStream(leasePath,FileMode.CreateNew,FileAccess.Write,FileShare.Read);
                    using(var self=Process.GetCurrentProcess()) {
                        byte[] record=System.Text.Encoding.UTF8.GetBytes(self.Id+"|"+self.StartTime.ToUniversalTime().Ticks);
                        lease.Write(record,0,record.Length);lease.Flush();
                    }
                    return true;
                } finally {foreach(var process in active)process.Dispose();}
            } finally {if(acquired)gate.ReleaseMutex();}
        }
    }
    public static void Leave() {
        if(lease!=null){lease.Dispose();lease=null;}
        if(leasePath!=null){try{File.Delete(leasePath);}catch(IOException){}leasePath=null;}
    }
}
