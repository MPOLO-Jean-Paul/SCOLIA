using System;using System.Collections.Generic;using System.IO;using System.Xml;using System.Xml.Serialization;using System.Globalization;
[XmlRoot("PrisesEnCharge")]
public sealed class ITMPELedger {
 [XmlAttribute] public int Version=1;[XmlAttribute] public string Dataset;[XmlAttribute] public long Revision;
 [XmlArrayItem("Record")] public List<ITMPERecord> Records=new List<ITMPERecord>();
 [XmlArrayItem("Event")] public List<ITMPEEvent> History=new List<ITMPEEvent>();
}
public sealed class ITMPERecord {
 [XmlAttribute] public string Id;[XmlAttribute] public string Matricule;[XmlAttribute] public string Annee;
 public string Promotion;public string Garant;public string Note;public decimal Tarif;public string Updated;
 [XmlArrayItem("boolean")] public bool[] Couverture=new bool[10];
}
public sealed class ITMPEEvent {public string Date,User,Action,Matricule,Annee,Motif;public ITMPERecord Before,After;}
public static class ITMPE {
 public static readonly string[] Months={"Septembre","Octobre","Novembre","Décembre","Janvier","Février","Mars","Avril","Mai","Juin"};
 public static string Year(string value){string s=(value??"").Trim().Replace('–','-').Replace('—','-').Replace(" ","");int y,z;if(s.Length!=9||s[4]!='-'||!Int32.TryParse(s.Substring(0,4),out y)||!Int32.TryParse(s.Substring(5),out z)||y<2000||y>2198||z!=y+1)throw new InvalidOperationException("Année scolaire attendue : 2026-2027.");return s;}
 public static string Id(string value){return (value??"").Trim().ToUpperInvariant();}
 public static int MonthIndex(DateTime date,string year){int y=Int32.Parse(Year(year).Substring(0,4));return (date.Year-y)*12+date.Month-9;}
 public static ITMPERecord Find(ITMPELedger l,string mat,string year){string m=Id(mat),y=Year(year);foreach(var r in l.Records)if(Id(r.Matricule)==m&&Year(r.Annee)==y)return r;return null;}
 public static bool Covered(ITMPERecord r,int month){return r!=null&&month>=0&&month<10&&r.Couverture[month];}
 public static decimal Total(ITMPERecord r,decimal monthly){if(monthly<0)throw new InvalidOperationException("Tarif négatif.");decimal v=0;if(r!=null)foreach(bool b in r.Couverture)if(b)v+=monthly;return v;}
 public static decimal Payable(ITMPERecord r,decimal monthly,decimal[] paid){if(paid==null||paid.Length!=10)throw new InvalidOperationException("Dix montants mensuels attendus.");decimal v=0;for(int i=0;i<10;i++){if(paid[i]<0)throw new InvalidOperationException("Paiement négatif.");if(!Covered(r,i))v+=Math.Max(0,monthly-paid[i]);}return v;}
 public static ITMPERecord Copy(ITMPERecord r){return r==null?null:new ITMPERecord{Id=r.Id,Matricule=r.Matricule,Annee=r.Annee,Promotion=r.Promotion,Garant=r.Garant,Note=r.Note,Tarif=r.Tarif,Updated=r.Updated,Couverture=(bool[])r.Couverture.Clone()};}
 public static void Validate(ITMPELedger l,string dataset){if(l==null||l.Version!=1||l.Dataset!=dataset||l.Revision<0||l.Records==null||l.History==null)throw new InvalidOperationException("Registre des prises en charge invalide ou appartenant à un autre dossier data.");var ids=new HashSet<string>();foreach(var r in l.Records){if(r==null||String.IsNullOrWhiteSpace(r.Id)||String.IsNullOrWhiteSpace(r.Matricule)||r.Couverture==null||r.Couverture.Length!=10||r.Tarif<=0||r.Tarif!=Decimal.Truncate(r.Tarif)||String.IsNullOrWhiteSpace(r.Promotion)||!ids.Add(Id(r.Matricule)+"|"+Year(r.Annee)))throw new InvalidOperationException("Dossier de prise en charge incomplet ou en double.");if((r.Garant??"").Length>160||(r.Note??"").Length>1000)throw new InvalidOperationException("Libellé de prise en charge trop long.");}}
 public static ITMPELedger Load(string path,string dataset){if(!File.Exists(path))return new ITMPELedger{Dataset=dataset};var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20000000};using(var reader=XmlReader.Create(path,settings)){var l=(ITMPELedger)new XmlSerializer(typeof(ITMPELedger)).Deserialize(reader);Validate(l,dataset);return l;}}
 public static void Save(string path,ITMPELedger l,long expected){Validate(l,l.Dataset);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));using(var gate=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
 var latest=Load(path,l.Dataset);if(latest.Revision!=expected)throw new InvalidOperationException("Une autre fenêtre a modifié les prises en charge. Actualisez avant de recommencer.");
 string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";long before=l.Revision;l.Revision=expected+1;
 try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){new XmlSerializer(typeof(ITMPELedger)).Serialize(fs,l);fs.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".precedent",true);else File.Move(temp,path);}catch{l.Revision=before;throw;}finally{if(File.Exists(temp))File.Delete(temp);}
 }}
 public static ITMPERecord Plan(ITMPERecord old,string mat,string year,string promotion,decimal monthly,int first,int last,int effective,decimal[] paid,string garant,string note){
 if(monthly<=0||monthly!=Decimal.Truncate(monthly)||first<0||last>9||last<first||effective<0||effective>10||paid==null||paid.Length!=10)throw new InvalidOperationException("Période ou tarif invalide.");
 var r=Copy(old)??new ITMPERecord{Id=Guid.NewGuid().ToString("N"),Matricule=Id(mat),Annee=Year(year)};
 if(Id(r.Matricule)!=Id(mat)||Year(r.Annee)!=Year(year))throw new InvalidOperationException("Dossier incompatible.");
 for(int i=0;i<10;i++){bool proposed=i>=first&&i<=last;if(old!=null&&i<effective)continue;if(proposed&&paid[i]>0)throw new InvalidOperationException("Un paiement existe pour "+Months[i]+". Choisissez une période sans paiement : les encaissements sont conservés.");r.Couverture[i]=proposed;}
 r.Promotion=promotion;r.Tarif=monthly;r.Garant=(garant??"").Trim();r.Note=(note??"").Trim();r.Updated=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);return r;
 }
 public static ITMPERecord Paying(ITMPERecord old,int from){if(old==null||from<0||from>10)throw new InvalidOperationException("Dossier ou mois de retour au statut payant invalide.");var r=Copy(old);for(int i=from;i<10;i++)r.Couverture[i]=false;r.Updated=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);return r;}
 public static void Apply(ITMPELedger l,ITMPERecord r,string user,string action,string motif){if(String.IsNullOrWhiteSpace(motif))throw new InvalidOperationException("Précisez le motif.");var old=Find(l,r.Matricule,r.Annee);l.History.Add(new ITMPEEvent{Date=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),User=user,Action=action,Matricule=r.Matricule,Annee=r.Annee,Motif=motif,Before=Copy(old),After=Copy(r)});if(old!=null)l.Records.Remove(old);l.Records.Add(Copy(r));Validate(l,l.Dataset);}
}
