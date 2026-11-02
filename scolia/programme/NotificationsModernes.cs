using System;
using System.Drawing;
using System.Windows.Forms;
using System.Media;
using System.IO;
// Confirmations et informations utilisent la même présentation que les formulaires.
public static class ITMMessageBox {
    public static DialogResult Show(string text){return Show(null,text,"SCOLIA",MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(string text,string caption){return Show(null,text,caption,MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(string text,string caption,MessageBoxButtons buttons){return Show(null,text,caption,buttons,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon){return Show(null,text,caption,buttons,icon,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(IWin32Window owner,string text){return Show(owner,text,"SCOLIA",MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(IWin32Window owner,string text,string caption){return Show(owner,text,caption,MessageBoxButtons.OK,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons){return Show(owner,text,caption,buttons,MessageBoxIcon.None,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon){return Show(owner,text,caption,buttons,icon,MessageBoxDefaultButton.Button1);}
    public static DialogResult Show(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton){
        using(var dialog=new ITMStandardDialog(owner,text,caption,buttons,icon,defaultButton))return owner==null?dialog.ShowDialog():dialog.ShowDialog(owner);
    }
}
public sealed class ITMStandardDialog : Form {
    private readonly Font regular,bold;
    private readonly Icon ownedIcon;
    public static DialogResult[] Results(MessageBoxButtons buttons){
        switch(buttons){
            case MessageBoxButtons.OKCancel:return new[]{DialogResult.OK,DialogResult.Cancel};
            case MessageBoxButtons.YesNo:return new[]{DialogResult.Yes,DialogResult.No};
            case MessageBoxButtons.YesNoCancel:return new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel};
            case MessageBoxButtons.RetryCancel:return new[]{DialogResult.Retry,DialogResult.Cancel};
            case MessageBoxButtons.AbortRetryIgnore:return new[]{DialogResult.Abort,DialogResult.Retry,DialogResult.Ignore};
            default:return new[]{DialogResult.OK};
        }
    }
    public static int DefaultIndex(MessageBoxDefaultButton value,int count){return Math.Max(0,Math.Min(count-1,((int)value)/256));}
    private static string Label(DialogResult result){switch(result){case DialogResult.Yes:return "Oui";case DialogResult.No:return "Non";case DialogResult.Cancel:return "Annuler";case DialogResult.Retry:return "Réessayer";case DialogResult.Abort:return "Abandonner";case DialogResult.Ignore:return "Ignorer";default:return "OK";}}
    public ITMStandardDialog(IWin32Window owner,string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton){
        regular=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize);bold=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize,FontStyle.Bold);Font=regular;
        Text="SCOLIA | "+caption;StartPosition=owner==null?FormStartPosition.CenterScreen:FormStartPosition.CenterParent;
        AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);MinimizeBox=MaximizeBox=false;
        var ownerControl=owner as Control;bool dark=ownerControl!=null&&ownerControl.BackColor.GetBrightness()<.36f;
        BackColor=dark?Color.FromArgb(30,35,43):Color.FromArgb(248,250,252);ForeColor=dark?Color.FromArgb(236,242,250):Color.FromArgb(35,43,65);
        try{if(File.Exists(ITMErrorDialog.BrandIconPath)){ownedIcon=new Icon(ITMErrorDialog.BrandIconPath);Icon=ownedIcon;}}catch{}
        var area=ownerControl==null?Screen.FromPoint(Cursor.Position).WorkingArea:Screen.FromControl(ownerControl).WorkingArea;
        int width=Math.Max(240,Math.Min(650,area.Width-32));int measured=TextRenderer.MeasureText(text??"",regular,new Size(width-48,10000),TextFormatFlags.WordBreak).Height;
        ClientSize=new Size(width,Math.Min(Math.Max(210,measured+132),Math.Max(120,area.Height-64)));MinimumSize=new Size(Math.Min(360,area.Width-32),Math.Min(220,area.Height-32));
        var header=new Label{Text=caption,Font=bold,Dock=DockStyle.Top,Height=48,Padding=new Padding(20,12,20,8),AutoEllipsis=true};Controls.Add(header);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Padding=new Padding(14,8,14,10)};Controls.Add(footer);
        var body=new TextBox{Text=text??"",Font=regular,Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,BackColor=BackColor,ForeColor=ForeColor,ScrollBars=ScrollBars.Vertical,WordWrap=true};
        var surface=new Panel{Dock=DockStyle.Fill,Padding=new Padding(20,8,20,12)};surface.Controls.Add(body);Controls.Add(surface);surface.BringToFront();
        var results=Results(buttons);int selected=DefaultIndex(defaultButton,results.Length);Button defaultAction=null;
        for(int i=results.Length-1;i>=0;i--){var result=results[i];var button=new ITMNotificationButton{Text=Label(result),DialogResult=result,Font=regular,Size=new Size(112,38),Margin=new Padding(4),Primary=i==selected};footer.Controls.Add(button);if(i==selected)defaultAction=button;if(result==DialogResult.Cancel||result==DialogResult.No)CancelButton=button;}
        AcceptButton=defaultAction;if(results.Length==1)CancelButton=defaultAction;
        Shown+=delegate{var work=Screen.FromControl(this).WorkingArea;MinimumSize=new Size(Math.Min(MinimumSize.Width,work.Width-24),Math.Min(MinimumSize.Height,work.Height-24));Size=new Size(Math.Min(Width,work.Width-24),Math.Min(Height,work.Height-24));Location=new Point(work.Left+(work.Width-Width)/2,work.Top+(work.Height-Height)/2);if(defaultAction!=null)defaultAction.Focus();};
        FormClosing+=delegate{if(DialogResult==DialogResult.None)DialogResult=Array.IndexOf(results,DialogResult.Cancel)>=0?DialogResult.Cancel:Array.IndexOf(results,DialogResult.No)>=0?DialogResult.No:DialogResult.OK;};
    }
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing){regular.Dispose();bold.Dispose();if(ownedIcon!=null)ownedIcon.Dispose();}}
}
public sealed class ITMErrorDialog : Form {
    public static float InterfaceTextSize=10f;
    public static string SoundPath;
    public static string InstitutionName="ITM Cœur de Dieu";
    public static bool SoundEnabled=true;
    public static int SoundVolume=35;
    public static string SoundTone="Douce";
    private static SoundPlayer sharedPlayer;private static MemoryStream sharedWave;
    public static void PlayNotification(){
        if(!SoundEnabled||SoundVolume<=0)return;
        try{
            if(sharedPlayer!=null){sharedPlayer.Stop();sharedPlayer.Dispose();sharedPlayer=null;}
            if(sharedWave!=null){sharedWave.Dispose();sharedWave=null;}
            var bytes=new MemoryStream(CreateNotificationWave(SoundVolume,SoundTone));
            bytes.Position=0;sharedWave=bytes;sharedPlayer=new SoundPlayer(sharedWave);sharedPlayer.Play();
        }catch{}
    }
    public static byte[] CreateNotificationWave(int volume,string tone){
            const int rate=22050;int samples=(int)(rate*.32);var bytes=new MemoryStream();
            using(var writer=new BinaryWriter(bytes,System.Text.Encoding.ASCII,true)){
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);
                double frequency=tone=="Cloche"?880:tone=="Discrète"?440:660;
                double amplitude=Math.Max(0,Math.Min(100,volume))/100.0*.3;
                for(int i=0;i<samples;i++){double time=i/(double)rate;double envelope=Math.Min(1,time/.02)*Math.Pow(1-i/(double)samples,2);double sample=Math.Sin(time*frequency*2*Math.PI);if(tone=="Cloche")sample=(sample+.25*Math.Sin(time*frequency*4*Math.PI))/1.25;writer.Write((short)(sample*envelope*amplitude*32767));}
            }
        return bytes.ToArray();
    }
    public static string BrandIconPath,BrandImagePath;
    private Icon brandIcon;
    private Image brandImage;
    private SoundPlayer player;
    private static int Scale(int value,float factor){return Math.Max(1,(int)Math.Round(value*factor));}
    public ITMErrorDialog(IWin32Window owner,string message,string title){
        Text=InstitutionName+" | "+title;ShowIcon=false;
        try{if(!String.IsNullOrEmpty(BrandIconPath)&&File.Exists(BrandIconPath)){brandIcon=new Icon(BrandIconPath);Icon=brandIcon;ShowIcon=true;}}catch{}
        StartPosition=FormStartPosition.CenterParent;MinimizeBox=false;MaximizeBox=false;
        AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);Font=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize);
        float scaleFactor=1f;try{using(var g=CreateGraphics())scaleFactor=Math.Max(1f,g.DpiX/96f);}catch{}
        var parent=owner as Control;bool dark=parent!=null&&parent.BackColor.GetBrightness()<.35f;
        BackColor=dark?Color.FromArgb(39,45,54):Color.FromArgb(246,247,249);
        ForeColor=dark?Color.FromArgb(236,242,250):Color.FromArgb(31,41,55);
        var area=owner is Control?Screen.FromControl((Control)owner).WorkingArea:Screen.PrimaryScreen.WorkingArea;
        ClientSize=new Size(Math.Min(Scale(700,scaleFactor),Math.Max(100,area.Width-32)),Math.Min(Scale(460,scaleFactor),Math.Max(100,area.Height-32)));
        MinimumSize=new Size(Math.Min(Scale(460,scaleFactor),area.Width-32),Math.Min(Scale(320,scaleFactor),area.Height-32));
        var layout=new TableLayoutPanel{BackColor=BackColor,Dock=DockStyle.Fill,Padding=new Padding(Scale(24,scaleFactor)),ColumnCount=1,RowCount=3};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,Scale(76,scaleFactor)));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));Controls.Add(layout);
        var header=new TableLayoutPanel{BackColor=BackColor,Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0)};header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,Scale(56,scaleFactor)));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.Controls.Add(header,0,0);
        var mark=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Margin=new Padding(0,Scale(10,scaleFactor),Scale(14,scaleFactor),Scale(10,scaleFactor)),TabStop=false};header.Controls.Add(mark,0,0);
        try{if(!String.IsNullOrEmpty(BrandImagePath)&&File.Exists(BrandImagePath)){using(var source=Image.FromFile(BrandImagePath))brandImage=new Bitmap(source);mark.Image=brandImage;}}catch{}
        if(brandImage==null){header.ColumnStyles[0].Width=0;mark.Visible=false;}
        var heading=new Label{Text=String.IsNullOrWhiteSpace(title)?"Une erreur est survenue":title,Dock=DockStyle.Fill,AutoSize=false,TextAlign=ContentAlignment.MiddleLeft,ForeColor=dark?Color.FromArgb(255,190,175):Color.FromArgb(174,55,50),Font=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize,FontStyle.Bold),AutoEllipsis=true,Margin=new Padding(0)};header.Controls.Add(heading,1,0);
        var details=new TextBox{Text=message??"",Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,BackColor=BackColor,ForeColor=ForeColor,ScrollBars=ScrollBars.Vertical,TabIndex=0,AccessibleName="Détails de l’erreur",Font=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize),Margin=new Padding(0,Scale(6,scaleFactor),0,Scale(8,scaleFactor))};layout.Controls.Add(details,0,1);
        var buttons=new TableLayoutPanel{BackColor=BackColor,Dock=DockStyle.Top,ColumnCount=2,RowCount=1,Padding=new Padding(0,Scale(10,scaleFactor),0,0),Margin=new Padding(0),AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink};layout.Controls.Add(buttons,0,2);
        var close=new ITMNotificationButton{Text="Fermer",Primary=true,Dock=DockStyle.Fill,Height=Scale(42,scaleFactor),DialogResult=DialogResult.OK,Margin=new Padding(Scale(6,scaleFactor),0,0,0),MinimumSize=new Size(Scale(150,scaleFactor),Scale(42,scaleFactor))};buttons.Controls.Add(close,1,0);AcceptButton=close;CancelButton=close;
        var copy=new ITMNotificationButton{Text="Copier le diagnostic",Dock=DockStyle.Fill,Height=Scale(42,scaleFactor),BackColor=BackColor,ForeColor=ForeColor,Margin=new Padding(0,0,Scale(6,scaleFactor),0),MinimumSize=new Size(Scale(190,scaleFactor),Scale(42,scaleFactor))};buttons.Controls.Add(copy,0,0);
        Action applyButtonLayout=delegate{
            bool stacked=ClientSize.Width<Scale(560,scaleFactor);
            buttons.SuspendLayout();
            try{
                buttons.Controls.Clear();buttons.ColumnStyles.Clear();buttons.RowStyles.Clear();buttons.Margin=new Padding(0);buttons.Padding=new Padding(0,Scale(10,scaleFactor),0,0);
                if(stacked){
                    buttons.ColumnCount=1;buttons.RowCount=2;buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    copy.Margin=new Padding(0,0,0,Scale(8,scaleFactor));close.Margin=new Padding(0);buttons.Controls.Add(copy,0,0);buttons.Controls.Add(close,0,1);
                }else{
                    buttons.ColumnCount=2;buttons.RowCount=1;buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,56));buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,44));buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    copy.Margin=new Padding(0,0,Scale(6,scaleFactor),0);close.Margin=new Padding(Scale(6,scaleFactor),0,0,0);buttons.Controls.Add(copy,0,0);buttons.Controls.Add(close,1,0);
                }
                copy.Height=Scale(42,scaleFactor);close.Height=Scale(42,scaleFactor);
            }finally{buttons.ResumeLayout();}
        };
        copy.Click+=delegate{try{Clipboard.SetText(details.Text);copy.Text="Diagnostic copié";}catch{details.SelectAll();details.Focus();}};
        Resize+=delegate{applyButtonLayout();};
        Shown+=delegate{applyButtonLayout();PlayNotification();close.Focus();};
    }
    protected override void Dispose(bool disposing){if(disposing&&player!=null){player.Stop();player.Dispose();player=null;}base.Dispose(disposing);if(disposing){if(brandIcon!=null){brandIcon.Dispose();brandIcon=null;}if(brandImage!=null){brandImage.Dispose();brandImage=null;}}}
    public static DialogResult Show(IWin32Window owner,string message,string title){using(var f=new ITMErrorDialog(owner,message,title)){return owner==null?f.ShowDialog():f.ShowDialog(owner);}}
}


public sealed class ITMSuccessToast : Form {
    private readonly Timer timer;
    private int ticks;
    private readonly Control ownerControl;
    private readonly bool dark;
    private Point targetLocation,startLocation;
    private ITMSuccessToast(Control owner,string title,string message){
        ownerControl=owner;dark=owner!=null&&owner.BackColor.GetBrightness()<.35f;Text=title??"Succès";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=false;
        StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize);BackColor=dark?Color.FromArgb(38,50,44):Color.FromArgb(250,252,250);
        ClientSize=new Size(430,96);Padding=new Padding(0);DoubleBuffered=true;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(16,14,16,14),BackColor=BackColor};
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,54));root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));Controls.Add(root);
        var badge=new Label{Text="✓",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI Symbol",22,FontStyle.Bold),ForeColor=dark?Color.FromArgb(126,231,166):Color.FromArgb(22,125,72),BackColor=dark?Color.FromArgb(43,77,57):Color.FromArgb(232,247,238),Margin=new Padding(0,5,12,5)};root.Controls.Add(badge,0,0);
        var copy=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=BackColor,Margin=new Padding(0)};copy.RowStyles.Add(new RowStyle(SizeType.Absolute,28));copy.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.Controls.Add(copy,1,0);
        var heading=new Label{Text=String.IsNullOrWhiteSpace(title)?"Enregistrement réussi":title,Dock=DockStyle.Fill,Font=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize,FontStyle.Bold),ForeColor=dark?Color.FromArgb(235,246,239):Color.FromArgb(31,55,42),TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};copy.Controls.Add(heading,0,0);
        var body=new Label{Text=message??"",Dock=DockStyle.Fill,Font=new Font("Segoe UI",ITMErrorDialog.InterfaceTextSize),ForeColor=dark?Color.FromArgb(188,207,196):Color.FromArgb(70,86,76),TextAlign=ContentAlignment.TopLeft,AutoEllipsis=true};copy.Controls.Add(body,0,1);
        timer=new Timer{Interval=30};timer.Tick+=delegate{
            ticks++;
            if(ticks<=7){float p=Math.Min(1f,ticks/7f);float ease=1f-(1f-p)*(1f-p);try{Opacity=.06+.92*ease;}catch{};Location=new Point(targetLocation.X,(int)(startLocation.Y+(targetLocation.Y-startLocation.Y)*ease));return;}
            if(ticks>=92){float p=Math.Min(1f,(ticks-92)/8f);try{Opacity=Math.Max(.02,.98*(1f-p));}catch{};if(p>=1f){timer.Stop();Close();}return;}
        };
        Shown+=delegate{PlaceToast();targetLocation=Location;startLocation=new Point(targetLocation.X,targetLocation.Y+14);Location=startLocation;try{Opacity=.06;}catch{}timer.Start();try{ITMErrorDialog.PlayNotification();}catch{}};
        if(ownerControl!=null){ownerControl.LocationChanged+=OwnerMoved;ownerControl.SizeChanged+=OwnerMoved;}
    }
    private void OwnerMoved(object sender,EventArgs e){if(Visible){PlaceToast();targetLocation=Location;if(ticks<=7){startLocation=new Point(targetLocation.X,targetLocation.Y+14);}}}
    private void PlaceToast(){
        try{
            var owner=ownerControl!=null?ownerControl.FindForm():null;Rectangle area=owner!=null?owner.RectangleToScreen(owner.ClientRectangle):Screen.PrimaryScreen.WorkingArea;
            int x=Math.Max(area.Left+12,area.Right-Width-22),y=Math.Max(area.Top+12,area.Bottom-Height-22);Location=new Point(x,y);
        }catch{}
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var pen=new Pen(dark?Color.FromArgb(67,100,80):Color.FromArgb(206,225,213),1)){e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);}
        using(var brush=new SolidBrush(dark?Color.FromArgb(68,193,126):Color.FromArgb(22,125,72)))e.Graphics.FillRectangle(brush,0,10,3,Math.Max(1,Height-20));
    }
    protected override void Dispose(bool disposing){if(disposing){if(timer!=null){timer.Stop();timer.Dispose();}if(ownerControl!=null){ownerControl.LocationChanged-=OwnerMoved;ownerControl.SizeChanged-=OwnerMoved;}}base.Dispose(disposing);}
    public static void Show(Control owner,string title,string message){
        try{var toast=new ITMSuccessToast(owner,title,message);var form=owner==null?null:owner.FindForm();if(form!=null)toast.Show(form);else toast.Show();}catch{}
    }
}

public sealed class ITMNotificationButton : Button {
    public bool Primary {get;set;}
    private bool hover;
    private float hoverBlend,pressBlend,hoverFrom,hoverTo,pressFrom,pressTo;
    private readonly Timer motionTimer=new Timer();
    private readonly System.Diagnostics.Stopwatch motionClock=new System.Diagnostics.Stopwatch();
    private static Color Mix(Color a,Color b,float t){t=Math.Max(0f,Math.Min(1f,t));return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
    private void Animate(float h,float p){hoverFrom=hoverBlend;hoverTo=h;pressFrom=pressBlend;pressTo=p;motionClock.Restart();motionTimer.Start();}
    public ITMNotificationButton(){DoubleBuffered=true;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint,true);motionTimer.Interval=16;motionTimer.Tick+=delegate{float t=Math.Min(1f,(float)(motionClock.Elapsed.TotalMilliseconds/120.0));float ease=1f-(1f-t)*(1f-t);hoverBlend=hoverFrom+(hoverTo-hoverFrom)*ease;pressBlend=pressFrom+(pressTo-pressFrom)*ease;Invalidate();if(t>=1f)motionTimer.Stop();};}
    protected override void OnMouseEnter(EventArgs e){hover=true;Animate(1f,pressBlend);base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Animate(0f,0f);base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){Animate(hover?1f:hoverBlend,1f);}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){bool inside=ClientRectangle.Contains(PointToClient(Cursor.Position));Animate(inside?1f:0f,0f);base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){
        bool dark=Parent!=null&&Parent.BackColor.GetBrightness()<.35f;
        e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(Width<6||Height<6)return;float d=Math.Min(14,Math.Min(Width-3,Height-3));
        using(var path=new System.Drawing.Drawing2D.GraphicsPath()){
            path.AddArc(1,1,d,d,180,90);path.AddArc(Width-d-2,1,d,d,270,90);path.AddArc(Width-d-2,Height-d-2,d,d,0,90);path.AddArc(1,Height-d-2,d,d,90,90);path.CloseFigure();
            Color idle=Primary?(dark?Color.FromArgb(47,83,131):Color.FromArgb(38,91,155)):(dark?Color.FromArgb(58,71,91):Color.White);
            Color over=Primary?Color.FromArgb(19,116,215):(dark?Color.FromArgb(68,81,101):Color.FromArgb(237,244,253));
            Color down=Primary?Color.FromArgb(0,82,166):(dark?Color.FromArgb(49,62,81):Color.FromArgb(224,233,244));
            Color fill=Mix(Mix(idle,over,hoverBlend),down,pressBlend);
            using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
            using(var pen=new Pen(Focused?Color.FromArgb(137,188,249):(dark?Color.FromArgb(85,104,134):Color.FromArgb(220,226,236)),Focused?2:1))e.Graphics.DrawPath(pen,path);
        }
        int offset=(int)Math.Round(pressBlend*e.Graphics.DpiX/96f);
        TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(6,3+offset,Math.Max(1,Width-12),Math.Max(1,Height-6)),Primary?Color.White:(dark?Color.FromArgb(236,242,250):Color.FromArgb(31,41,55)),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
    }
    protected override void Dispose(bool disposing){if(disposing){motionTimer.Stop();motionTimer.Dispose();}base.Dispose(disposing);}
}

