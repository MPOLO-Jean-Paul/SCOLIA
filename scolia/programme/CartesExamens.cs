using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;

using System.Security;
using System.Text;

// ZIP stored entries: portable on Windows PowerShell 5.1 without extra assemblies.
internal sealed class CardZip : IDisposable {
 private readonly BinaryWriter w; private readonly List<byte[]> entries=new List<byte[]>();
 public CardZip(Stream stream){w=new BinaryWriter(stream);}
 private static uint Crc(byte[] bytes){uint c=0xffffffff;foreach(byte b in bytes){c^=b;for(int i=0;i<8;i++)c=(c&1)!=0?0xedb88320^(c>>1):c>>1;}return ~c;}
 public void Add(string name,byte[] data){byte[] n=Encoding.UTF8.GetBytes(name);uint crc=Crc(data),at=(uint)w.BaseStream.Position;w.Write(0x04034b50u);w.Write((ushort)20);w.Write((ushort)0x800);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)33);w.Write(crc);w.Write((uint)data.Length);w.Write((uint)data.Length);w.Write((ushort)n.Length);w.Write((ushort)0);w.Write(n);w.Write(data);
 using(var ms=new MemoryStream())using(var c=new BinaryWriter(ms)){c.Write(0x02014b50u);c.Write((ushort)20);c.Write((ushort)20);c.Write((ushort)0x800);c.Write((ushort)0);c.Write((ushort)0);c.Write((ushort)33);c.Write(crc);c.Write((uint)data.Length);c.Write((uint)data.Length);c.Write((ushort)n.Length);c.Write((ushort)0);c.Write((ushort)0);c.Write((ushort)0);c.Write((ushort)0);c.Write(0u);c.Write(at);c.Write(n);entries.Add(ms.ToArray());}}
 public void Dispose(){uint at=(uint)w.BaseStream.Position;foreach(byte[] e in entries)w.Write(e);uint size=(uint)w.BaseStream.Position-at;w.Write(0x06054b50u);w.Write((ushort)0);w.Write((ushort)0);w.Write((ushort)entries.Count);w.Write((ushort)entries.Count);w.Write(size);w.Write(at);w.Write((ushort)0);w.Flush();}
}
public sealed class ITMExamLearner {
    public string Nom {get;set;} public string PostNom {get;set;} public string Prenom {get;set;}
    public string Promotion {get;set;} public string Matricule {get;set;}
}
public sealed class ITMExamBatch {
    public ITMExamLearner[] Learners {get;set;}
    public string[] Header {get;set;}
    public string Session {get;set;} public string AcademicYear {get;set;} public string BatchId {get;set;}
    public string ResourceFolder {get;set;} public string Institution {get;set;} public string LogoPath {get;set;} public bool CutMarks {get;set;} public int Copies {get;set;}
    public ITMExamBatch(){Copies=1;CutMarks=true;}
}
public static class ITMExamCards {
    public const double CardWidth=92,CardHeight=63,Margin=10,ColumnGap=6,RowGap=7;
    private static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    public static int CardCount(ITMExamBatch b){Validate(b);return checked(b.Learners.Length*b.Copies);}
    public static int PageCount(ITMExamBatch b){return (CardCount(b)+7)/8;}
    public static string PromotionCode(string value){string s=(value??"").Trim().ToUpperInvariant().Replace(" ","").Replace("-","");return s=="TML1"?"TLM1":s;}
    public static Color BandColor(string promotion){
        switch(PromotionCode(promotion)){
            case "TLM1":return Color.FromArgb(0,105,180);case "ACC1":return Color.FromArgb(244,206,20);
            case "ACC2":return Color.FromArgb(130,82,181);case "ACC3":return Color.FromArgb(183,226,121);
            case "INF1":return Color.FromArgb(137,207,240);case "INF2":return Color.FromArgb(243,181,200);
            case "INF3":return Color.FromArgb(79,154,114);default:return Color.FromArgb(220,225,232);
        }
    }
    private static Color BandInk(Color c){return (c.R*.299+c.G*.587+c.B*.114)>130?Color.FromArgb(20,30,45):Color.White;}
    public static void Validate(ITMExamBatch b){
        if(b==null||b.Learners==null||b.Learners.Length==0)throw new InvalidOperationException("Sélectionnez au moins un apprenant.");
        if(b.Copies<1||b.Copies>5)throw new InvalidOperationException("Choisissez de 1 à 5 exemplaires par apprenant.");
        if(b.Header==null||b.Header.Length!=7)throw new InvalidOperationException("L'en-tête doit contenir sept mentions.");
        if(String.IsNullOrWhiteSpace(b.Session)||b.Session.Length>80)throw new InvalidOperationException("Session invalide.");
        if(String.IsNullOrWhiteSpace(b.AcademicYear)||b.AcademicYear.Length>40)throw new InvalidOperationException("Année scolaire invalide.");
        if(!File.Exists(b.LogoPath))throw new FileNotFoundException("Logo de l'école introuvable.",b.LogoPath);
        foreach(string h in b.Header)if(String.IsNullOrWhiteSpace(h)||h.Length>160)throw new InvalidOperationException("Mention d'en-tête vide ou trop longue (160 caractères maximum).");
        ConfigureFonts(b);
        var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var a in b.Learners){
            if(a==null||String.IsNullOrWhiteSpace(a.Nom)||String.IsNullOrWhiteSpace(a.PostNom)||String.IsNullOrWhiteSpace(a.Prenom)||String.IsNullOrWhiteSpace(a.Promotion))throw new InvalidOperationException("Une identité ou une promotion est incomplète.");
            if(!String.IsNullOrWhiteSpace(a.Matricule)&&!ids.Add(a.Matricule.Trim()))throw new InvalidOperationException("Matricule répété dans la sélection : "+a.Matricule);
            foreach(string name in new[]{a.Nom,a.PostNom,a.Prenom,a.Promotion})if(name.Length>160)throw new InvalidOperationException("Texte de carte trop long : "+a.Matricule);
        }
    }
    private static string fontFolder;
    private static System.Drawing.Text.PrivateFontCollection fonts;
    private static int[] regularWidths,boldWidths;
    private static void ConfigureFonts(ITMExamBatch b){
        string dir=Path.Combine(String.IsNullOrEmpty(b.ResourceFolder)?Path.GetDirectoryName(b.LogoPath):b.ResourceFolder,"Fonts");
        if(fontFolder==dir&&fonts!=null)return;
        foreach(string name in new[]{"Regular","Bold"})if(!File.Exists(Path.Combine(dir,"Roboto-"+name+".ttf"))||!File.Exists(Path.Combine(dir,"Roboto-"+name+".metrics")))throw new FileNotFoundException("Police Roboto manquante. Extrayez tout le paquet de l'application.");
        var collection=new System.Drawing.Text.PrivateFontCollection();collection.AddFontFile(Path.Combine(dir,"Roboto-Regular.ttf"));collection.AddFontFile(Path.Combine(dir,"Roboto-Bold.ttf"));
        regularWidths=ReadWidths(Path.Combine(dir,"Roboto-Regular.metrics"));boldWidths=ReadWidths(Path.Combine(dir,"Roboto-Bold.metrics"));if(fonts!=null)fonts.Dispose();fonts=collection;fontFolder=dir;
    }
    private static int[] ReadWidths(string path){var widths=new int[256];using(var r=new BinaryReader(File.OpenRead(path)))for(int i=0;i<256;i++)widths[i]=r.ReadUInt16();return widths;}
    private static Font Roboto(double size,bool bold){return new Font(fonts.Families[0],(float)size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Point);}
    private static string Json(string s){return "\""+Clean(s).Replace("\\","\\\\").Replace("\"","\\\"")+"\"";}
    public static string QrPayload(ITMExamBatch b,ITMExamLearner a){return "{\"ecole\":"+Json(String.IsNullOrEmpty(b.Institution)?b.Header[5]:b.Institution)+",\"nom\":"+Json(a.Nom)+",\"postnom\":"+Json(a.PostNom)+",\"prenom\":"+Json(a.Prenom)+",\"classe\":"+Json(PromotionCode(a.Promotion))+",\"session\":"+Json(b.Session)+",\"annee\":"+Json(b.AcademicYear)+"}";}
    private static System.Reflection.Assembly qrAssembly;
    private static readonly Dictionary<string,bool[,]> qrCache=new Dictionary<string,bool[,]>();
    private static bool[,] Qr(ITMExamBatch b,ITMExamLearner a){
        string payload=QrPayload(b,a);bool[,] cached;lock(qrCache){if(qrCache.TryGetValue(payload,out cached))return cached;}
        if(qrAssembly==null){string root=Path.GetDirectoryName(String.IsNullOrEmpty(b.ResourceFolder)?Path.GetDirectoryName(b.LogoPath):b.ResourceFolder);qrAssembly=System.Reflection.Assembly.LoadFrom(Path.Combine(root,"Lib/QRCoder.dll"));}
        Type type=qrAssembly.GetType("QRCoder.QRCodeGenerator",true),ecc=type.GetNestedType("ECCLevel"),eci=type.GetNestedType("EciMode");object generator=Activator.CreateInstance(type),data=null;
        try{var method=type.GetMethod("CreateQrCode",new[]{typeof(string),ecc,typeof(bool),typeof(bool),eci,typeof(int)});data=method.Invoke(generator,new[]{(object)payload,Enum.Parse(ecc,"M"),true,false,Enum.ToObject(eci,26),-1});
            var matrix=(System.Collections.Generic.List<System.Collections.BitArray>)data.GetType().GetProperty("ModuleMatrix").GetValue(data,null);var result=new bool[matrix.Count,matrix.Count];for(int y=0;y<matrix.Count;y++)for(int x=0;x<matrix.Count;x++)result[x,y]=matrix[y][x];lock(qrCache){if(qrCache.Count>=2048)qrCache.Clear();qrCache[payload]=result;}return result;
        }catch(System.Reflection.TargetInvocationException e){throw new InvalidOperationException("Impossible de créer le QR de cette carte.",e.InnerException);}finally{if(data is IDisposable)((IDisposable)data).Dispose();((IDisposable)generator).Dispose();}
    }
    private static void DrawQr(Graphics g,bool[,] matrix,float x,float y,float size){int n=matrix.GetLength(0);using(var white=new SolidBrush(Color.White))g.FillRectangle(white,x,y,size,size);using(var ink=new SolidBrush(Color.FromArgb(23,40,60))){float unit=size/n;for(int j=0;j<n;j++)for(int i=0;i<n;i++)if(matrix[i,j])g.FillRectangle(ink,x+i*unit,y+j*unit,unit+.005f,unit+.005f);}}
    private static string QrPdf(bool[,] matrix,double x,double y,double size){var s=new StringBuilder("0.09 0.16 0.24 rg\n");int n=matrix.GetLength(0);double unit=size/n;for(int j=0;j<n;j++)for(int i=0;i<n;i++)if(matrix[i,j])s.Append(RectanglePdf(x+i*unit,y+j*unit,unit+.002,unit+.002)+" f\n");return s.ToString();}
    private static byte[] QrPng(ITMExamBatch b,ITMExamLearner a){var matrix=Qr(b,a);int n=matrix.GetLength(0);using(var bitmap=new Bitmap(n*8,n*8))using(var g=Graphics.FromImage(bitmap))using(var m=new MemoryStream()){g.Clear(Color.White);DrawQr(g,matrix,0,0,n*8);bitmap.Save(m,ImageFormat.Png);return m.ToArray();}}
    private static void Decorations(Graphics g){
        var state=g.Save();g.SetClip(new RectangleF(0,0,92,56));
        using(var soft=new SolidBrush(Color.FromArgb(245,248,251)))g.FillRectangle(soft,0,0,92,19);
        using(var light=new Pen(Color.FromArgb(237,243,248),.18f))for(int i=0;i<6;i++)g.DrawArc(light,new RectangleF(61+i*2,31+i*2,38,38),180,100);
        using(var accent=new Pen(Color.FromArgb(67,110,149),.25f))g.DrawLine(accent,4,19.1f,88,19.1f);
        using(var ink=new Pen(Color.FromArgb(35,59,83),.2f))g.DrawLine(ink,4,32.0f,60,32.0f);
        g.Restore(state);
    }

    private sealed class TextPart {public string Text;public double X,Y,Size;public bool Bold;public Color Ink;}
    private static string Clean(string s){var result=new StringBuilder();foreach(char c in s??""){if(c=='\r'||c=='\n'||c=='\t'){result.Append(' ');}else if(!Char.IsControl(c)){result.Append(c);}}return result.ToString().Trim();}
    // Roboto glyph advances, rounded conservatively for shared vector layout.
    private static double Width(string s,double size){double sum=0;foreach(byte c in Encoding.GetEncoding(1252).GetBytes(Clean(s)))sum+=(boldWidths==null?600:boldWidths[c]);return sum/1000.0*size*25.4/72.0;}
    private static double Fit(string s,double width,double size,double minimum){while(size>minimum&&Width(s,size)>width)size-=.1;if(Width(s,size)>width)throw new InvalidOperationException("Mention trop longue pour la carte : "+s);return Math.Round(size,1);}
    private static List<string> Wrap(string text,double width,double size){
        var lines=new List<string>();var current=new StringBuilder();
        foreach(char ch in Clean(text)){
            if(current.Length>0&&Width(current.ToString()+ch,size)>width){
                int split=current.ToString().LastIndexOf(' ');
                if(split>0){lines.Add(current.ToString(0,split));string rest=current.ToString(split+1,current.Length-split-1);current.Clear();current.Append(rest);}
                else{lines.Add(current.ToString());current.Clear();}
            }
            current.Append(ch);
        }
        if(current.Length>0)lines.Add(current.ToString().Trim());return lines;
    }
    private static List<TextPart> Parts(ITMExamBatch b,ITMExamLearner a){
        var p=new List<TextPart>();Color ink=Color.FromArgb(28,43,64);
        for(int i=0;i<8;i++){
            string s=i<7?Clean(b.Header[i]):"Année scolaire "+Clean(b.AcademicYear);double size=Fit(s,73,4.9,3.8);
            p.Add(new TextPart{Text=s,X=15+(73-Width(s,size))/2,Y=4.2+i*1.85,Size=size,Bold=(i==0||i==5||i==7),Ink=ink});
        }
        string session=Clean(b.Session).ToUpperInvariant();double fs=Fit(session,84,11,7);
        p.Add(new TextPart{Text=session,X=(CardWidth-Width(session,fs))/2,Y=23.7,Size=fs,Bold=true,Ink=Color.FromArgb(0,70,125)});
        string title="CARTE D'ACCÈS";double titleSize=10;p.Add(new TextPart{Text=title,X=(CardWidth-Width(title,titleSize))/2,Y=29.4,Size=titleSize,Bold=true,Ink=ink});
        string[] labels={"NOM","POST-NOM","PRÉNOM","CLASSE"},values={a.Nom,a.PostNom,a.Prenom,PromotionCode(a.Promotion)};
        for(int i=0;i<4;i++){
            double y=37+i*5.6;double size=7.6;List<string> lines=Wrap(values[i],42,size);
            while(lines.Count>2&&size>6){size-=.2;lines=Wrap(values[i],42,size);}
            if(lines.Count>2)throw new InvalidOperationException("Identité trop longue pour la carte : "+a.Matricule);
            p.Add(new TextPart{Text=labels[i]+" :",X=4,Y=y,Size=6.2,Bold=true,Ink=ink});
            for(int j=0;j<lines.Count;j++)p.Add(new TextPart{Text=lines[j],X=20,Y=y+j*2.5,Size=size,Bold=true,Ink=ink});
        }
        return p;
    }
    public static RectangleF CardBounds(int slot){if(slot<0||slot>7)throw new ArgumentOutOfRangeException("slot");return new RectangleF((float)(Margin+(slot%2)*(CardWidth+ColumnGap)),(float)(Margin+(slot/2)*(CardHeight+RowGap)),(float)CardWidth,(float)CardHeight);}
    private static ITMExamLearner At(ITMExamBatch b,int index){return b.Learners[index/b.Copies];}
    public static void DrawPage(Graphics g,ITMExamBatch b,int page){
        int count=CardCount(b);if(page<0||page>=(count+7)/8)throw new ArgumentOutOfRangeException("page");
        using(Image logo=Image.FromFile(b.LogoPath)){
            for(int slot=0;slot<8;slot++){
                int n=page*8+slot;if(n>=count)break;var a=At(b,n);var r=CardBounds(slot);var state=g.Save();g.TranslateTransform(r.X,r.Y);
                using(var brush=new SolidBrush(Color.White))g.FillRectangle(brush,0,0,(float)CardWidth,(float)CardHeight);
                using(var brush=new SolidBrush(BandColor(a.Promotion)))g.FillRectangle(brush,0,56,(float)CardWidth,7);
                Decorations(g);
                DrawQr(g,Qr(b,a),64,32,24);
                g.DrawImage(logo,new RectangleF(3,4,10,10));
                foreach(var t in Parts(b,a))using(var f=Roboto(t.Size,t.Bold))using(var brush=new SolidBrush(t.Ink)){
                    // Baselines in millimetres, matching the vector PDF.
                    using(var format=new StringFormat(StringFormat.GenericTypographic)){format.FormatFlags|=StringFormatFlags.NoWrap;g.DrawString(t.Text,f,brush,new PointF((float)t.X,(float)(t.Y-t.Size*25.4/72*.85)),format);}
                }
                using(var pen=new Pen(Color.FromArgb(55,70,90),.2f))g.DrawRectangle(pen,0,0,(float)CardWidth,(float)CardHeight);
                g.Restore(state);
                if(b.CutMarks)using(var pen=new Pen(Color.FromArgb(160,165,175),.12f)){
                    g.DrawLine(pen,r.X-2,r.Y,r.X-1,r.Y);g.DrawLine(pen,r.Right+1,r.Y,r.Right+2,r.Y);g.DrawLine(pen,r.X-2,r.Bottom,r.X-1,r.Bottom);g.DrawLine(pen,r.Right+1,r.Bottom,r.Right+2,r.Bottom);
                }
            }
        }
    }
    public static PrintDocument CreatePrintDocument(ITMExamBatch b){
        Validate(b);var doc=new PrintDocument();doc.DocumentName="Cartes ITM - "+b.Session;
        doc.DefaultPageSettings.PaperSize=new PaperSize("A4",827,1169);doc.DefaultPageSettings.Landscape=false;doc.DefaultPageSettings.Margins=new Margins(39,39,39,39);doc.OriginAtMargins=false;
        int page=0;doc.BeginPrint+=delegate{page=0;};doc.PrintPage+=delegate(object sender,PrintPageEventArgs e){
            if(Math.Abs(e.PageBounds.Width-827)>12||Math.Abs(e.PageBounds.Height-1169)>12)throw new InvalidOperationException("Choisissez du papier A4 portrait et une impression à taille réelle.");
            e.Graphics.PageUnit=GraphicsUnit.Millimeter;e.Graphics.TranslateTransform(-e.PageSettings.HardMarginX*.254f,-e.PageSettings.HardMarginY*.254f);DrawPage(e.Graphics,b,page++);e.HasMorePages=page<PageCount(b);
        };return doc;
    }
    private static string N(double n){return n.ToString("0.###",Inv);}
    private static string PdfString(string s){var v=new StringBuilder();foreach(byte c in Encoding.GetEncoding(1252).GetBytes(Clean(s)))v.Append("\\"+Convert.ToString(c,8).PadLeft(3,'0'));return "("+v+")";}
    private static byte[] Stream(byte[] bytes,string extra){using(var m=new MemoryStream()){byte[] head=Encoding.ASCII.GetBytes("<< /Length "+bytes.Length+" "+extra+" >>\nstream\n");m.Write(head,0,head.Length);m.Write(bytes,0,bytes.Length);byte[] tail=Encoding.ASCII.GetBytes("\nendstream");m.Write(tail,0,tail.Length);return m.ToArray();}}
    private static byte[] ContentStream(byte[] bytes,string extra=""){
        using(var m=new MemoryStream()){
            m.WriteByte(0x78);m.WriteByte(0x9c);
            using(var deflate=new System.IO.Compression.DeflateStream(m,System.IO.Compression.CompressionMode.Compress,true)){deflate.Write(bytes,0,bytes.Length);}
            uint a=1,b=0;foreach(byte value in bytes){a=(a+value)%65521;b=(b+a)%65521;}uint sum=(b<<16)|a;
            for(int i=3;i>=0;i--)m.WriteByte((byte)(sum>>(8*i)));
            return Stream(m.ToArray(),"/Filter /FlateDecode "+extra);
        }
    }
    private static string ColorPdf(Color c){return N(c.R/255.0)+" "+N(c.G/255.0)+" "+N(c.B/255.0);}
    private static string RectanglePdf(double x,double y,double w,double h){double q=72/25.4;return N(x*q)+" "+N((297-y-h)*q)+" "+N(w*q)+" "+N(h*q)+" re";}
    private static void Commit(string temp,string path){if(File.Exists(path))File.Replace(temp,path,null,true);else File.Move(temp,path);}
    private static void AddPdfFont(List<byte[]> objects,int index,string style,int[] widths){
        byte[] ttf=File.ReadAllBytes(Path.Combine(fontFolder,"Roboto-"+style+".ttf"));int streamId=objects.Count+1;objects.Add(Stream(ttf,"/Length1 "+ttf.Length));int descriptor=objects.Count+1;
        objects.Add(Encoding.ASCII.GetBytes("<< /Type /FontDescriptor /FontName /Roboto-"+style+" /Flags 32 /FontBBox [-1100 -400 2100 1400] /ItalicAngle 0 /Ascent 928 /Descent -245 /CapHeight 710 /StemV "+(style=="Bold"?120:80)+" /FontFile2 "+streamId+" 0 R >>"));
        var values=new StringBuilder();for(int i=32;i<256;i++)values.Append(widths[i]+" ");
        objects[index]=Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /TrueType /BaseFont /Roboto-"+style+" /Encoding /WinAnsiEncoding /FirstChar 32 /LastChar 255 /Widths ["+values+"] /FontDescriptor "+descriptor+" 0 R >>");
    }
    public static void ExportPdf(ITMExamBatch b,string path){
        Validate(b);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            var objects=new List<byte[]>();objects.Add(null);objects.Add(null);
            objects.Add(null);objects.Add(null);objects.Add(null);
            AddPdfFont(objects,2,"Regular",regularWidths);AddPdfFont(objects,3,"Bold",boldWidths);
            // Preserve the seal alpha channel instead of flattening it onto a white JPEG.
            byte[] rgb,alpha;int iw,ih;
            using(Image source=Image.FromFile(b.LogoPath))using(var bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb)){
                using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.DrawImage(source,0,0,source.Width,source.Height);}
                iw=bitmap.Width;ih=bitmap.Height;rgb=new byte[iw*ih*3];alpha=new byte[iw*ih];
                var data=bitmap.LockBits(new Rectangle(0,0,iw,ih),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
                try{var row=new byte[iw*4];for(int y=0;y<ih;y++){
                    System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);
                    for(int x=0;x<iw;x++){int pixel=y*iw+x;rgb[pixel*3]=row[x*4+2];rgb[pixel*3+1]=row[x*4+1];rgb[pixel*3+2]=row[x*4];alpha[pixel]=row[x*4+3];}
                }}finally{bitmap.UnlockBits(data);}
            }
            int maskId=objects.Count+1;
            objects.Add(ContentStream(alpha,"/Type /XObject /Subtype /Image /Width "+iw+" /Height "+ih+" /ColorSpace /DeviceGray /BitsPerComponent 8"));
            objects[4]=ContentStream(rgb,"/Type /XObject /Subtype /Image /Width "+iw+" /Height "+ih+" /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask "+maskId+" 0 R");
            var kids=new List<int>();double q=72/25.4;int count=CardCount(b);
            for(int page=0;page<(count+7)/8;page++){
                var content=new StringBuilder();
                for(int slot=0;slot<8&&page*8+slot<count;slot++){
                    var a=At(b,page*8+slot);var r=CardBounds(slot);double x=r.X,y=r.Y;
                    content.Append("1 1 1 rg "+RectanglePdf(x,y,CardWidth,CardHeight)+" f\n");
                    content.Append(ColorPdf(BandColor(a.Promotion))+" rg "+RectanglePdf(x,y+56,CardWidth,7)+" f\n");
                    content.Append("0.961 0.973 0.984 rg "+RectanglePdf(x,y,92,19)+" f\n");
                    for(int line=0;line<6;line++)content.Append("0.935 0.957 0.975 RG 0.3 w "+N((x+63+line*3)*q)+" "+N((297-y-30)*q)+" m "+N((x+88)*q)+" "+N((297-y-52+line*3)*q)+" l S\n");
                    content.Append("0.263 0.431 0.584 RG 0.7 w "+N((x+4)*q)+" "+N((297-y-19.1)*q)+" m "+N((x+88)*q)+" "+N((297-y-19.1)*q)+" l S\n");
                    content.Append("0.137 0.231 0.325 RG 0.5 w "+N((x+4)*q)+" "+N((297-y-32.0)*q)+" m "+N((x+60)*q)+" "+N((297-y-32.0)*q)+" l S\n");
                    content.Append("1 1 1 rg "+RectanglePdf(x+64,y+32,24,24)+" f\n"+QrPdf(Qr(b,a),x+64,y+32,24));
                    content.Append("q "+N(10*q)+" 0 0 "+N(10*q)+" "+N((x+3)*q)+" "+N((297-y-14)*q)+" cm /Logo Do Q\n");
                    foreach(var t in Parts(b,a))content.Append("BT "+ColorPdf(t.Ink)+" rg /"+(t.Bold?"FB":"FR")+" "+N(t.Size)+" Tf 1 0 0 1 "+N((x+t.X)*q)+" "+N((297-y-t.Y)*q)+" Tm "+PdfString(t.Text)+" Tj ET\n");
                    content.Append("0.22 0.27 0.35 RG 0.5 w "+RectanglePdf(x,y,CardWidth,CardHeight)+" S\n");
                    if(b.CutMarks)foreach(double cy in new[]{y,y+CardHeight})content.Append("0.6 0.62 0.68 RG 0.3 w "+N((x-2)*q)+" "+N((297-cy)*q)+" m "+N((x-1)*q)+" "+N((297-cy)*q)+" l S "+N((x+CardWidth+1)*q)+" "+N((297-cy)*q)+" m "+N((x+CardWidth+2)*q)+" "+N((297-cy)*q)+" l S\n");
                }
                int streamId=objects.Count+1;objects.Add(ContentStream(Encoding.ASCII.GetBytes(content.ToString())));int pageId=objects.Count+1;kids.Add(pageId);
                objects.Add(Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.276 841.89] /Resources << /Font << /FR 3 0 R /FB 4 0 R >> /XObject << /Logo 5 0 R >> >> /Contents "+streamId+" 0 R >>"));
            }
            objects[0]=Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");objects[1]=Encoding.ASCII.GetBytes("<< /Type /Pages /Count "+kids.Count+" /Kids ["+String.Join(" ",kids.ConvertAll(k=>k+" 0 R").ToArray())+"] >>");
            using(var f=new FileStream(temp,FileMode.CreateNew)){Action<string> write=s=>{byte[] bytes=Encoding.ASCII.GetBytes(s);f.Write(bytes,0,bytes.Length);};write("%PDF-1.4\n%ITM\n");var offsets=new List<long>();foreach(byte[] obj in objects){offsets.Add(f.Position);write(offsets.Count+" 0 obj\n");f.Write(obj,0,obj.Length);write("\nendobj\n");}long start=f.Position;write("xref\n0 "+(objects.Count+1)+"\n0000000000 65535 f \n");foreach(long offset in offsets)write(offset.ToString("0000000000",Inv)+" 00000 n \n");write("trailer\n<< /Size "+(objects.Count+1)+" /Root 1 0 R >>\nstartxref\n"+start+"\n%%EOF\n");}
            Commit(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    private static string X(string s){return SecurityElement.Escape(Clean(s))??"";}
    private static int Tw(double mm){return (int)Math.Round(mm*1440/25.4);}
    private static string Hex(Color c){return c.R.ToString("X2")+c.G.ToString("X2")+c.B.ToString("X2");}
    private static string Paragraph(string text,double size,bool bold,string align,string color,int line){
        return "<w:p><w:pPr><w:spacing w:before='0' w:after='0' w:line='"+line+"' w:lineRule='exact'/><w:jc w:val='"+align+"'/></w:pPr><w:r><w:rPr><w:rFonts w:ascii='Roboto' w:hAnsi='Roboto'/><w:sz w:val='"+(int)Math.Round(size*2)+"'/><w:color w:val='"+color+"'/>"+(bold?"<w:b/>":"")+"</w:rPr><w:t xml:space='preserve'>"+X(Clean(text))+"</w:t></w:r></w:p>";
    }
    private static string Cell(string content,int width,bool border){return "<w:tc><w:tcPr><w:tcW w:w='"+width+"' w:type='dxa'/><w:vAlign w:val='top'/><w:tcMar><w:top w:w='"+0+"' w:type='dxa'/><w:left w:w='"+0+"' w:type='dxa'/><w:bottom w:w='0' w:type='dxa'/><w:right w:w='"+0+"' w:type='dxa'/></w:tcMar>"+(border?"<w:tcBorders><w:top w:val='single' w:sz='5' w:color='37465A'/><w:left w:val='single' w:sz='5' w:color='37465A'/><w:bottom w:val='single' w:sz='5' w:color='37465A'/><w:right w:val='single' w:sz='5' w:color='37465A'/></w:tcBorders>":"")+"</w:tcPr>"+content+"</w:tc>";}
    private static string ImageParagraph(int id){return "<w:p><w:pPr><w:spacing w:before='0' w:after='0'/></w:pPr><w:r><w:drawing><wp:inline><wp:extent cx='360000' cy='360000'/><wp:docPr id='"+id+"' name='Logo ITM' descr='Logo de l’école'/><a:graphic><a:graphicData uri='http://schemas.openxmlformats.org/drawingml/2006/picture'><pic:pic><pic:nvPicPr><pic:cNvPr id='0' name='Logo ITM'/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed='rIdLogo'/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x='0' y='0'/><a:ext cx='360000' cy='360000'/></a:xfrm><a:prstGeom prst='rect'><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";}
    private static string QrImageParagraph(int id){return ImageParagraph(id+10000).Replace("360000","864000").Replace("rIdLogo","rIdQr"+id).Replace("Logo ITM","QR de la carte").Replace("Logo de l’école","Identité, classe et session");}
    private static byte[] WordFont(string style,string key){byte[] bytes=File.ReadAllBytes(Path.Combine(fontFolder,"Roboto-"+style+".ttf"));string hex=key.Replace("-","");var xor=new byte[16];for(int i=0;i<16;i++)xor[15-i]=Convert.ToByte(hex.Substring(i*2,2),16);for(int i=0;i<32;i++)bytes[i]^=xor[i%16];return bytes;}
    private static string WordRow(string content,double height,string fill){return "<w:tr><w:trPr><w:cantSplit/><w:trHeight w:val='"+Tw(height)+"' w:hRule='exact'/></w:trPr><w:tc><w:tcPr><w:tcW w:w='"+Tw(92)+"' w:type='dxa'/><w:shd w:val='clear' w:color='auto' w:fill='"+fill+"'/><w:tcMar><w:top w:w='"+Tw(.5)+"' w:type='dxa'/><w:bottom w:w='0' w:type='dxa'/><w:left w:w='"+Tw(3)+"' w:type='dxa'/><w:right w:w='"+Tw(3)+"' w:type='dxa'/></w:tcMar></w:tcPr>"+content+"</w:tc></w:tr>";}
    private static string CardXml(ITMExamBatch b,ITMExamLearner a,int id){
        List<TextPart> parts=Parts(b,a);var header=new StringBuilder();for(int i=0;i<8;i++)header.Append(Paragraph(parts[i].Text,parts[i].Size,parts[i].Bold,"center","1C2B40",Tw(1.85)));
        string head="<w:tbl><w:tblPr><w:tblW w:w='"+Tw(86)+"' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='"+Tw(12)+"'/><w:gridCol w:w='"+Tw(74)+"'/></w:tblGrid><w:tr>"+Cell(ImageParagraph(id),Tw(12),false)+Cell(header.ToString(),Tw(74),false)+"</w:tr></w:tbl>"+Paragraph("",1,false,"left","1C2B40",20);
        string title=Paragraph(parts[8].Text,parts[8].Size,true,"center","00467D",Tw(4.8)).Replace("<w:pPr>","<w:pPr><w:pBdr><w:top w:val='single' w:sz='5' w:color='436E95'/></w:pBdr>")+Paragraph("CARTE D'ACCÈS",10,true,"center","1C2B40",Tw(4.8));
        string[] labels={"NOM", "POST-NOM", "PRÉNOM", "CLASSE"},values={a.Nom,a.PostNom,a.Prenom,PromotionCode(a.Promotion)};
        var identity=new StringBuilder();for(int i=0;i<4;i++){
            double size=8;var lines=Wrap(values[i],42,size);while(lines.Count>2&&size>6){size-=.2;lines=Wrap(values[i],42,size);}
            identity.Append("<w:tr><w:trPr><w:trHeight w:val='"+Tw(5.6)+"' w:hRule='exact'/></w:trPr>"+Cell(Paragraph(labels[i]+" :",6.2,true,"left","1C2B40",Tw(2.5)),Tw(17),false));
            var value=new StringBuilder();foreach(string line in lines)value.Append(Paragraph(line,size,true,"left","1C2B40",Tw(2.5)));identity.Append(Cell(value.ToString(),Tw(43),false)+"</w:tr>");
        }
        string identities="<w:tbl><w:tblPr><w:tblW w:w='"+Tw(60)+"' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='"+Tw(17)+"'/><w:gridCol w:w='"+Tw(43)+"'/></w:tblGrid>"+identity+"</w:tbl>"+Paragraph("",1,false,"left","1C2B40",20);
        string body="<w:tbl><w:tblPr><w:tblW w:w='"+Tw(86)+"' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='"+Tw(60)+"'/><w:gridCol w:w='"+Tw(26)+"'/></w:tblGrid><w:tr>"+Cell(identities,Tw(60),false)+Cell(QrImageParagraph(id),Tw(26),false)+"</w:tr></w:tbl>"+Paragraph("",1,false,"left","1C2B40",20);
        return "<w:tbl><w:tblPr><w:tblW w:w='"+Tw(92)+"' w:type='dxa'/><w:tblLayout w:type='fixed'/><w:tblCellMar><w:top w:w='0' w:type='dxa'/><w:left w:w='0' w:type='dxa'/><w:bottom w:w='0' w:type='dxa'/><w:right w:w='0' w:type='dxa'/></w:tblCellMar></w:tblPr><w:tblGrid><w:gridCol w:w='"+Tw(92)+"'/></w:tblGrid>"+WordRow(head,18.5,"F5F8FB")+WordRow(title,12.5,"FFFFFF")+WordRow(body,25,"FFFFFF")+WordRow(Paragraph("",1,false,"center","FFFFFF",Tw(5)),7,Hex(BandColor(a.Promotion)))+"</w:tbl>"+Paragraph("",1,false,"left","1C2B40",20);
    }
    private static string OrderWordProperties(string text){
        var xml=new System.Xml.XmlDocument();xml.LoadXml(text);
        var ns=new System.Xml.XmlNamespaceManager(xml.NameTable);ns.AddNamespace("w","http://schemas.openxmlformats.org/wordprocessingml/2006/main");
        string[][] rules={new[]{"tcMar","top,left,bottom,right"},new[]{"tcPr","cnfStyle,tcW,gridSpan,hMerge,vMerge,tcBorders,shd,noWrap,tcMar,textDirection,tcFitText,vAlign,hideMark,headers,cellIns,cellDel,cellMerge,tcPrChange"},new[]{"rPr","rStyle,rFonts,b,bCs,i,iCs,caps,smallCaps,strike,dstrike,outline,shadow,emboss,imprint,noProof,snapToGrid,vanish,webHidden,color,spacing,w,kern,position,sz,szCs,highlight,u,effect,bdr,shd,fitText,vertAlign,rtl,cs,em,lang,eastAsianLayout,specVanish,oMath,rPrChange"}};
        foreach(var rule in rules){string[] order=rule[1].Split(',');foreach(System.Xml.XmlElement node in xml.SelectNodes("//w:"+rule[0],ns)){
            var children=new List<System.Xml.XmlElement>();foreach(System.Xml.XmlNode child in node.ChildNodes){var element=child as System.Xml.XmlElement;if(element!=null)children.Add(element);}
            children.Sort((a,b)=>Array.IndexOf(order,a.LocalName).CompareTo(Array.IndexOf(order,b.LocalName)));foreach(var child in children)node.AppendChild(child);
        }}
        return xml.OuterXml;
    }
    private static void ZipText(CardZip z,string name,string text){if(name=="word/document.xml")text=OrderWordProperties(text);z.Add(name,new UTF8Encoding(false).GetBytes(text));}
    public static void ExportWord(ITMExamBatch b,string path){
        Validate(b);string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{
            using(var f=new FileStream(temp,FileMode.CreateNew))using(var z=new CardZip(f)){
                ZipText(z,"[Content_Types].xml","<Types xmlns='http://schemas.openxmlformats.org/package/2006/content-types'><Default Extension='rels' ContentType='application/vnd.openxmlformats-package.relationships+xml'/><Default Extension='xml' ContentType='application/xml'/><Default Extension='png' ContentType='image/png'/><Default Extension='odttf' ContentType='application/vnd.openxmlformats-officedocument.obfuscatedFont'/><Override PartName='/word/fontTable.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml'/><Override PartName='/word/document.xml' ContentType='application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml'/></Types>");
                ZipText(z,"_rels/.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rId1' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument' Target='word/document.xml'/></Relationships>");
                var relations=new StringBuilder("<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rIdLogo' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/image' Target='media/logo.png'/><Relationship Id='rIdFonts' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable' Target='fontTable.xml'/>");
                int totalCards=CardCount(b);for(int n=0;n<totalCards;n++){int id=n+1;z.Add("word/media/qr"+id+".png",QrPng(b,At(b,n)));relations.Append("<Relationship Id='rIdQr"+id+"' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/image' Target='media/qr"+id+".png'/>");}relations.Append("</Relationships>");ZipText(z,"word/_rels/document.xml.rels",relations.ToString());
                const string regularKey="D8E0D09A-8B8C-4B8B-B311-702D11740001",boldKey="D8E0D09A-8B8C-4B8B-B311-702D11740002";
                z.Add("word/fonts/Roboto-Regular.odttf",WordFont("Regular",regularKey));z.Add("word/fonts/Roboto-Bold.odttf",WordFont("Bold",boldKey));
                ZipText(z,"word/fontTable.xml","<w:fonts xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><w:font w:name='Roboto'><w:family w:val='swiss'/><w:embedRegular r:id='rIdRegular' w:fontKey='{"+regularKey+"}'/><w:embedBold r:id='rIdBold' w:fontKey='{"+boldKey+"}'/></w:font></w:fonts>".Replace("{ ","{").Replace(" }","}"));
                ZipText(z,"word/_rels/fontTable.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='rIdRegular' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/font' Target='fonts/Roboto-Regular.odttf'/><Relationship Id='rIdBold' Type='http://schemas.openxmlformats.org/officeDocument/2006/relationships/font' Target='fonts/Roboto-Bold.odttf'/></Relationships>");
                using(var buffer=new MemoryStream())using(Image logo=Image.FromFile(b.LogoPath)){logo.Save(buffer,ImageFormat.Png);z.Add("word/media/logo.png",buffer.ToArray());}
                var xml=new StringBuilder("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships' xmlns:wp='http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing' xmlns:a='http://schemas.openxmlformats.org/drawingml/2006/main' xmlns:pic='http://schemas.openxmlformats.org/drawingml/2006/picture'><w:body>");
                int count=CardCount(b);
                for(int page=0;page<(count+7)/8;page++){
                    if(page>0)xml.Append("<w:p><w:pPr><w:spacing w:before='0' w:after='0' w:line='20' w:lineRule='exact'/></w:pPr><w:r><w:br w:type='page'/></w:r></w:p>");
                    xml.Append("<w:tbl><w:tblPr><w:tblW w:w='"+Tw(190)+"' w:type='dxa'/><w:tblLayout w:type='fixed'/></w:tblPr><w:tblGrid><w:gridCol w:w='"+Tw(92)+"'/><w:gridCol w:w='"+Tw(6)+"'/><w:gridCol w:w='"+Tw(92)+"'/></w:tblGrid>");
                    for(int row=0;row<4;row++){
                        xml.Append("<w:tr><w:trPr><w:cantSplit/><w:trHeight w:val='"+Tw(63)+"' w:hRule='exact'/></w:trPr>");
                        for(int col=0;col<3;col++){
                            int n=page*8+row*2+(col==2?1:0);bool card=col!=1&&n<count;
                            xml.Append(Cell(card?CardXml(b,At(b,n),n+1):Paragraph("",1,false,"left","000000",20),Tw(col==1?6:92),card));
                        }
                        xml.Append("</w:tr>");if(row<3)xml.Append("<w:tr><w:trPr><w:trHeight w:val='"+Tw(7)+"' w:hRule='exact'/></w:trPr>"+Cell(Paragraph("",1,false,"left","000000",20),Tw(92),false)+Cell(Paragraph("",1,false,"left","000000",20),Tw(6),false)+Cell(Paragraph("",1,false,"left","000000",20),Tw(92),false)+"</w:tr>");
                    }
                    xml.Append("</w:tbl>");
                }
                xml.Append("<w:p><w:pPr><w:spacing w:before='0' w:after='0' w:line='20' w:lineRule='exact'/></w:pPr></w:p><w:sectPr><w:pgSz w:w='"+Tw(210)+"' w:h='"+Tw(297)+"'/><w:pgMar w:top='"+Tw(10)+"' w:bottom='"+Tw(10)+"' w:left='"+Tw(10)+"' w:right='"+Tw(10)+"' w:header='0' w:footer='0'/></w:sectPr></w:body></w:document>");ZipText(z,"word/document.xml",xml.ToString());
            }
            Commit(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    // Read the already-validated Excel report. Selection, sorting and totals stay with the existing engine.
    private sealed class ReportSheet {
        public string Name,Title,Meta; public string[] Headers; public readonly List<string[]> Rows=new List<string[]>();
    }
    private static byte[] PackageEntry(string file,string name){
        using(var reader=new BinaryReader(File.OpenRead(file))){
            long length=reader.BaseStream.Length,start=Math.Max(0,length-65557),end=-1;
            reader.BaseStream.Position=start;byte[] tail=reader.ReadBytes((int)(length-start));
            for(int i=tail.Length-22;i>=0;i--)if(BitConverter.ToUInt32(tail,i)==0x06054b50u){end=start+i;break;}
            if(end<0)throw new InvalidDataException("Rapport Excel incomplet.");
            reader.BaseStream.Position=end+10;int count=reader.ReadUInt16();reader.ReadUInt32();uint central=reader.ReadUInt32();reader.BaseStream.Position=central;
            for(int i=0;i<count;i++){
                if(reader.ReadUInt32()!=0x02014b50u)throw new InvalidDataException("Rapport Excel illisible.");
                reader.ReadUInt16();reader.ReadUInt16();ushort flags=reader.ReadUInt16(),method=reader.ReadUInt16();reader.ReadUInt16();reader.ReadUInt16();reader.ReadUInt32();uint packed=reader.ReadUInt32(),unpacked=reader.ReadUInt32();
                int n=reader.ReadUInt16(),extra=reader.ReadUInt16(),comment=reader.ReadUInt16();reader.ReadUInt16();reader.ReadUInt16();reader.ReadUInt32();uint offset=reader.ReadUInt32();string entry=Encoding.UTF8.GetString(reader.ReadBytes(n));reader.BaseStream.Position+=extra+comment;
                if(entry!=name)continue;
                if((flags&1)!=0||packed>134217728||unpacked>134217728)throw new InvalidDataException("Rapport chiffré ou trop volumineux.");
                reader.BaseStream.Position=offset;
                if(reader.ReadUInt32()!=0x04034b50u)throw new InvalidDataException("Entrée Excel incomplète.");
                reader.BaseStream.Position=offset+26;int localName=reader.ReadUInt16(),localExtra=reader.ReadUInt16();reader.BaseStream.Position+=localName+localExtra;byte[] bytes=reader.ReadBytes((int)packed);
                if(bytes.Length!=packed)throw new InvalidDataException("Entrée Excel tronquée.");
                if(method==0)return bytes;if(method!=8)throw new InvalidDataException("Compression Excel non reconnue.");
                using(var input=new MemoryStream(bytes))using(var inflate=new System.IO.Compression.DeflateStream(input,System.IO.Compression.CompressionMode.Decompress))using(var output=new MemoryStream()){
                    byte[] buffer=new byte[8192];int read;while((read=inflate.Read(buffer,0,buffer.Length))>0){if(output.Length+read>unpacked)throw new InvalidDataException("Taille Excel incohérente.");output.Write(buffer,0,read);}if(output.Length!=unpacked)throw new InvalidDataException("Rapport Excel tronqué.");return output.ToArray();
                }
            }
        }return null;
    }
    private static System.Xml.XmlDocument ReportXml(byte[] bytes){
        if(bytes==null)throw new InvalidDataException("Partie du rapport Excel absente.");
        var xml=new System.Xml.XmlDocument();xml.XmlResolver=null;
        var settings=new System.Xml.XmlReaderSettings();settings.DtdProcessing=System.Xml.DtdProcessing.Prohibit;settings.XmlResolver=null;
        using(var input=new MemoryStream(bytes))using(var reader=System.Xml.XmlReader.Create(input,settings))xml.Load(reader);return xml;
    }
    private static List<ReportSheet> ReadReport(string file,int mode){
        const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared=new List<string>();byte[] strings=PackageEntry(file,"xl/sharedStrings.xml");
        if(strings!=null){var xml=ReportXml(strings);foreach(System.Xml.XmlNode item in xml.GetElementsByTagName("si",ns))shared.Add(item.InnerText);}
        var relationships=new Dictionary<string,string>();var rels=ReportXml(PackageEntry(file,"xl/_rels/workbook.xml.rels"));
        foreach(System.Xml.XmlElement r in rels.DocumentElement.ChildNodes){if(r.GetAttribute("TargetMode")=="External")continue;string target=r.GetAttribute("Target");relationships[r.GetAttribute("Id")]=target.StartsWith("/")?target.TrimStart('/'):"xl/"+target;}
        var result=new List<ReportSheet>();var book=ReportXml(PackageEntry(file,"xl/workbook.xml"));
        foreach(System.Xml.XmlElement sheet in book.GetElementsByTagName("sheet",ns)){
            string id=sheet.GetAttribute("id","http://schemas.openxmlformats.org/officeDocument/2006/relationships");string target;if(!relationships.TryGetValue(id,out target))throw new InvalidDataException("Feuille du rapport absente.");
            var xml=ReportXml(PackageEntry(file,target));var rows=new SortedDictionary<int,string[]>();
            foreach(System.Xml.XmlElement row in xml.GetElementsByTagName("row",ns)){
                int at=Int32.Parse(row.GetAttribute("r"),Inv);var values=new string[10];
                foreach(System.Xml.XmlElement cell in row.ChildNodes){
                    string address=cell.GetAttribute("r");int column=0;foreach(char ch in address){if(ch<'A'||ch>'Z')break;column=column*26+ch-'A'+1;}column--;if(column<0||column>=10)continue;
                    string type=cell.GetAttribute("t"),value="";foreach(System.Xml.XmlElement child in cell.ChildNodes){if(child.LocalName=="v"||child.LocalName=="is")value=child.InnerText;}
                    int si;if(type=="s"&&Int32.TryParse(value,out si)){if(si<0||si>=shared.Count)throw new InvalidDataException("Texte Excel incohérent.");value=shared[si];}
                    values[column]=Clean(value);
                }rows[at]=values;
            }
            bool diagnostic=sheet.GetAttribute("name")=="Diagnostic";int header=diagnostic?4:5;string[] labels;
            if(!rows.TryGetValue(header,out labels))continue;
            int count=labels.Length;while(count>0&&String.IsNullOrWhiteSpace(labels[count-1]))count--;if(count==0)continue;
            var report=new ReportSheet();report.Name=sheet.GetAttribute("name");report.Headers=new string[count];Array.Copy(labels,report.Headers,count);
            string[] title,meta;report.Title=rows.TryGetValue(2,out title)?title[0]:"Rapport";report.Meta=rows.TryGetValue(3,out meta)?meta[0]:"";
            if(diagnostic){report.Title="DONNÉES À VÉRIFIER";report.Meta=rows.TryGetValue(2,out title)?title[0]:"";}
            if(!diagnostic&&mode==3)report.Title="LISTE D'ENREGISTREMENT";
            else if(!diagnostic&&mode==1)report.Title="LISTE DES PAIEMENTS - "+report.Title.Replace("APPRENANTS EN ORDRE POUR ","MOIS SOLDÉ : ");
            else if(!diagnostic&&mode==2)report.Title="LISTE DES PAIEMENTS - "+report.Title.Replace("APPRENANTS AYANT VERSE POUR ","VERSEMENTS : ");
            foreach(var row in rows){if(row.Key<=header)continue;bool any=false;var data=new string[count];for(int i=0;i<count;i++){data[i]=row.Value[i]??"";if(data[i].Length>0)any=true;}if(any)report.Rows.Add(data);}
            result.Add(report);
        }if(result.Count==0)throw new InvalidOperationException("Le rapport ne contient aucune liste à exporter.");return result;
    }
    private static string ReportLabel(string label){
        switch((label??"").Trim()){
            case "Prenom":return "Prénom";case "Annee scolaire":return "Année scolaire";case "Periode":return "Période";case "Du mensuel FC":return "Dû mensuel (FC)";case "Verse FC":return "Versé (FC)";case "Reste FC":return "Reste (FC)";case "Dette FC":return "Dette (FC)";case "Numero":return "Numéro";default:return label;
        }
    }
    private static bool IsMoney(string label){return (label??"").IndexOf("FC",StringComparison.OrdinalIgnoreCase)>=0;}
    private static double ReportWeight(string label){
        if(label=="Nom"||label=="Post-nom")return 1.6;if(label=="Prenom")return 1.35;if(label=="Observation")return 3.6;if(label=="Promotion"||label=="Statut")return 1.0;return 1.2;
    }
    private static string ReportText(string text,double x,double y,double size,bool bold,Color ink){return "BT "+ColorPdf(ink)+" rg /"+(bold?"FB":"FR")+" "+N(size)+" Tf 1 0 0 1 "+N(x*72/25.4)+" "+N((210-y)*72/25.4)+" Tm "+PdfString(text)+" Tj ET\n";}
    private static string ReportRect(double x,double y,double width,double height){double q=72/25.4;return N(x*q)+" "+N((210-y-height)*q)+" "+N(width*q)+" "+N(height*q)+" re";}
    private static void ReportRule(StringBuilder content,double y){content.Append("0.87 0.90 0.94 RG 0.4 w "+N(12*72/25.4)+" "+N((210-y)*72/25.4)+" m "+N(285*72/25.4)+" "+N((210-y)*72/25.4)+" l S\n");}
    private static int ReportLogo(List<byte[]> objects,string path){
        using(Image image=Image.FromFile(path))using(var bitmap=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb)){
            using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.DrawImage(image,0,0,image.Width,image.Height);}
            byte[] rgb=new byte[bitmap.Width*bitmap.Height*3],alpha=new byte[bitmap.Width*bitmap.Height];
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){Color pixel=bitmap.GetPixel(x,y);int i=y*bitmap.Width+x;rgb[i*3]=pixel.R;rgb[i*3+1]=pixel.G;rgb[i*3+2]=pixel.B;alpha[i]=pixel.A;}
            int mask=objects.Count+1;objects.Add(ContentStream(alpha,"/Type /XObject /Subtype /Image /Width "+bitmap.Width+" /Height "+bitmap.Height+" /ColorSpace /DeviceGray /BitsPerComponent 8"));
            int logo=objects.Count+1;objects.Add(ContentStream(rgb,"/Type /XObject /Subtype /Image /Width "+bitmap.Width+" /Height "+bitmap.Height+" /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask "+mask+" 0 R"));return logo;
        }
    }
    private static double ReportWidth(string text,double size,bool bold){double width=0;int[] metrics=bold?boldWidths:regularWidths;foreach(byte ch in Encoding.GetEncoding(1252).GetBytes(Clean(text)))width+=metrics[ch];return width/1000*size*25.4/72;}
    public static void ExportWorkbookPdf(string workbook,string path,string resources,string institution,int mode,string logoPath){
        var sheets=ReadReport(workbook,mode);ConfigureFonts(new ITMExamBatch{ResourceFolder=resources,LogoPath=Path.Combine(resources,"Logo_ITM_CD_Transparent.png")});
        Color ink=Color.FromArgb(31,41,55),muted=Color.FromArgb(91,105,122),blue=Color.FromArgb(38,91,155);
        var pages=new List<StringBuilder>();var pageNames=new List<string>();
        foreach(var sheet in sheets){
            int count=sheet.Headers.Length;var widths=new double[count];double totalWeight=0;for(int c=0;c<count;c++)totalWeight+=ReportWeight(sheet.Headers[c]);for(int c=0;c<count;c++)widths[c]=273*ReportWeight(sheet.Headers[c])/totalWeight;
            int index=0;bool first=true;
            do{
                var content=new StringBuilder();pages.Add(content);pageNames.Add(sheet.Name);
                content.Append("0.15 0.36 0.61 rg "+ReportRect(12,12,273,1.2)+" f\n");
                double headY=20;
                content.Append("q "+N(12*72/25.4)+" 0 0 "+N(12*72/25.4)+" "+N(273*72/25.4)+" "+N((210-28)*72/25.4)+" cm /Logo Do Q\n");
                foreach(var line in Wrap(institution,254,12)){content.Append(ReportText(line,12,headY,12,true,blue));headY+=5;}
                headY+=3;foreach(var line in Wrap(sheet.Title??"Rapport",273,11)){content.Append(ReportText(line,12,headY,11,true,ink));headY+=4.5;}
                headY+=2;string meta=String.IsNullOrWhiteSpace(sheet.Meta)?"Promotion : "+sheet.Name:sheet.Meta;
                foreach(var line in Wrap(meta,273,8.5)){content.Append(ReportText(line,12,headY,8.5,false,muted));headY+=3.6;}
                double y=headY+3,headingHeight=12,x=12;
                for(int c=0;c<count;c++)headingHeight=Math.Max(headingHeight,Wrap(ReportLabel(sheet.Headers[c]),widths[c]-4,8.8).Count*3.3+4);
                content.Append("0.15 0.36 0.61 rg "+ReportRect(12,y,273,headingHeight)+" f\n");
                for(int c=0;c<count;c++){var lines=Wrap(ReportLabel(sheet.Headers[c]),widths[c]-4,8.8);for(int k=0;k<lines.Count;k++)content.Append(ReportText(lines[k],x+2,y+4.5+k*3.3,8.8,true,Color.White));x+=widths[c];}y+=headingHeight;
                double rowCapacity=190-y;
                while(index<sheet.Rows.Count){
                    string[] data=sheet.Rows[index];bool isTotal=false;foreach(string value in data)if(value=="TOTAL")isTotal=true;
                    var lines=new List<string>[count];double height=8;
                    for(int c=0;c<count;c++){
                        string value=data[c];decimal amount;if(IsMoney(sheet.Headers[c])&&Decimal.TryParse(value,NumberStyles.Float,Inv,out amount))value=amount.ToString("#,##0.##",CultureInfo.GetCultureInfo("fr-FR")).Replace('\u00a0',' ');
                        lines[c]=Wrap(value,widths[c]-4,9.0);height=Math.Max(height,lines[c].Count*3.6+3.6);
                    }
                    if(height>rowCapacity)throw new InvalidOperationException("Une ligne est trop longue pour tenir sur une page : "+sheet.Name+" / "+(index+1));
                    if(y+height>190)break;
                    if(isTotal)content.Append("0.90 0.94 0.98 rg "+ReportRect(12,y,273,height)+" f\n");else if(index%2==1)content.Append("0.975 0.98 0.988 rg "+ReportRect(12,y,273,height)+" f\n");
                    x=12;
                    for(int c=0;c<count;c++){
                        for(int k=0;k<lines[c].Count;k++){
                            string text=lines[c][k];double tx=x+2;if(IsMoney(sheet.Headers[c]))tx=x+widths[c]-2-ReportWidth(text,9.0,isTotal);
                            content.Append(ReportText(text,tx,y+4.8+k*3.6,9.0,isTotal,ink));
                        }x+=widths[c];
                    }
                    y+=height;ReportRule(content,y);index++;
                }
                if(first&&sheet.Rows.Count==0)content.Append(ReportText("Aucune ligne à afficher.",14,y+8,9,false,muted));first=false;
            }while(index<sheet.Rows.Count);
        }
        var objects=new List<byte[]>();for(int i=0;i<4;i++)objects.Add(null);AddPdfFont(objects,2,"Regular",regularWidths);AddPdfFont(objects,3,"Bold",boldWidths);int logoId=ReportLogo(objects,logoPath);var kids=new List<int>();
        for(int i=0;i<pages.Count;i++){
            ReportRule(pages[i],196);pages[i].Append(ReportText("SCOLIA | "+pageNames[i],12,202,8,false,muted));string footer="Page "+(i+1)+" / "+pages.Count;pages[i].Append(ReportText(footer,285-ReportWidth(footer,8,false),202,8,false,muted));
            int streamId=objects.Count+1;objects.Add(ContentStream(Encoding.ASCII.GetBytes(pages[i].ToString())));int pageId=objects.Count+1;objects.Add(Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 841.89 595.276] /Resources << /Font << /FR 3 0 R /FB 4 0 R >> /XObject << /Logo "+logoId+" 0 R >> >> /Contents "+streamId+" 0 R >>"));kids.Add(pageId);
        }
        var references=new StringBuilder();foreach(int kid in kids)references.Append(kid+" 0 R ");objects[0]=Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>");objects[1]=Encoding.ASCII.GetBytes("<< /Type /Pages /Kids ["+references+"] /Count "+kids.Count+" >>");
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var f=new FileStream(temp,FileMode.CreateNew)){Action<string> write=text=>{byte[] bytes=Encoding.ASCII.GetBytes(text);f.Write(bytes,0,bytes.Length);};write("%PDF-1.4\n%SCOLIA\n");var offsets=new List<long>();foreach(byte[] obj in objects){offsets.Add(f.Position);write(offsets.Count+" 0 obj\n");f.Write(obj,0,obj.Length);write("\nendobj\n");}long at=f.Position;write("xref\n0 "+(objects.Count+1)+"\n0000000000 65535 f \n");foreach(long offset in offsets)write(offset.ToString("0000000000",Inv)+" 00000 n \n");write("trailer\n<< /Size "+(objects.Count+1)+" /Root 1 0 R >>\nstartxref\n"+at+"\n%%EOF\n");}Commit(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
    }

}
