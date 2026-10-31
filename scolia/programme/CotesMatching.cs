using System;using System.Collections.Generic;using System.Globalization;using System.Text;
public sealed class ITMGradeLearner{public string Id{get;set;}public string Name{get;set;}}
public sealed class ITMGradeInput{public string Name{get;set;}public decimal Grade{get;set;}public int SourceRow{get;set;}}
public sealed class ITMGradeMatch{public string Id{get;set;}public string Name{get;set;}public decimal Grade{get;set;}public int SourceRow{get;set;}public string Problem{get;set;}}
public static class ITMGradeMatcher{
 public static string Key(string name){var output=new StringBuilder();bool space=false;foreach(char c in (name??"").Normalize(NormalizationForm.FormD)){if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;if(Char.IsLetterOrDigit(c)){output.Append(Char.ToUpperInvariant(c));space=false;}else if(!space&&output.Length>0){output.Append(' ');space=true;}}return output.ToString().Trim();}
 public static ITMGradeMatch[] Match(ITMGradeLearner[] learners,ITMGradeInput[] input,decimal maximum){
  if(maximum<=0)throw new ArgumentException("Maximum de points invalide.");
  var ids=new HashSet<string>();var names=new Dictionary<string,List<ITMGradeLearner>>();foreach(var learner in learners){if(String.IsNullOrWhiteSpace(learner.Id)||!ids.Add(learner.Id))throw new ArgumentException("Matricules absents ou répétés dans la promotion.");string key=Key(learner.Name);if(!names.ContainsKey(key))names[key]=new List<ITMGradeLearner>();names[key].Add(learner);}
  var counts=new Dictionary<string,int>();foreach(var row in input){string key=Key(row.Name);if(!counts.ContainsKey(key))counts[key]=0;counts[key]++;}
  var result=new List<ITMGradeMatch>();foreach(var row in input){string key=Key(row.Name);var match=new ITMGradeMatch{Name=row.Name,Grade=row.Grade,SourceRow=row.SourceRow,Problem=""};
   if(key.Length==0)match.Problem="Nom vide";else if(counts[key]>1)match.Problem="Nom répété dans le fichier de cotes";else if(!names.ContainsKey(key))match.Problem="Apprenant absent de la promotion";else if(names[key].Count!=1)match.Problem="Homonymes : correspondance à préciser";else if(row.Grade<0||row.Grade>maximum)match.Problem="Cote hors du barème";else match.Id=names[key][0].Id;
   result.Add(match);
  }return result.ToArray();
 }
}

public sealed class ITMDeliberationResult {
 public decimal Total{get;set;}public decimal Maximum{get;set;}public decimal Percentage{get;set;}
 public int Missing{get;set;}public int Failed{get;set;}public string Decision{get;set;}
}
public static class ITMDeliberation {
 public static ITMDeliberationResult Evaluate(object[] grades,decimal[] maxima){
  if(grades==null||maxima==null||grades.Length==0||grades.Length!=maxima.Length)throw new ArgumentException("Cours et barèmes incohérents.");
  var result=new ITMDeliberationResult();
  for(int i=0;i<grades.Length;i++){
   if(maxima[i]<=0)throw new ArgumentException("Barème de cours invalide.");result.Maximum+=maxima[i];
   object value=grades[i];if(value==null||String.IsNullOrWhiteSpace(Convert.ToString(value,CultureInfo.InvariantCulture))){result.Missing++;continue;}
   if(value is bool)throw new ArgumentException("Cote booléenne invalide au cours "+(i+1)+".");
   decimal grade;
   if(value is string){string text=(string)value;if(!Decimal.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out grade)&&!Decimal.TryParse(text,NumberStyles.Float,CultureInfo.GetCultureInfo("fr-FR"),out grade))throw new ArgumentException("Cote non numérique au cours "+(i+1)+".");}
   else{try{grade=Convert.ToDecimal(value,CultureInfo.InvariantCulture);}catch{throw new ArgumentException("Cote non numérique au cours "+(i+1)+".");}}
   if(grade<0||grade>maxima[i])throw new ArgumentException("Cote hors barème au cours "+(i+1)+".");
   result.Total+=grade;if(grade<maxima[i]*.5m)result.Failed++;
  }
  result.Percentage=result.Total*100m/result.Maximum;
  // A missing grade takes priority over an individual failure; compare before rounding.
  result.Decision=result.Missing>0?(result.Percentage>=50m?"AA":"D"):(result.Failed>0?"A":"P");return result;
 }
}

public sealed class ITMGridOrderResult {
 public object[,] Matrix{get;set;}public string[] Names{get;set;}public bool Changed{get;set;}
}
public static class ITMGridAlphabetical {
 private sealed class Row {public int Index;public string Name;public string Key;public int Rank;}
 public static ITMGridOrderResult Sort(Array source,string[] orderedNames){
  if(source==null||source.Rank!=2||source.GetLength(1)<2)throw new ArgumentException("Plage des apprenants invalide.");
  int n=source.GetLength(0),cols=source.GetLength(1),r0=source.GetLowerBound(0),c0=source.GetLowerBound(1);
  var ranks=new Dictionary<string,int>();if(orderedNames!=null)for(int i=0;i<orderedNames.Length;i++){string key=ITMGradeMatcher.Key(orderedNames[i]);if(key.Length>0&&!ranks.ContainsKey(key))ranks.Add(key,i);}
  var seen=new HashSet<string>();var rows=new List<Row>();
  for(int i=0;i<n;i++){
   string name=Convert.ToString(source.GetValue(i+r0,c0+1),CultureInfo.InvariantCulture),key=ITMGradeMatcher.Key(name);
   if(name.StartsWith("=",StringComparison.Ordinal))throw new ArgumentException("Les noms de la grille doivent être des valeurs, pas des formules.");
   if(key.Length>0&&!seen.Add(key))throw new ArgumentException("La grille contient des noms complets répétés.");
   int rank;rows.Add(new Row{Index=i,Name=name,Key=key,Rank=ranks.TryGetValue(key,out rank)?rank:Int32.MaxValue});
  }
  var comparer=StringComparer.Create(CultureInfo.GetCultureInfo("fr-FR"),true);
  rows.Sort(delegate(Row a,Row b){
   if(a.Key.Length==0||b.Key.Length==0){int empty=(a.Key.Length==0?1:0).CompareTo(b.Key.Length==0?1:0);return empty!=0?empty:a.Index.CompareTo(b.Index);}
   // The promotion list carries separate Nom/PostNom/Prenom keys. Otherwise use the full name.
   int order=a.Rank.CompareTo(b.Rank);if(order==0)order=comparer.Compare(a.Key,b.Key);return order!=0?order:a.Index.CompareTo(b.Index);
  });
  var output=new object[n,cols];var names=new List<string>();bool changed=false;int ordinal=0;
  for(int i=0;i<n;i++){
   Row row=rows[i];if(row.Index!=i)changed=true;
   for(int col=0;col<cols;col++)output[i,col]=source.GetValue(row.Index+r0,col+c0);
   object number=row.Key.Length==0?null:(object)(double)(++ordinal);
   if(!Object.Equals(output[i,0],number))changed=true;output[i,0]=number;
   if(row.Key.Length>0)names.Add(row.Name);
  }
  return new ITMGridOrderResult{Matrix=output,Names=names.ToArray(),Changed=changed};
 }
}
