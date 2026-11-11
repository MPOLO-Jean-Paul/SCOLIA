using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
public sealed class ScoliaNetworkPeer {
 public string SchoolId,School,Root,Host;
}
public sealed class ScoliaNetworkProbe { public string Root,Profile; }
public sealed class ScoliaNetworkHost:IDisposable {
 readonly UdpClient socket;readonly Thread thread;volatile bool stopped;
 readonly string response;readonly string[] networks;
 public ScoliaNetworkHost(string id,string school,string share,string[] localNetworks) {
  ScoliaNetwork.ValidateIdentity(id,school,share);networks=localNetworks;
  response="SCOLIA|1|{0}|"+id+"|"+Convert.ToBase64String(Encoding.UTF8.GetBytes(school))+"|"+share+"|"+Environment.MachineName;
  socket=new UdpClient(new IPEndPoint(IPAddress.Any,48629));socket.Client.ReceiveTimeout=700;
  thread=new Thread(Listen);thread.IsBackground=true;thread.Start();
 }
 void Listen(){while(!stopped){try{
  IPEndPoint remote=new IPEndPoint(IPAddress.Any,0);byte[] data=socket.Receive(ref remote);
  if(data.Length>128||!ScoliaNetwork.IsLocal(remote.Address,networks))continue;
  string request=Encoding.ASCII.GetString(data);if(!Regex.IsMatch(request,@"^SCOLIA_DISCOVER_V1\|[a-f0-9]{32}$"))continue;
  byte[] reply=Encoding.UTF8.GetBytes(String.Format(response,request.Substring(19)));socket.Send(reply,reply.Length,remote);
 }catch(SocketException){if(stopped)return;}catch(ObjectDisposedException){return;}catch{}}}
 public void Dispose(){stopped=true;socket.Close();thread.Join(900);}
}
[DataContract] public sealed class ScoliaProbeReply {
 [DataMember]public string Nonce;[DataMember]public bool Success;[DataMember]public string Root;[DataMember]public string Profile;[DataMember]public string Error;
}
public static class ScoliaNetwork {
 public static string WorkerScript;
 static string Quote(string s){return "\""+s+"\"";}
 public static Task<ScoliaNetworkProbe> ProbeBoundedAsync(string root,int timeoutMs){
  string target=ValidateRoot(root);if(timeoutMs<1000||timeoutMs>30000)throw new ArgumentOutOfRangeException("timeoutMs");
  string script=WorkerScript;if(String.IsNullOrEmpty(script)||!File.Exists(script))throw new FileNotFoundException("Vérificateur réseau absent.");
  return Task.Factory.StartNew(()=>{
   string nonce=Guid.NewGuid().ToString("N"),request=Path.Combine(Path.GetTempPath(),"scolia-probe-"+nonce+".json");
   var ser=new DataContractJsonSerializer(typeof(ScoliaProbeReply));var input=new ScoliaProbeReply{Nonce=nonce,Root=target};using(var f=File.Create(request))ser.WriteObject(f,input);
   try{string executable=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");
    using(var process=new Process()){process.StartInfo=new ProcessStartInfo(executable,"-NoProfile -ExecutionPolicy Bypass -File "+Quote(script)+" -RequestPath "+Quote(request)){UseShellExecute=false,CreateNoWindow=true};process.Start();
     if(!process.WaitForExit(timeoutMs)){try{process.Kill();process.WaitForExit(2000);}catch{}throw new TimeoutException("Le partage n’a pas répondu dans le délai prévu. Vérifiez le poste hôte et réessayez.");}
     using(var f=File.OpenRead(request+".result")){var reply=(ScoliaProbeReply)ser.ReadObject(f);if(reply.Nonce!=nonce||!reply.Success)throw new IOException(reply.Error??"Réponse de vérification invalide.");if(!String.Equals(reply.Root,target,StringComparison.OrdinalIgnoreCase))throw new IOException("Adresse de vérification différente.");return new ScoliaNetworkProbe{Root=reply.Root,Profile=reply.Profile};}
    }
   }finally{foreach(string f in new[]{request,request+".result"})try{if(File.Exists(f))File.Delete(f);}catch{}}
  });
 }

 public static void ValidateIdentity(string id,string school,string share){
  if(!Regex.IsMatch(id??"",@"^[a-f0-9]{32}$")||String.IsNullOrWhiteSpace(school)||school.Length>160||!Regex.IsMatch(share??"",@"^SCOLIA_[A-Z0-9_]{1,60}$"))throw new ArgumentException("Annonce SCOLIA invalide.");
 }
 static uint Address(IPAddress ip){byte[] b=ip.GetAddressBytes();if(b.Length!=4)throw new ArgumentException("IPv4 requis.");return ((uint)b[0]<<24)|((uint)b[1]<<16)|((uint)b[2]<<8)|b[3];}
 public static bool IsLocal(IPAddress ip,string[] networks){
  if(ip.AddressFamily!=AddressFamily.InterNetwork)return false;
  foreach(string network in networks??new string[0]){string[] parts=network.Split('/');int bits;if(parts.Length!=2||!Int32.TryParse(parts[1],out bits)||bits<8||bits>32)continue;IPAddress local;if(!IPAddress.TryParse(parts[0],out local)||local.AddressFamily!=AddressFamily.InterNetwork)continue;uint mask=bits==32?UInt32.MaxValue:UInt32.MaxValue<<(32-bits);if((Address(ip)&mask)==(Address(local)&mask))return true;}
  return false;
 }
 public static string Broadcast(string network){string[] parts=network.Split('/');int bits=Int32.Parse(parts[1]);if(bits<8||bits>30)throw new ArgumentException("Sous-réseau IPv4 invalide.");uint value=Address(IPAddress.Parse(parts[0])),mask=UInt32.MaxValue<<(32-bits),b=value|~mask;return new IPAddress(new byte[]{(byte)(b>>24),(byte)(b>>16),(byte)(b>>8),(byte)b}).ToString();}
 public static ScoliaNetworkPeer ParseReply(byte[] data,IPAddress source,string nonce,string[] networks){
  try{if(data.Length>2048||!Regex.IsMatch(nonce??"",@"^[a-f0-9]{32}$")||!IsLocal(source,networks))return null;
   string[] parts=new UTF8Encoding(false,true).GetString(data).Split('|');if(parts.Length!=7||parts[0]!="SCOLIA"||parts[1]!="1"||parts[2]!=nonce)return null;
   string school=new UTF8Encoding(false,true).GetString(Convert.FromBase64String(parts[4]));ValidateIdentity(parts[3],school,parts[5]);if(!Regex.IsMatch(parts[6],@"^[A-Za-z0-9][A-Za-z0-9.-]{0,62}$"))return null;
   return new ScoliaNetworkPeer{SchoolId=parts[3],School=school,Host=parts[6],Root=@"\\"+parts[6]+@"\"+parts[5]};
  }catch{return null;}
 }
 public static Task<ScoliaNetworkPeer[]> DiscoverAsync(string[] networks){return Task.Factory.StartNew(()=>{
  var peers=new Dictionary<string,ScoliaNetworkPeer>();string nonce=Guid.NewGuid().ToString("N");byte[] request=Encoding.ASCII.GetBytes("SCOLIA_DISCOVER_V1|"+nonce);
  var sockets=new List<UdpClient>();try{
   foreach(string network in networks){try{string local=network.Split('/')[0];var s=new UdpClient(new IPEndPoint(IPAddress.Parse(local),0));s.EnableBroadcast=true;s.Client.ReceiveTimeout=100; sockets.Add(s);s.Send(request,request.Length,new IPEndPoint(IPAddress.Parse(Broadcast(network)),48629));}catch{}}
   if(sockets.Count==0)throw new IOException("Aucun réseau IPv4 privé utilisable.");
   DateTime until=DateTime.UtcNow.AddSeconds(3);while(DateTime.UtcNow<until){foreach(var s in sockets){try{if(s.Available==0)continue;IPEndPoint remote=new IPEndPoint(IPAddress.Any,0);var peer=ParseReply(s.Receive(ref remote),remote.Address,nonce,networks);if(peer!=null)peers[peer.Root]=peer;}catch(SocketException){}}Thread.Sleep(25);}
   var result=new List<ScoliaNetworkPeer>(peers.Values);result.Sort((a,b)=>StringComparer.CurrentCultureIgnoreCase.Compare(a.School,b.School));return result.ToArray();
  }finally{foreach(var s in sockets)s.Close();}
 });}
 public static string ValidateRoot(string root){
  if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("Choisissez un dossier SCOLIA.");if(root.IndexOfAny(new[]{'"','\r','\n','\0','|','<','>','*','?'})>=0)throw new ArgumentException("Adresse de dossier invalide.");string full=Path.GetFullPath(root.Trim());
  if(full.StartsWith(@"\\")){if(!Regex.IsMatch(full,@"^\\\\[^\\/\s]+\\[^\\/]+(?:\\.*)?$"))throw new ArgumentException("Adresse réseau : \\\\ordinateur\\partage.");}
  return full;
 }
 public static Task<ScoliaNetworkProbe> ProbeAsync(string root){return Task.Factory.StartNew(()=>Probe(root));}
 public static ScoliaNetworkProbe Probe(string root){
  root=ValidateRoot(root);string profile=Path.Combine(root,"profil-scolia.json");string text;
  using(var file=new FileStream(profile,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(file.Length>1048576)throw new IOException("Configuration trop grande.");using(var reader=new StreamReader(file,Encoding.UTF8,true))text=reader.ReadToEnd();}
  string test=Path.Combine(root,".scolia-probe-"+Guid.NewGuid().ToString("N")),next=test+".next",backup=test+".bak";
  try{using(var f=new FileStream(test,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.WriteByte(1);f.Flush(true);}using(var f=new FileStream(next,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.WriteByte(2);f.Flush(true);}File.Replace(next,test,backup);if(File.ReadAllBytes(test)[0]!=2)throw new IOException("Écriture réseau non fiable.");}
  finally{foreach(string f in new[]{test,next,backup}){try{if(File.Exists(f))File.Delete(f);}catch{}}}
  return new ScoliaNetworkProbe{Root=root,Profile=text};
 }
}
