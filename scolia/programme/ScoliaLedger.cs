using System;using System.IO;using System.Xml;using System.Xml.Serialization;using System.Globalization;using System.Collections.Generic;using System.Linq;using System.Threading.Tasks;using System.Security.Cryptography;using System.Text;
public sealed class ScoliaPart{public string Mois;public decimal Montant;}
public sealed class ScoliaEntry{
 public int Version=1;public int ConfigRevision;public string Id,SchoolId,Kind,Reference,Date,Created,Compte,Nom,PostNom,Prenom,Promotion,Annee,Matricule,Sexe,Naissance,Lieu,Contact1,Contact2,Adresse,Description;
 public decimal Montant;public List<ScoliaPart> Parts=new List<ScoliaPart>();public string Checksum;public string OperatorId,OperatorName;public string CategoryName,Category,CommonReceipt,Revision,Cancelled,CorrectionReason,CorrectionAuthor;public List<ScoliaPart> OriginalParts;public bool ShouldSerializeOriginalParts(){return OriginalParts!=null&&OriginalParts.Count>0;}
 public ScoliaEntry Copy(){var copy=(ScoliaEntry)MemberwiseClone();copy.Parts=Parts==null?null:Parts.Select(p=>new ScoliaPart{Mois=p.Mois,Montant=p.Montant}).ToList();copy.OriginalParts=OriginalParts==null?null:OriginalParts.Select(p=>new ScoliaPart{Mois=p.Mois,Montant=p.Montant}).ToList();return copy;}
}
public sealed class ScoliaState{public string Id,SchoolId,Status,Matricule,Message,Updated,Revision;}
public static class ScoliaLedger{
 static readonly XmlSerializer serializer=new XmlSerializer(typeof(ScoliaEntry));static readonly XmlSerializer stateSerializer=new XmlSerializer(typeof(ScoliaState));
 public static readonly string[] Months={"Janvier","Février","Mars","Avril","Mai","Juin","Juillet","Août","Septembre","Octobre","Novembre","Décembre"};
 public static string Key(string value){var b=new StringBuilder();foreach(char c in (value??"").Trim().ToUpperInvariant().Normalize(NormalizationForm.FormD)){if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)b.Append(c);}return String.Join(" ",b.ToString().Split((char[])null,StringSplitOptions.RemoveEmptyEntries));}
 public static string Identity(ScoliaEntry e){return Key(e.Nom)+"|"+Key(e.PostNom)+"|"+Key(e.Prenom)+"|"+Key(e.Promotion)+"|"+Key(e.Annee).Replace(" ","");}
 public static bool SameLearner(ScoliaEntry a,ScoliaEntry b){return !String.IsNullOrEmpty(a.Matricule)&&!String.IsNullOrEmpty(b.Matricule)?Key(a.Matricule)==Key(b.Matricule)&&ScoliaSharing.YearKey(a.Annee)==ScoliaSharing.YearKey(b.Annee):Identity(a)==Identity(b);}
 public static string[] Schedule(int start,int count){if(start<1||start>12||count<1||count>12)throw new ArgumentException("Calendrier scolaire invalide.");return Enumerable.Range(0,count).Select(i=>Months[(start-1+i)%12]).ToArray();}
 internal static byte[] Bytes(object obj,XmlSerializer ser){using(var stream=new MemoryStream()){ser.Serialize(stream,obj);return stream.ToArray();}}
 internal static string Hash(ScoliaEntry e){string old=e.Checksum;e.Checksum=null;try{using(var h=SHA256.Create())return Convert.ToBase64String(h.ComputeHash(Bytes(e,serializer)));}finally{e.Checksum=old;}}
 internal static void Validate(ScoliaEntry e,string school){Guid id;if(e==null||e.Version!=1||!Guid.TryParseExact(e.Id,"N",out id)||e.SchoolId!=school||e.Kind!="Paiement"&&e.Kind!="Depense"||e.Montant<=0||e.Montant!=Math.Truncate(e.Montant)||e.Montant>100000000000m)throw new InvalidDataException("Écriture SCOLIA invalide.");DateTime date;if(!DateTime.TryParseExact(e.Date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date)||date.Date>DateTime.Today)throw new InvalidDataException("Date invalide ou future.");foreach(string s in new[]{e.Nom,e.PostNom,e.Prenom,e.Promotion,e.Annee,e.Reference,e.Description})if((s??"").Length>250)throw new InvalidDataException("Texte trop long.");if(e.Kind=="Paiement"){if(String.IsNullOrWhiteSpace(e.Nom)||(e.Compte!="Secrétariat"&&(String.IsNullOrWhiteSpace(e.PostNom)||String.IsNullOrWhiteSpace(e.Prenom)))||String.IsNullOrWhiteSpace(e.Promotion)||String.IsNullOrWhiteSpace(e.Annee)||String.IsNullOrWhiteSpace(e.Reference))throw new InvalidDataException("Identité, promotion, année et référence de caisse obligatoires.");if(ScoliaFinance.IsTuition(e)&&(e.Parts==null||e.Parts.Count<1||e.Parts.Count>12||e.Parts.Any(p=>p.Montant<=0||p.Montant!=Math.Truncate(p.Montant)||!Months.Contains(p.Mois))||e.Parts.Sum(p=>p.Montant)!=e.Montant||e.Parts.Select(p=>p.Mois).Distinct().Count()!=e.Parts.Count))throw new InvalidDataException("Répartition mensuelle invalide.");}else if(String.IsNullOrWhiteSpace(e.Description)||String.IsNullOrWhiteSpace(e.Reference))throw new InvalidDataException("Référence et libellé de dépense obligatoires.");}
 public static ScoliaEntry[] Read(string root,string school){ScoliaPolicy.Load(root,school);string folder=Path.Combine(Path.GetFullPath(root),"journal");var folders=Directory.GetDirectories(root).Select(Path.GetFullPath);if(!folders.Contains(folder,StringComparer.OrdinalIgnoreCase))return new ScoliaEntry[0];var list=new List<ScoliaEntry>();foreach(string f in Directory.GetFiles(folder,"*.xml")){if(new FileInfo(f).Length>262144)throw new InvalidDataException("Écriture trop volumineuse : "+Path.GetFileName(f));ScoliaEntry e;var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};using(var reader=XmlReader.Create(f,settings))e=(ScoliaEntry)serializer.Deserialize(reader);Validate(e,school);if(Path.GetFileNameWithoutExtension(f)!=e.Id||e.Checksum!=Hash(e))throw new InvalidDataException("Intégrité du journal invalide : "+Path.GetFileName(f));list.Add(e);}return ScoliaFinance.Apply(root,school,list.ToArray());}
 public static ScoliaEntry ReadVerifiedFile(string file,string school){
  if(new FileInfo(file).Length>262144)throw new InvalidDataException("Écriture trop volumineuse.");var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};ScoliaEntry e;using(var reader=XmlReader.Create(file,settings))e=(ScoliaEntry)serializer.Deserialize(reader);Validate(e,school);if(Path.GetFileNameWithoutExtension(file)!=e.Id||e.Checksum!=Hash(e))throw new InvalidDataException("Intégrité du journal invalide : "+Path.GetFileName(file));return e;
 }
 public static void ValidateDraft(ScoliaEntry e,string school){if(e==null)throw new InvalidDataException("Brouillon absent.");var trial=e.Copy();if(trial.Kind=="Paiement")trial.Parts=new List<ScoliaPart>{new ScoliaPart{Mois="Septembre",Montant=trial.Montant}};Validate(trial,school);if((e.OperatorName??"").Length>160||(e.OperatorId??"").Length>80)throw new InvalidDataException("Auteur invalide.");}
 public static string SaveDraft(string folder,string school,ScoliaEntry source){var draft=source.Copy();draft.SchoolId=school;if(String.IsNullOrEmpty(draft.Id))draft.Id=Guid.NewGuid().ToString("N");ValidateDraft(draft,school);draft.Checksum=Hash(draft);Directory.CreateDirectory(folder);string path=Path.Combine(folder,draft.Id+".xml");Atomic(path,Bytes(draft,serializer));return path;}
 public static ScoliaEntry ReadDraft(string path,string school){if(new FileInfo(path).Length>262144)throw new InvalidDataException("Brouillon trop volumineux.");var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};ScoliaEntry e;using(var r=XmlReader.Create(path,settings))e=(ScoliaEntry)serializer.Deserialize(r);ValidateDraft(e,school);if(Path.GetFileNameWithoutExtension(path)!=e.Id||e.Checksum!=Hash(e))throw new InvalidDataException("Intégrité du brouillon invalide.");return e;}
 public static bool SameIntent(ScoliaEntry a,ScoliaEntry b){return ScoliaFinance.CategoryKey(a)==ScoliaFinance.CategoryKey(b)&&a.Version==b.Version&&a.Id==b.Id&&a.Matricule==b.Matricule&&a.Kind==b.Kind&&a.Montant==b.Montant&&a.Date==b.Date&&a.Reference==b.Reference&&Identity(a)==Identity(b)&&a.Compte==b.Compte&&a.OperatorId==b.OperatorId&&a.OperatorName==b.OperatorName&&a.Description==b.Description&&a.Sexe==b.Sexe&&a.Naissance==b.Naissance&&a.Lieu==b.Lieu&&a.Contact1==b.Contact1&&a.Contact2==b.Contact2&&a.Adresse==b.Adresse;}
 public static Task<ScoliaEntry[]> ReadAsync(string root,string school){return Task.Factory.StartNew(()=>Read(root,school));}
 internal static void Atomic(string path,byte[] bytes){string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){fs.Write(bytes,0,bytes.Length);fs.Flush(true);}File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 public static ScoliaEntry Commit(string root,string school,ScoliaEntry e,int start,int months,decimal fee){
  Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"journal"));
  using(var gate=new FileStream(Path.Combine(root,"journal.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   ScoliaPolicy.Check(root,school,e,start,months,fee);if(String.IsNullOrEmpty(e.Id))e.Id=Guid.NewGuid().ToString("N");e.SchoolId=school;
   var all=Read(root,school);var existing=all.FirstOrDefault(x=>x.Id==e.Id);
   if(existing!=null){if(SameIntent(e,existing))return existing;throw new InvalidOperationException("Identifiant déjà utilisé par une autre opération.");}
   existing=all.FirstOrDefault(x=>Key(x.Reference)==Key(e.Reference)&&ScoliaSharing.YearKey(x.Annee)==ScoliaSharing.YearKey(e.Annee)&&x.Kind==e.Kind);
   if(existing!=null){if(ScoliaFinance.CommonMatch(existing,e))return existing;throw new InvalidOperationException("Référence commune déjà utilisée : montant, date, apprenant ou catégorie différents. Ouvrez le rapprochement.");}
   ScoliaFinance.Allocate(root,school,e,all,start,months,fee);
   e.Created=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);Validate(e,school);e.Checksum=Hash(e);
   Atomic(Path.Combine(root,"journal",e.Id+".xml"),Bytes(e,serializer));return e;
  }
 }

 // Stable across retries and segment moves; corrections retain the same identity.
 public static string SecretariatId(string school,string dataSet,string year,string receipt,string matricule){
  if(String.IsNullOrWhiteSpace(dataSet)||String.IsNullOrWhiteSpace(receipt)||String.IsNullOrWhiteSpace(matricule))throw new InvalidDataException("Identité du reçu incomplète.");
  using(var h=SHA256.Create()){var b=h.ComputeHash(Encoding.UTF8.GetBytes("SCOLIA-SECRETARIAT-1|"+school+"|"+dataSet+"|"+ScoliaSharing.YearKey(year)+"|"+Key(receipt)+"|"+Key(matricule)));return BitConverter.ToString(b,0,16).Replace("-","").ToLowerInvariant();}
 }
 public static bool SamePayment(ScoliaEntry a,ScoliaEntry b){return ScoliaFinance.CategoryKey(a)==ScoliaFinance.CategoryKey(b)&&a.Id==b.Id&&a.SchoolId==b.SchoolId&&a.Matricule==b.Matricule&&a.Reference==b.Reference&&a.Date==b.Date&&a.Montant==b.Montant&&ScoliaSharing.YearKey(a.Annee)==ScoliaSharing.YearKey(b.Annee)&&Key(a.Promotion)==Key(b.Promotion)&&a.Parts.Count==b.Parts.Count&&a.Parts.All(p=>b.Parts.Any(q=>p.Mois==q.Mois&&p.Montant==q.Montant));}
 public static Task<ScoliaImportResult> ImportSecretariatAsync(string root,string school,string year){return Task.Factory.StartNew(()=>ImportSecretariat(root,school,year));}
 public static ScoliaImportResult ImportSecretariat(string root,string school,string year){
  var result=new ScoliaImportResult();ScoliaPolicy.Load(root,school);
  using(var gate=new FileStream(Path.Combine(root,"journal.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var roster=ScoliaSharing.ReadRoster(root,school,year);if(String.IsNullOrEmpty(roster.PublishedUtc))throw new InvalidOperationException("Ouvrez la nouvelle version sur le poste secrétariat puis cliquez sur Actualiser pour publier ses paiements.");
   var all=Read(root,school).ToList();Directory.CreateDirectory(Path.Combine(root,"journal"));
   foreach(var source in (roster.Payments??new List<ScoliaEntry>()).OrderBy(e=>e.Date,StringComparer.Ordinal).ThenBy(e=>e.Reference,StringComparer.Ordinal).ThenBy(e=>e.Id)){
    var row=new ScoliaImportItem{Id=source.Id,Reference=source.Reference,Date=source.Date,Nom=(source.Nom+" "+source.PostNom+" "+source.Prenom).Trim(),Promotion=source.Promotion,Matricule=source.Matricule,Montant=source.Montant,Origine="Secrétariat",Mois=String.Join(" ; ",source.Parts.Select(p=>p.Mois+" : "+p.Montant.ToString(CultureInfo.InvariantCulture)).ToArray())};
    try{
     Validate(source,school);if(source.Compte!="Secrétariat"||source.Kind!="Paiement"||ScoliaSharing.YearKey(source.Annee)!=ScoliaSharing.YearKey(year)||!roster.Imported.Contains(source.Id))throw new InvalidDataException("Paiement hors du répertoire publié.");
     var old=all.FirstOrDefault(e=>e.Id==source.Id);
     if(old!=null){if(!SamePayment(source,old))throw new InvalidOperationException("Le reçu source a été modifié après son import. Rapprochement nécessaire ; aucun doublon créé.");row.Etat="Déjà importé";row.Importation=old.Created;result.Existing++;}
     else{
      if(all.Any(e=>e.Kind=="Paiement"&&Key(e.Reference)==Key(source.Reference)&&ScoliaSharing.YearKey(e.Annee)==ScoliaSharing.YearKey(year)))throw new InvalidOperationException("Référence déjà utilisée par une autre opération.");
      var importedEntry=source.Copy();importedEntry.Created=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture);importedEntry.Checksum=Hash(importedEntry);Atomic(Path.Combine(root,"journal",importedEntry.Id+".xml"),Bytes(importedEntry,serializer));all.Add(importedEntry);row.Etat="Importé";row.Importation=importedEntry.Created;result.Imported++;
     }
    }catch(Exception ex){row.Etat="À vérifier";row.Observation=ex.Message;result.Errors++;}
    result.Items.Add(row);
   }
  }
  return result;
 }
 public static Task<ScoliaEntry> CommitAsync(string root,string school,ScoliaEntry e,int start,int months,decimal fee){return Task.Factory.StartNew(()=>Commit(root,school,e,start,months,fee));}
 public static ScoliaState State(string root,string school,string id){Guid parsed,schoolGuid;if(!Guid.TryParseExact(school,"N",out schoolGuid)||!Guid.TryParseExact(id,"N",out parsed))throw new ArgumentException("Référence invalide.");string path=Path.Combine(root,"receptions",school+"-"+id+".xml");if(!File.Exists(path))return null;var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};using(var reader=XmlReader.Create(path,settings)){var s=(ScoliaState)stateSerializer.Deserialize(reader);if(s.Id!=id||s.SchoolId!=school||s.Status!="Importé"&&s.Status!="Brouillon"&&s.Status!="Erreur")throw new InvalidDataException("État de réception invalide.");return s;}}
 public static void SaveState(string root,ScoliaState s){Guid id,school;if(!Guid.TryParseExact(s.Id,"N",out id)||!Guid.TryParseExact(s.SchoolId,"N",out school)||s.Status!="Importé"&&s.Status!="Brouillon"&&s.Status!="Erreur")throw new ArgumentException("État invalide.");string folder=Path.Combine(root,"receptions");Directory.CreateDirectory(folder);string path=Path.Combine(folder,s.SchoolId+"-"+s.Id+".xml");using(var gate=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){var old=State(root,s.SchoolId,s.Id);if(old!=null&&old.Status=="Importé"&&s.Status!="Importé"&&old.Revision==s.Revision)throw new InvalidOperationException("Un transfert importé ne peut pas redevenir brouillon.");s.Updated=DateTime.UtcNow.ToString("o");string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,Bytes(s,stateSerializer));if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}}
}

public sealed class ScoliaLearner {
 public string Matricule,Nom,PostNom,Prenom,Promotion,Annee,Sexe,Naissance,Lieu,Contact1,Contact2,Adresse,Tuteur,Inscription,Statut;
 public decimal Mensuel,Annuel;public List<ScoliaPart> Paid=new List<ScoliaPart>();public List<ScoliaPart> Covered=new List<ScoliaPart>();
 public ScoliaLearner Copy(){var l=(ScoliaLearner)MemberwiseClone();l.Paid=Paid.Select(p=>new ScoliaPart{Mois=p.Mois,Montant=p.Montant}).ToList();l.Covered=Covered.Select(p=>new ScoliaPart{Mois=p.Mois,Montant=p.Montant}).ToList();return l;}
}
public sealed class ScoliaRoster{public int Version=1;public string SchoolId,Year;public List<ScoliaLearner> Learners=new List<ScoliaLearner>();public List<string> Imported=new List<string>();public List<ScoliaEntry> Payments=new List<ScoliaEntry>();public string PublishedUtc;public List<ScoliaEntry> AppliedPayments;public string AppliedFormat;public bool ShouldSerializeAppliedPayments(){return AppliedPayments!=null&&AppliedPayments.Count>0;}}
public sealed class ScoliaSnapshot{public ScoliaEntry[] Entries;public ScoliaRoster Roster;}
public static class ScoliaSharing {
 static readonly XmlSerializer serializer=new XmlSerializer(typeof(ScoliaRoster));
 public static string YearKey(string year){return ScoliaLedger.Key(year).Replace(" ","");}
 static string PathFor(string root,string school,string year){Guid id;if(!Guid.TryParseExact(school,"N",out id)||!System.Text.RegularExpressions.Regex.IsMatch(YearKey(year),"^[0-9]{4}-[0-9]{4}$"))throw new ArgumentException("Établissement ou année invalide.");return Path.Combine(root,"roster-"+school+"-"+YearKey(year)+".xml");}
 public static ScoliaRoster ReadRoster(string root,string school,string year){ScoliaPolicy.Load(root,school);string path=Path.GetFullPath(PathFor(root,school,year));var files=Directory.GetFiles(root).Select(Path.GetFullPath);if(!files.Contains(path,StringComparer.OrdinalIgnoreCase))return new ScoliaRoster{SchoolId=school,Year=year};if(new FileInfo(path).Length>67108864)throw new InvalidDataException("Répertoire trop volumineux.");var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};using(var reader=XmlReader.Create(path,settings)){var r=(ScoliaRoster)serializer.Deserialize(reader);if(r.Version!=1||r.SchoolId!=school||YearKey(r.Year)!=YearKey(year)||r.Learners==null||r.Imported==null)throw new InvalidDataException("Répertoire d’une autre institution ou année.");return r;}}
 public static void ValidateRoster(ScoliaRoster r,string school,string year){
  if(r==null||r.SchoolId!=school||YearKey(r.Year)!=YearKey(year)||r.Learners==null)throw new InvalidDataException("Répertoire d'une autre institution ou année.");
  var seen=new HashSet<string>();foreach(var l in r.Learners){if(String.IsNullOrWhiteSpace(l.Matricule)||String.IsNullOrWhiteSpace(l.Nom)||String.IsNullOrWhiteSpace(l.Promotion)||YearKey(l.Annee)!=YearKey(year)||!seen.Add(ScoliaLedger.Key(l.Matricule)))throw new InvalidDataException("Dossier sans matricule, sans promotion, en double ou d'une autre année. Corrigez le répertoire au secrétariat.");}
 }
 public static void SaveLocalRoster(string path,ScoliaRoster r,string school,string year){
  ValidateRoster(r,school,year);if(String.IsNullOrEmpty(r.PublishedUtc))throw new InvalidOperationException("Le secrétariat doit publier son répertoire avec la nouvelle version avant cet import.");
  Directory.CreateDirectory(Path.GetDirectoryName(path));string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var fs=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){serializer.Serialize(fs,r);fs.Flush(true);}if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}
 }
 public static ScoliaRoster LoadLocalRoster(string path,string school,string year){
  if(!File.Exists(path))return new ScoliaRoster{SchoolId=school,Year=year};if(new FileInfo(path).Length>67108864)throw new InvalidDataException("Répertoire trop volumineux.");
  var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};using(var reader=XmlReader.Create(path,settings)){var r=(ScoliaRoster)serializer.Deserialize(reader);ValidateRoster(r,school,year);return r;}
 }
 public static Task<ScoliaSnapshot> ReadAsync(string root,string school,string year){return Task.Factory.StartNew(()=>new ScoliaSnapshot{Entries=ScoliaLedger.Read(root,school),Roster=ReadRoster(root,school,year)});}
 public static Task PublishAsync(string root,ScoliaRoster roster){return Task.Factory.StartNew(()=>Publish(root,roster));}
 public static void Publish(string root,ScoliaRoster roster){Directory.CreateDirectory(root);string path=PathFor(root,roster.SchoolId,roster.Year);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";using(var gate=new FileStream(Path.Combine(root,"journal.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){var policy=ScoliaPolicy.Load(root,roster.SchoolId);if(YearKey(roster.Year)!=YearKey(policy.Year))throw new InvalidOperationException("Le répertoire ne correspond pas à l’année configurée. Connectez la base actuelle.");try{using(var fs=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){serializer.Serialize(fs,roster);fs.Flush(true);}if(File.Exists(path))File.Replace(temp,path,path+".bak");else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}}
 public static decimal OpeningPaid(ScoliaRoster roster,ScoliaLearner learner,ScoliaEntry[] entries,string month){decimal value=learner.Paid.Where(p=>p.Mois==month).Sum(p=>p.Montant);decimal imports=(roster.AppliedFormat=="1"?(roster.AppliedPayments??new List<ScoliaEntry>()):entries.Select(e=>{var c=e.Copy();if(c.OriginalParts!=null&&c.OriginalParts.Count>0)c.Parts=c.OriginalParts;return c;}).ToList()).Where(e=>e.Kind=="Paiement"&&roster.Imported.Contains(e.Id)&&((!String.IsNullOrEmpty(e.Matricule)&&ScoliaLedger.Key(e.Matricule)==ScoliaLedger.Key(learner.Matricule)&&YearKey(e.Annee)==YearKey(learner.Annee))||Matches(e,learner))).SelectMany(e=>e.Parts).Where(p=>p.Mois==month).Sum(p=>p.Montant);if(imports>value)throw new InvalidDataException("Le répertoire et les transferts importés sont incohérents. Actualisez depuis le secrétariat.");return value-imports;}
 public static bool Matches(ScoliaEntry entry,ScoliaLearner learner){return ScoliaLedger.Key(entry.Nom)==ScoliaLedger.Key(learner.Nom)&&ScoliaLedger.Key(entry.PostNom)==ScoliaLedger.Key(learner.PostNom)&&ScoliaLedger.Key(entry.Prenom)==ScoliaLedger.Key(learner.Prenom)&&ScoliaLedger.Key(entry.Promotion)==ScoliaLedger.Key(learner.Promotion)&&YearKey(entry.Annee)==YearKey(learner.Annee);}
}

[System.Runtime.Serialization.DataContract]public sealed class ScoliaPolicyClass{[System.Runtime.Serialization.DataMember]public string Code;[System.Runtime.Serialization.DataMember]public decimal Fee;}
[System.Runtime.Serialization.DataContract]public sealed class ScoliaPolicyData{[System.Runtime.Serialization.DataMember]public string Id;[System.Runtime.Serialization.DataMember]public string Year;[System.Runtime.Serialization.DataMember]public int Revision;[System.Runtime.Serialization.DataMember]public int Months;[System.Runtime.Serialization.DataMember]public int StartMonth;[System.Runtime.Serialization.DataMember]public ScoliaPolicyClass[] Classes;}
public static class ScoliaPolicy{
 public static ScoliaPolicyData Load(string root,string school){
  string path=Path.Combine(root,"profil-scolia.json");var ser=new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ScoliaPolicyData));ScoliaPolicyData p;
  // Opening and enumerating the directory must fail on an unavailable share, not return an empty journal.
  using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
   if(file.Length>1048576)throw new InvalidDataException("Configuration trop volumineuse.");
   using(var reader=new StreamReader(file,Encoding.UTF8,true)){using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd())))p=(ScoliaPolicyData)ser.ReadObject(stream);}
  }
  if(p==null||p.Id!=school||p.Revision<1||p.StartMonth<1||p.StartMonth>12||p.Months<1||p.Months>12||!System.Text.RegularExpressions.Regex.IsMatch(ScoliaSharing.YearKey(p.Year),"^[0-9]{4}-[0-9]{4}$"))throw new InvalidDataException("Configuration SCOLIA invalide ou d'une autre institution.");
  return p;
 }
 public static void Check(string root,string school,ScoliaEntry e,int start,int count,decimal fee){ScoliaPolicyData p=Load(root,school);if(p.Id!=school||p.Revision!=e.ConfigRevision||p.StartMonth!=start||p.Months!=count||ScoliaSharing.YearKey(p.Year)!=ScoliaSharing.YearKey(e.Annee))throw new InvalidOperationException("Configuration modifiée : reconnectez le compte avant d’enregistrer.");if(e.Kind=="Paiement"){var classes=p.Classes==null?new ScoliaPolicyClass[0]:p.Classes;var matches=classes.Where(x=>ScoliaLedger.Key(x.Code)==ScoliaLedger.Key(e.Promotion)).ToArray();if(matches.Length!=1||matches[0].Fee!=fee)throw new InvalidDataException("Classe ou tarif différent de la configuration actuelle.");}}
}

public sealed class ScoliaIncrementalReader {
 sealed class Cached {public long Length,Ticks;public ScoliaEntry Entry;}
 Dictionary<string,Cached> cached=new Dictionary<string,Cached>(StringComparer.OrdinalIgnoreCase);
 readonly string root,school,year;DateTime lastFull=DateTime.MinValue;ScoliaRoster roster;long rosterLength=-1,rosterTicks=-1;
 public int LastFilesRead{get;private set;}
 public ScoliaIncrementalReader(string r,string s,string y){root=Path.GetFullPath(r);school=s;year=y;}
 public Task<ScoliaSnapshot> ReadAsync(){return Task.Factory.StartNew(()=>Read());}
 public Task<ScoliaSnapshot> ReadAndCacheAsync(string localRoot,string role){
  return Task.Factory.StartNew(()=>{var result=Read();ScoliaInboxCache.Save(localRoot,school,year,role,result);return result;});
 }
 public ScoliaSnapshot Read(){lock(this){ScoliaPolicy.Load(root,school);bool full=(DateTime.UtcNow-lastFull).TotalMinutes>=5;int reads=0;
  string folder=Path.Combine(root,"journal");var folders=Directory.GetDirectories(root).Select(Path.GetFullPath);var next=new Dictionary<string,Cached>(StringComparer.OrdinalIgnoreCase);
  if(folders.Contains(folder,StringComparer.OrdinalIgnoreCase)){foreach(string file in Directory.GetFiles(folder,"*.xml")){var info=new FileInfo(file);Cached old;
   if(!full&&cached.TryGetValue(file,out old)&&old.Length==info.Length&&old.Ticks==info.LastWriteTimeUtc.Ticks)next[file]=old;
   else{next[file]=new Cached{Length=info.Length,Ticks=info.LastWriteTimeUtc.Ticks,Entry=ScoliaLedger.ReadVerifiedFile(file,school)};reads++;}
  }}
  if(cached.Keys.Any(k=>!next.ContainsKey(k)))throw new InvalidDataException("Des écritures précédemment reçues ont disparu du journal. Vérifiez le partage.");
  string rp=Path.Combine(root,"roster-"+school+"-"+ScoliaSharing.YearKey(year)+".xml");bool exists=Directory.GetFiles(root).Any(f=>String.Equals(f,rp,StringComparison.OrdinalIgnoreCase));
  ScoliaRoster fresh=roster;long length=-1,ticks=-1;
  if(exists){var info=new FileInfo(rp);length=info.Length;ticks=info.LastWriteTimeUtc.Ticks;if(full||roster==null||length!=rosterLength||ticks!=rosterTicks)fresh=ScoliaSharing.ReadRoster(root,school,year);}
  else{if(rosterLength>=0)throw new InvalidDataException("Le répertoire partagé a disparu.");if(fresh==null)fresh=new ScoliaRoster{SchoolId=school,Year=year};}
  cached=next;roster=fresh;rosterLength=length;rosterTicks=ticks;LastFilesRead=reads;if(full)lastFull=DateTime.UtcNow;
  return new ScoliaSnapshot{Entries=ScoliaFinance.Apply(root,school,cached.Values.Select(c=>c.Entry.Copy()).ToArray()),Roster=new ScoliaRoster{Version=fresh.Version,SchoolId=fresh.SchoolId,Year=fresh.Year,Imported=new List<string>(fresh.Imported),Payments=(fresh.Payments??new List<ScoliaEntry>()).Select(e=>e.Copy()).ToList(),PublishedUtc=fresh.PublishedUtc,AppliedFormat=fresh.AppliedFormat,AppliedPayments=fresh.AppliedPayments==null?null:fresh.AppliedPayments.Select(e=>e.Copy()).ToList(),Learners=fresh.Learners.Select(l=>l.Copy()).ToList()}};
 }}
}

public sealed class ScoliaImportItem{public string Id,Reference,Date,Nom,Promotion,Matricule,Etat,Observation,Mois,Origine,Importation;public string Categorie="SCOLARITE";public decimal Montant;}
public sealed class ScoliaImportResult{public int Imported,Existing,Errors;public List<ScoliaImportItem> Items=new List<ScoliaImportItem>();}

// Local inbox snapshots are durable drafts, not financial entries.
public sealed class ScoliaInboxArchive{
 public int Version=1;public string SchoolId,Year,Role,UpdatedUtc,Checksum;public ScoliaSnapshot Snapshot;
}
public static class ScoliaInboxCache{
 static readonly XmlSerializer archiveSerializer=new XmlSerializer(typeof(ScoliaInboxArchive));
 static readonly XmlSerializer snapshotSerializer=new XmlSerializer(typeof(ScoliaSnapshot));
 static readonly XmlSerializer learnerSerializer=new XmlSerializer(typeof(ScoliaLearner));
 static byte[] Serialize(object item,XmlSerializer serializer){using(var stream=new MemoryStream()){serializer.Serialize(stream,item);return stream.ToArray();}}
 static int RevisionNumber(ScoliaEntry e){return String.IsNullOrEmpty(e.Revision)?0:Int32.Parse(e.Revision,CultureInfo.InvariantCulture);}
 static string Fingerprint(ScoliaSnapshot snapshot){using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(Serialize(snapshot,snapshotSerializer)));}
 static string FilePath(string root,string school,string year,string role){
  Guid id;if(!Guid.TryParseExact(school,"N",out id)||!System.Text.RegularExpressions.Regex.IsMatch(ScoliaSharing.YearKey(year),"^[0-9]{4}-[0-9]{4}$")||role!="secretariat"&&role!="comptabilite"&&role!="mixte")throw new ArgumentException("Compte ou année du brouillon invalide.");
  return Path.Combine(root,"brouillons-recus",school+"-"+ScoliaSharing.YearKey(year)+"-"+role+".xml");
 }
 public static ScoliaInboxArchive Read(string root,string school,string year,string role){
  string path=FilePath(root,school,year,role);if(!File.Exists(path))return null;
  if(new FileInfo(path).Length>134217728)throw new InvalidDataException("Brouillons reçus trop volumineux.");
  var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null};
  using(var reader=XmlReader.Create(path,settings)){
   var a=(ScoliaInboxArchive)archiveSerializer.Deserialize(reader);
   if(a.Version!=1||a.SchoolId!=school||ScoliaSharing.YearKey(a.Year)!=ScoliaSharing.YearKey(year)||a.Role!=role||a.Snapshot==null||a.Snapshot.Roster==null||a.Snapshot.Roster.SchoolId!=school||ScoliaSharing.YearKey(a.Snapshot.Roster.Year)!=ScoliaSharing.YearKey(year)||a.Snapshot.Entries==null||a.Checksum!=Fingerprint(a.Snapshot))throw new InvalidDataException("Brouillons reçus endommagés ou d'un autre compte.");
   return a;
  }
 }
 public static bool Save(string root,string school,string year,string role,ScoliaSnapshot source){
  string path=FilePath(root,school,year,role);
  if(source==null||source.Roster==null||source.Entries==null||source.Roster.SchoolId!=school||ScoliaSharing.YearKey(source.Roster.Year)!=ScoliaSharing.YearKey(year)||source.Entries.Any(e=>e.SchoolId!=school))throw new InvalidDataException("Réception d'un autre établissement.");
  var snapshot=new ScoliaSnapshot{Roster=source.Roster,Entries=source.Entries.Where(e=>ScoliaSharing.YearKey(e.Annee)==ScoliaSharing.YearKey(year)).OrderBy(e=>e.Date,StringComparer.Ordinal).ThenBy(e=>e.Id).ToArray()};
  string fingerprint=Fingerprint(snapshot);Directory.CreateDirectory(Path.GetDirectoryName(path));
  using(var gate=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var old=Read(root,school,year,role);
   if(old!=null){
    if(old.Snapshot.Entries.Any(e=>snapshot.Entries.Any(n=>n.Id==e.Id&&(RevisionNumber(n)<RevisionNumber(e)||(RevisionNumber(n)==RevisionNumber(e)&&n.Checksum!=e.Checksum)))))throw new InvalidDataException("Une correction reçue a disparu ou a été remplacée. Les données locales sont conservées.");
    if(old.Snapshot.Entries.Any(e=>!snapshot.Entries.Any(n=>n.Id==e.Id)))throw new InvalidDataException("Des paiements précédemment reçus ont disparu du partage. Les brouillons locaux sont conservés.");
    if(String.IsNullOrEmpty(snapshot.Roster.PublishedUtc)&&!String.IsNullOrEmpty(old.Snapshot.Roster.PublishedUtc)){snapshot.Roster=old.Snapshot.Roster;fingerprint=Fingerprint(snapshot);}
    if(old.Checksum==fingerprint)return false;
   }
   var a=new ScoliaInboxArchive{SchoolId=school,Year=year,Role=role,UpdatedUtc=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture),Checksum=fingerprint,Snapshot=snapshot};
   string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var fs=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){byte[] bytes=Serialize(a,archiveSerializer);fs.Write(bytes,0,bytes.Length);fs.Flush(true);}if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}
   return true;
  }
 }
 public static bool SameLearner(ScoliaLearner a,ScoliaLearner b){return a!=null&&b!=null&&Serialize(a,learnerSerializer).SequenceEqual(Serialize(b,learnerSerializer));}
}

// Revision events are immutable; the original journal remains verifiable.
public sealed class ScoliaRevision {
 public string Id,SchoolId,Previous,Reason,AuthorId,AuthorName,Created,Checksum;
 public int Number;public ScoliaEntry Replacement;
}
public sealed class ScoliaFee {public string Code,Name,Promotion;public decimal Amount;public bool Active=true;}
public sealed class ScoliaFees {public string SchoolId,Year;public int Revision;public List<ScoliaFee> Items=new List<ScoliaFee>();}
public sealed class ScoliaReconciliationRow {public string Id,Reference,Matricule,Promotion,Category,Status,Detail;public decimal Shared,Secretariat,Difference;}
public static class ScoliaFinance {
 static readonly XmlSerializer revisionSerializer=new XmlSerializer(typeof(ScoliaRevision));
 static readonly XmlSerializer feesSerializer=new XmlSerializer(typeof(ScoliaFees));
 public static string CategoryKey(ScoliaEntry e){return String.IsNullOrEmpty(e.Category)?"SCOLARITE":ScoliaLedger.Key(e.Category);}
 public static bool IsTuition(ScoliaEntry e){return CategoryKey(e)=="SCOLARITE";}
 public static bool CommonMatch(ScoliaEntry a,ScoliaEntry b){return a.Kind=="Paiement"&&b.Kind=="Paiement"&&a.Cancelled!="1"&&ScoliaLedger.SameLearner(a,b)&&CategoryKey(a)==CategoryKey(b)&&a.Date==b.Date&&a.Montant==b.Montant;}
 static string Digest(ScoliaRevision r){var old=r.Checksum;r.Checksum=null;try{using(var sha=SHA256.Create())return Convert.ToBase64String(sha.ComputeHash(ScoliaLedger.Bytes(r,revisionSerializer)));}finally{r.Checksum=old;}}
 static T ReadXml<T>(string path,XmlSerializer serializer){if(new FileInfo(path).Length>4194304)throw new InvalidDataException("Fichier financier trop volumineux.");using(var reader=XmlReader.Create(path,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))return (T)serializer.Deserialize(reader);}
 public static ScoliaRevision[] History(string root,string school){
  var folder=Path.Combine(root,"corrections");if(!Directory.Exists(folder))return new ScoliaRevision[0];
  var result=new List<ScoliaRevision>();foreach(var file in Directory.GetFiles(folder,"*.xml")){
   var r=ReadXml<ScoliaRevision>(file,revisionSerializer);Guid id;
   if(r.SchoolId!=school||!Guid.TryParseExact(r.Id,"N",out id)||r.Number<1||Path.GetFileNameWithoutExtension(file)!=r.Id+"-"+r.Number.ToString("D8")||r.Replacement==null||r.Replacement.Id!=r.Id||r.Replacement.SchoolId!=school||r.Checksum!=Digest(r))throw new InvalidDataException("Historique de correction invalide.");result.Add(r);
  }return result.OrderBy(r=>r.Number).ThenBy(r=>r.Id).ToArray();
 }
 public static ScoliaEntry[] Apply(string root,string school,ScoliaEntry[] original){
  var map=original.ToDictionary(e=>e.Id,e=>e.Copy());
  foreach(var r in History(root,school)){
   ScoliaEntry before;if(!map.TryGetValue(r.Id,out before)||before.Checksum!=r.Previous||r.Number!=(String.IsNullOrEmpty(before.Revision)?1:Int32.Parse(before.Revision,CultureInfo.InvariantCulture)+1))throw new InvalidDataException("Chaîne des corrections incomplète. Vérifiez le partage.");
   var revisedEntry=r.Replacement.Copy();revisedEntry.Revision=r.Number.ToString(CultureInfo.InvariantCulture);revisedEntry.Checksum=r.Checksum;revisedEntry.OriginalParts=before.OriginalParts!=null&&before.OriginalParts.Count>0?before.OriginalParts:before.Parts;revisedEntry.CorrectionReason=r.Reason;revisedEntry.CorrectionAuthor=r.AuthorName;map[revisedEntry.Id]=revisedEntry;
  }return map.Values.OrderBy(e=>e.Created,StringComparer.Ordinal).ThenBy(e=>e.Id).ToArray();
 }
 public static ScoliaFees Fees(string root,string school,string year){
  ScoliaPolicy.Load(root,school);string path=Path.Combine(root,"frais-"+ScoliaSharing.YearKey(year)+".xml");
  if(!File.Exists(path))return new ScoliaFees{SchoolId=school,Year=year};var f=ReadXml<ScoliaFees>(path,feesSerializer);ValidateFees(f,school,year);return f;
 }
 static void ValidateFees(ScoliaFees f,string school,string year){
  if(f==null||f.SchoolId!=school||ScoliaSharing.YearKey(f.Year)!=ScoliaSharing.YearKey(year)||f.Items==null||f.Items.Count>200)throw new InvalidDataException("Catalogue des frais invalide.");
  var seen=new HashSet<string>();foreach(var c in f.Items)if(!System.Text.RegularExpressions.Regex.IsMatch(c.Code??"","^[A-Z0-9_]{2,24}$")||c.Code=="SCOLARITE"||!seen.Add(c.Code)||String.IsNullOrWhiteSpace(c.Name)||c.Name.Length>100||c.Amount<=0||c.Amount!=Math.Truncate(c.Amount)||c.Amount>100000000000m||(!String.IsNullOrEmpty(c.Promotion)&&!System.Text.RegularExpressions.Regex.IsMatch(c.Promotion,"^[A-Z0-9]{4}$")))throw new InvalidDataException("Code unique, libellé et tarif entier positif obligatoires ; promotion vide = toutes.");
 }
 public static void SaveFees(string root,string school,ScoliaFees f,int expected){
  ValidateFees(f,school,f.Year);using(var gate=new FileStream(Path.Combine(root,"journal.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var policy=ScoliaPolicy.Load(root,school);if(ScoliaSharing.YearKey(f.Year)!=ScoliaSharing.YearKey(policy.Year)||f.Items.Any(c=>!String.IsNullOrEmpty(c.Promotion)&&!policy.Classes.Any(p=>p.Code==c.Promotion)))throw new InvalidOperationException("Année ou promotion différente de la configuration.");
   var old=Fees(root,school,f.Year);if(old.Revision!=expected)throw new InvalidOperationException("Catalogue modifié sur un autre poste. Rouvrez-le.");
   var all=ScoliaLedger.Read(root,school);foreach(var c in old.Items){var n=f.Items.FirstOrDefault(x=>x.Code==c.Code);if(all.Any(e=>CategoryKey(e)==c.Code&&ScoliaSharing.YearKey(e.Annee)==ScoliaSharing.YearKey(f.Year))&&(n==null||n.Amount!=c.Amount||n.Promotion!=c.Promotion))throw new InvalidOperationException("Catégorie déjà utilisée : conservez son tarif et sa promotion ; désactivez-la ou créez un nouveau code.");}
   f.Revision=old.Revision+1;var path=Path.Combine(root,"frais-"+ScoliaSharing.YearKey(f.Year)+".xml");Replace(path,ScoliaLedger.Bytes(f,feesSerializer));
  }
 }
 static void Replace(string path,byte[] bytes){var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(tmp,bytes);if(File.Exists(path))File.Replace(tmp,path,path+".bak");else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
 public static void Allocate(string root,string school,ScoliaEntry e,ScoliaEntry[] all,int start,int months,decimal fee){
  if(e.Kind!="Paiement")return;
  var roster=ScoliaSharing.ReadRoster(root,school,e.Annee);var matched=roster.Learners.Where(l=>!String.IsNullOrEmpty(e.Matricule)?ScoliaLedger.Key(l.Matricule)==ScoliaLedger.Key(e.Matricule):ScoliaSharing.Matches(e,l)).ToArray();
  if(matched.Length>1)throw new InvalidOperationException("Apprenant en double dans le répertoire.");var learner=matched.FirstOrDefault();
  if(!String.IsNullOrEmpty(e.Matricule)&&(learner==null||!ScoliaSharing.Matches(e,learner)))throw new InvalidOperationException("Dossier modifié : importez les apprenants et sélectionnez à nouveau la personne.");
  var payments=all.Where(x=>x.Id!=e.Id&&x.Kind=="Paiement"&&x.Cancelled!="1"&&CategoryKey(x)==CategoryKey(e)&&ScoliaLedger.SameLearner(x,e)).ToArray();
  e.Parts.Clear();
  if(!IsTuition(e)){
   var category=Fees(root,school,e.Annee).Items.FirstOrDefault(c=>c.Code==CategoryKey(e)&&(c.Active||all.Any(x=>x.Id==e.Id&&CategoryKey(x)==c.Code)));
   if(category==null||(!String.IsNullOrEmpty(category.Promotion)&&ScoliaLedger.Key(category.Promotion)!=ScoliaLedger.Key(e.Promotion)))throw new InvalidOperationException("Catégorie inactive ou non applicable à cette promotion.");
   e.CategoryName=category.Name;if(e.Montant>category.Amount-payments.Sum(x=>x.Montant))throw new InvalidOperationException("Montant supérieur au reste de cette catégorie de frais.");return;
  }
  if(fee<=0||fee!=Math.Truncate(fee)||(learner!=null&&learner.Mensuel!=fee))throw new InvalidOperationException("Tarif mensuel différent du secrétariat.");
  decimal opening=0;if(learner!=null)foreach(string month in ScoliaLedger.Schedule(start,months))opening+=ScoliaSharing.OpeningPaid(roster,learner,all,month)+learner.Covered.Where(p=>p.Mois==month).Sum(p=>p.Montant);
  if(e.Montant>(learner==null?fee*months:learner.Annuel)-opening-payments.Sum(x=>x.Montant))throw new InvalidOperationException("Montant supérieur au reste annuel.");
  decimal remaining=e.Montant;foreach(string month in ScoliaLedger.Schedule(start,months)){
   decimal used=payments.SelectMany(x=>x.Parts).Where(p=>p.Mois==month).Sum(p=>p.Montant);
   if(learner!=null)used+=ScoliaSharing.OpeningPaid(roster,learner,all,month)+learner.Covered.Where(p=>p.Mois==month).Sum(p=>p.Montant);
   decimal part=Math.Min(Math.Max(0,fee-used),remaining);if(part>0){e.Parts.Add(new ScoliaPart{Mois=month,Montant=part});remaining-=part;}
  }if(remaining!=0)throw new InvalidOperationException("Montant supérieur au reste disponible des mensualités.");
 }
 public static ScoliaEntry Correct(string root,string school,string id,string expected,decimal amount,string date,string reason,string authorId,string authorName,int configRevision){
  if(String.IsNullOrWhiteSpace(reason)||reason.Trim().Length<5||reason.Length>500||String.IsNullOrWhiteSpace(authorId)||String.IsNullOrWhiteSpace(authorName))throw new ArgumentException("Motif détaillé et auteur obligatoires.");
  if(amount<0||amount!=Math.Truncate(amount)||amount>100000000000m)throw new ArgumentException("Montant entier positif ou zéro pour annuler.");
  using(var gate=new FileStream(Path.Combine(root,"journal.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
   var policy=ScoliaPolicy.Load(root,school);if(policy.Revision!=configRevision)throw new InvalidOperationException("Configuration modifiée. Reconnectez-vous.");
   var all=ScoliaLedger.Read(root,school);var before=all.SingleOrDefault(e=>e.Id==id);if(before==null||before.Checksum!=expected)throw new InvalidOperationException("Ce reçu a changé. Actualisez avant de corriger.");
   if(ScoliaSharing.YearKey(before.Annee)!=ScoliaSharing.YearKey(policy.Year)||before.Cancelled=="1")throw new InvalidOperationException("Année ancienne ou reçu déjà annulé.");
   var correctedEntry=before.Copy();correctedEntry.Montant=amount;correctedEntry.Date=date;correctedEntry.Cancelled=amount==0?"1":null;
   if(amount>0){decimal fee=before.Kind=="Paiement"?policy.Classes.Single(c=>ScoliaLedger.Key(c.Code)==ScoliaLedger.Key(correctedEntry.Promotion)).Fee:0;Allocate(root,school,correctedEntry,all,policy.StartMonth,policy.Months,fee);ScoliaLedger.Validate(correctedEntry,school);}else{correctedEntry.Parts.Clear();DateTime parsed;if(!DateTime.TryParseExact(date,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out parsed)||parsed>DateTime.Today)throw new ArgumentException("Date incorrecte.");}
   int number=String.IsNullOrEmpty(before.Revision)?1:Int32.Parse(before.Revision,CultureInfo.InvariantCulture)+1;
   var r=new ScoliaRevision{Id=id,SchoolId=school,Number=number,Previous=before.Checksum,Reason=reason.Trim(),AuthorId=authorId,AuthorName=authorName,Created=DateTime.UtcNow.ToString("o"),Replacement=correctedEntry};
   r.Checksum=Digest(r);Directory.CreateDirectory(Path.Combine(root,"corrections"));ScoliaLedger.Atomic(Path.Combine(root,"corrections",id+"-"+number.ToString("D8")+".xml"),ScoliaLedger.Bytes(r,revisionSerializer));
   correctedEntry.Revision=number.ToString(CultureInfo.InvariantCulture);correctedEntry.Checksum=r.Checksum;correctedEntry.OriginalParts=before.OriginalParts!=null&&before.OriginalParts.Count>0?before.OriginalParts:before.Parts;correctedEntry.CorrectionReason=r.Reason;correctedEntry.CorrectionAuthor=authorName;return correctedEntry;
  }
 }
 public static ScoliaReconciliationRow[] Reconcile(ScoliaSnapshot snapshot){
  var rows=new List<ScoliaReconciliationRow>();var local=snapshot.Roster.AppliedFormat=="1"?(snapshot.Roster.AppliedPayments??new List<ScoliaEntry>()):(snapshot.Roster.Payments??new List<ScoliaEntry>());
  foreach(var e in snapshot.Entries.Where(e=>e.Kind=="Paiement"&&ScoliaSharing.YearKey(e.Annee)==ScoliaSharing.YearKey(snapshot.Roster.Year))){
   var l=local.FirstOrDefault(x=>x.Id==e.Id);var row=new ScoliaReconciliationRow{Id=e.Id,Reference=e.Reference,Matricule=e.Matricule,Promotion=e.Promotion,Category=CategoryKey(e),Shared=e.Montant,Secretariat=l==null?0:l.Montant};row.Difference=row.Shared-row.Secretariat;
   if(l==null){row.Status=snapshot.Roster.Imported.Contains(e.Id)&&String.IsNullOrEmpty(e.Revision)?"Import reconnu":"À importer";row.Detail="Actualisez la publication du secrétariat pour comparer les montants.";}
   else if(row.Difference!=0||l.Date!=e.Date||l.Parts.Count!=e.Parts.Count||l.Parts.Any(p=>!e.Parts.Any(q=>q.Mois==p.Mois&&q.Montant==p.Montant))){row.Status="Écart";row.Detail="Montant, date ou affectation à synchroniser.";}
   else{row.Status=e.Cancelled=="1"?"Annulé synchronisé":"Rapproché";row.Detail="Même reçu et même affectation.";}rows.Add(row);
  }
  foreach(var e in snapshot.Roster.Payments.Where(e=>!snapshot.Entries.Any(x=>x.Id==e.Id)))rows.Add(new ScoliaReconciliationRow{Id=e.Id,Reference=e.Reference,Matricule=e.Matricule,Promotion=e.Promotion,Category=CategoryKey(e),Secretariat=e.Montant,Difference=-e.Montant,Status="Secrétariat seulement",Detail="Importer les paiements dans le compte comptable."});
  return rows.OrderBy(r=>r.Promotion).ThenBy(r=>r.Reference).ToArray();
 }
}
