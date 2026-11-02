using System;
using System.Drawing;
using System.Windows.Forms;
using System.Threading;
using System.Reflection;
public sealed class ITMActionButton : Button {
    private bool hover;
    private float hoverBlend, pressBlend;
    private float hoverFrom, hoverTo, pressFrom, pressTo;
    private readonly System.Windows.Forms.Timer motionTimer = new System.Windows.Forms.Timer();
    private readonly System.Diagnostics.Stopwatch motionClock = new System.Diagnostics.Stopwatch();
    public bool Destructive {get;set;}
    public bool Primary {get;set;}
    private static Color Mix(Color a,Color b,float t){t=Math.Max(0f,Math.Min(1f,t));return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
    private void AnimateTo(float hoverTarget,float pressTarget){
        if(IsDisposed||Disposing)return;
        hoverFrom=hoverBlend;hoverTo=hoverTarget;pressFrom=pressBlend;pressTo=pressTarget;
        motionClock.Restart();motionTimer.Start();
    }
    public ITMActionButton() {
        DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint,true);Padding=new Padding(12,4,12,4);Cursor=Cursors.Hand;
        motionTimer.Interval=16;
        motionTimer.Tick+=delegate{
            float p=Math.Min(1f,(float)(motionClock.Elapsed.TotalMilliseconds/125.0));
            float ease=1f-(1f-p)*(1f-p);
            hoverBlend=hoverFrom+(hoverTo-hoverFrom)*ease;pressBlend=pressFrom+(pressTo-pressFrom)*ease;
            Invalidate();if(p>=1f)motionTimer.Stop();
        };
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;AnimateTo(1f,pressBlend);base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;AnimateTo(0f,0f);base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){AnimateTo(hover?1f:hoverBlend,1f);}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){bool inside=ClientRectangle.Contains(PointToClient(Cursor.Position));AnimateTo(inside?1f:0f,0f);base.OnMouseUp(e);}
    protected override void OnEnabledChanged(EventArgs e){if(!Enabled){hover=false;hoverBlend=pressBlend=0f;motionTimer.Stop();}Invalidate();base.OnEnabledChanged(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){
        if(Width<24 || Height<24)return;
        bool dark=BackColor.GetBrightness()<0.35f;
        bool primary=Primary;
        Color idle=primary?(dark?Color.FromArgb(47,82,127):Color.FromArgb(38,91,155)):(dark?Color.FromArgb(39,45,54):Color.White);
        Color over=primary?(dark?Color.FromArgb(57,98,149):Color.FromArgb(46,106,177)):(dark?Color.FromArgb(49,57,69):Color.FromArgb(246,248,251));
        Color down=primary?(dark?Color.FromArgb(35,65,103):Color.FromArgb(30,73,125)):(dark?Color.FromArgb(35,42,51):Color.FromArgb(232,237,244));
        Color fill=Mix(Mix(idle,over,hoverBlend),down,pressBlend);
        if(!Enabled)fill=dark?Color.FromArgb(51,62,79):Color.FromArgb(243,244,246);
        e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float focusMix=Focused?1f:Math.Min(.35f,hoverBlend*.35f);
        Color divider=dark?Color.FromArgb(75,84,97):Color.FromArgb(219,224,231);
        Color accent=dark?Color.FromArgb(166,198,237):Color.FromArgb(38,91,155);
        Color stroke=Mix(divider,accent,focusMix);
        float radius=Math.Min(7*e.Graphics.DpiX/96f,Math.Min(Width,Height)/3f),diameter=radius*2;
        using(var path=new System.Drawing.Drawing2D.GraphicsPath()){
            path.AddArc(1,1,diameter,diameter,180,90);path.AddArc(Width-diameter-2,1,diameter,diameter,270,90);
            path.AddArc(Width-diameter-2,Height-diameter-2,diameter,diameter,0,90);path.AddArc(1,Height-diameter-2,diameter,diameter,90,90);path.CloseFigure();
            using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
            using(var border=new Pen(stroke,Focused?2f:1f))e.Graphics.DrawPath(border,path);
        }
        Color ink=!Enabled?(dark?Color.FromArgb(174,187,207):Color.FromArgb(132,141,155)):(Destructive?(dark?Color.FromArgb(255,180,170):Color.FromArgb(196,43,28)):(primary?Color.White:(dark?Color.FromArgb(236,242,250):Color.FromArgb(35,43,58))));
        float q=e.Graphics.DpiX/96f;int offset=(int)Math.Round(pressBlend*q);
        int iconSize=(int)(16*q),reserve=Width>=140?iconSize+8:0;
        if(reserve>0){
            string label=(Text??"").ToUpperInvariant();Color iconInk=primary?ink:(Destructive?(dark?Color.FromArgb(255,180,170):Color.FromArgb(193,53,57)):ink);
            var state=e.Graphics.Save();e.Graphics.TranslateTransform(Padding.Left,(Height-iconSize)/2f+offset);e.Graphics.ScaleTransform(iconSize/20f,iconSize/20f);e.Graphics.SetClip(new RectangleF(0,0,20,20));
            using(var pen=new Pen(iconInk,1.6f)){pen.StartCap=pen.EndCap=System.Drawing.Drawing2D.LineCap.Round;
                if(Destructive||label.Contains("SUPPRIMER")){e.Graphics.DrawLine(pen,3,4,17,4);e.Graphics.DrawRectangle(pen,5,5,10,13);e.Graphics.DrawLine(pen,8,1,12,1);}
                else if(label.Contains("FERMER")||label.Contains("ANNULER")){e.Graphics.DrawLine(pen,5,5,15,15);e.Graphics.DrawLine(pen,15,5,5,15);}
                else if(label.Contains("ENREGISTR")||label.Contains("SAUVEGARD")||label.Contains("MÉMORIS")){e.Graphics.DrawRectangle(pen,3,2,14,16);e.Graphics.DrawRectangle(pen,7,2,7,5);e.Graphics.DrawRectangle(pen,7,11,7,7);}
                else if(label.Contains("NOUVEAU")||label.Contains("NOUVELLE")||label.Contains("CRÉER")||label.Contains("INSCRIRE")){e.Graphics.DrawLine(pen,10,3,10,17);e.Graphics.DrawLine(pen,3,10,17,10);}
                else if(label.Contains("MODIFIER")||label.Contains("CORRIGER")){e.Graphics.DrawPolygon(pen,new Point[]{new Point(3,17),new Point(5,12),new Point(14,3),new Point(18,7),new Point(8,16)});}
                else if(label.Contains("PRISE")&&label.Contains("CHARGE")){e.Graphics.DrawEllipse(pen,2,2,6,6);e.Graphics.DrawEllipse(pen,12,2,6,6);e.Graphics.DrawArc(pen,1,10,8,10,180,180);e.Graphics.DrawArc(pen,11,10,8,10,180,180);}
                else if(label.Contains("PAIEMENT")||label.Contains("REÇU")){e.Graphics.DrawRectangle(pen,2,4,16,12);e.Graphics.DrawEllipse(pen,7,6,6,8);}
                else if(label.Contains("COMPTE")||label.Contains("RÔLE")){e.Graphics.DrawEllipse(pen,6,2,8,8);e.Graphics.DrawArc(pen,2,12,16,14,180,180);}
                else if(label.Contains("PDF")||label.Contains("WORD")||label.Contains("CSV")){e.Graphics.DrawLines(pen,new Point[]{new Point(13,2),new Point(3,2),new Point(3,18),new Point(17,18),new Point(17,6),new Point(13,2)});e.Graphics.DrawLine(pen,13,2,13,6);e.Graphics.DrawLine(pen,13,6,17,6);if(label.Contains("CSV")){e.Graphics.DrawRectangle(pen,6,8,8,7);e.Graphics.DrawLine(pen,6,11.5f,14,11.5f);e.Graphics.DrawLine(pen,10,8,10,15);}else if(label.Contains("PDF")){e.Graphics.DrawBezier(pen,6,16,15,5,6,5,10,13);e.Graphics.DrawBezier(pen,10,13,18,16,14,8,6,16);}else{e.Graphics.DrawLine(pen,6,9,14,9);e.Graphics.DrawLine(pen,6,12,14,12);e.Graphics.DrawLine(pen,6,15,12,15);}}
                else if(label.Contains("EXPORT")||label.Contains("TÉLÉCHARG")){e.Graphics.DrawLine(pen,10,2,10,13);e.Graphics.DrawLines(pen,new Point[]{new Point(6,9),new Point(10,13),new Point(14,9)});e.Graphics.DrawLines(pen,new Point[]{new Point(3,14),new Point(3,18),new Point(17,18),new Point(17,14)});}
                else if(label.Contains("OUVRIR")||label.Contains("DOSSIER")||label.Contains("CONNECT")){e.Graphics.DrawLines(pen,new Point[]{new Point(2,16),new Point(2,4),new Point(8,4),new Point(11,7),new Point(18,7)});e.Graphics.DrawPolygon(pen,new Point[]{new Point(2,16),new Point(5,9),new Point(19,9),new Point(16,16)});}
                else if(label.Contains("ACTUALIS")){e.Graphics.DrawArc(pen,3,3,14,14,35,290);e.Graphics.DrawLines(pen,new Point[]{new Point(17,2),new Point(17,8),new Point(12,8)});}
                else{e.Graphics.DrawRectangle(pen,4,2,12,16);e.Graphics.DrawLine(pen,7,7,13,7);e.Graphics.DrawLine(pen,7,12,13,12);}
            }e.Graphics.Restore(state);
        }
        var text=new Rectangle(Padding.Left+reserve,Padding.Top+offset,Math.Max(1,Width-Padding.Horizontal-reserve),Math.Max(1,Height-Padding.Vertical));
        Font fitted=null;
        try {
            Font drawFont=Font;float size=Font.SizeInPoints;
            while(size>8f && TextRenderer.MeasureText(e.Graphics,Text,drawFont,new Size(text.Width,10000),TextFormatFlags.WordBreak).Height>text.Height){
                size-=0.5f;if(fitted!=null)fitted.Dispose();fitted=new Font(Font.FontFamily,size,Font.Style);drawFont=fitted;
            }
            TextRenderer.DrawText(e.Graphics,Text,drawFont,text,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak);
        } finally { if(fitted!=null)fitted.Dispose(); }
        if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-10,Height-10),ink,fill);
    }
    protected override void Dispose(bool disposing){if(disposing){motionTimer.Stop();motionTimer.Dispose();}base.Dispose(disposing);}
}
public sealed class ITMSurfacePanel : Panel {
    public ITMSurfacePanel() { DoubleBuffered=true; SetStyle(ControlStyles.ResizeRedraw,true); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        if(Width<28 || Height<28) return;
        using(var pen=new Pen(BackColor.GetBrightness()<.35f?Color.FromArgb(85,104,134):Color.FromArgb(220,226,236)))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
    }
}
public sealed class ITMActivity : IDisposable {
    public static float InterfaceTextSize=10f;
    private volatile bool stopped;
    private Form window;
    private ActivityCard card;
    private readonly object gate = new object();
    private string message;
    private readonly Rectangle placement;
    private readonly IntPtr owner;
    private readonly bool darkMode;
    private readonly bool transitionMode;
    private bool visible, finished;

    private sealed class Owner : IWin32Window {
        private readonly IntPtr handle;
        public Owner(IntPtr value) { handle=value; }
        public IntPtr Handle { get { return handle; } }
    }

    private sealed class Toast : Form {
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams {
            get {
                var p=base.CreateParams;
                // WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW: indicateur discret, sans voler le focus.
                p.ExStyle |= 0x080000A0;
                return p;
            }
        }
    }

    private sealed class ActivityCard : Control {
        private float angle;
        private string messageText;
        public bool DarkMode;
        public bool TransitionMode;
        public double ElapsedSeconds;

        public ActivityCard(string text,bool dark) {
            messageText=text??"Traitement en cours";
            DarkMode=dark;
            DoubleBuffered=true;
            SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint,true);
            AccessibleRole=AccessibleRole.StatusBar;
            AccessibleName="Traitement en cours";
            AccessibleDescription=messageText;
            BackColor=dark?Color.FromArgb(39,45,54):Color.White;
        }

        public string Message {
            get { return messageText; }
            set {
                messageText=String.IsNullOrWhiteSpace(value)?"Traitement en cours":value;
                AccessibleDescription=messageText;
                Invalidate();
            }
        }

        public float Angle {
            get { return angle; }
            set { angle=value; Invalidate(); }
        }

        public static System.Drawing.Drawing2D.GraphicsPath WindowShape(Rectangle bounds,float radius){return Rounded(new RectangleF(bounds.X,bounds.Y,bounds.Width,bounds.Height),radius);}

        private static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r,float radius) {
            var path=new System.Drawing.Drawing2D.GraphicsPath();
            radius=Math.Max(1,Math.Min(radius,Math.Min(r.Width,r.Height)/2));
            float d=radius*2;
            path.AddArc(r.Left,r.Top,d,d,180,90);
            path.AddArc(r.Right-d,r.Top,d,d,270,90);
            path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);
            path.AddArc(r.Left,r.Bottom-d,d,d,90,90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e) {
            float q=e.Graphics.DpiX/96f;
            Color surface=DarkMode?Color.FromArgb(39,45,54):Color.White;
            Color ink=DarkMode?Color.FromArgb(237,240,244):Color.FromArgb(31,41,55);
            Color muted=DarkMode?Color.FromArgb(189,199,211):Color.FromArgb(91,105,122);
            Color accent=DarkMode?Color.FromArgb(166,198,237):Color.FromArgb(38,91,155);
            Color track=DarkMode?Color.FromArgb(69,79,94):Color.FromArgb(229,235,243);
            e.Graphics.Clear(surface);
            if(Width<100 || Height<40)return;
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int pad=Math.Max(8,(int)(18*q));
            bool compact=Height<100*q;
            using(var outline=Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),10*q))
            using(var border=new Pen(track))e.Graphics.DrawPath(border,outline);
            int diameter=(int)((compact?24:32)*q),left=pad;
            int top=compact?(Height-diameter)/2:pad;
            using(var pen=new Pen(track,3*q))e.Graphics.DrawEllipse(pen,left,top,diameter,diameter);
            using(var pen=new Pen(accent,3*q)){
                pen.StartCap=pen.EndCap=System.Drawing.Drawing2D.LineCap.Round;
                e.Graphics.DrawArc(pen,left,top,diameter,diameter,angle-90,245);
            }
            int textLeft=left+diameter+(int)(14*q),textWidth=Math.Max(1,Width-textLeft-pad);
            using(var titleFont=new Font("Segoe UI",ITMActivity.InterfaceTextSize,FontStyle.Bold))
            using(var detailFont=new Font("Segoe UI",ITMActivity.InterfaceTextSize,FontStyle.Regular)) {
                if(compact){
                    TextRenderer.DrawText(e.Graphics,messageText,detailFont,new Rectangle(textLeft,pad,textWidth,Math.Max(1,Height-2*pad)),ink,TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
                    return;
                }
                int titleHeight=Math.Max((int)(23*q),titleFont.Height+4);
                TextRenderer.DrawText(e.Graphics,TransitionMode?(textWidth<240*q?"Préparation":"Préparation de votre espace"):"Traitement en cours",titleFont,new Rectangle(textLeft,pad,textWidth,titleHeight),ink,TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
                int detailTop=pad+titleHeight+(int)(3*q),footerTop=Height-(int)(40*q);
                // The detail and elapsed-time regions never overlap, including long messages.
                TextRenderer.DrawText(e.Graphics,messageText,detailFont,new Rectangle(textLeft,detailTop,textWidth,Math.Max(1,footerTop-detailTop-(int)(6*q))),muted,TextFormatFlags.NoPrefix|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis);
                string footer=ElapsedSeconds>=12?"Toujours en cours · "+((int)ElapsedSeconds)+" s":"Veuillez patienter…";
                TextRenderer.DrawText(e.Graphics,footer,detailFont,new Rectangle(pad,footerTop,Width-2*pad,(int)(20*q)),muted,TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
            }
            int w=Math.Max(1,Width-2*pad),barY=Height-(int)(14*q),barH=Math.Max(3,(int)(3*q));
            using(var shape=Rounded(new RectangleF(pad,barY,w,barH),barH/2f))
            using(var brush=new SolidBrush(track))e.Graphics.FillPath(brush,shape);
            // Indeterminate animation, no invented completion percentage.
            int segment=Math.Max(16,w/4),offset=(int)((angle/360f)*(w+segment))-segment;
            int start=Math.Max(0,offset),end=Math.Min(w,offset+segment);
            if(end>start)using(var brush=new SolidBrush(accent))e.Graphics.FillRectangle(brush,pad+start,barY,end-start,barH);
        }

    }

    public ITMActivity(string text, Rectangle bounds, IntPtr parent, string logoPath) : this(text,bounds,parent,logoPath,false) {}
    public ITMActivity(string text, Rectangle bounds, IntPtr parent, string logoPath, bool dark) : this(text,bounds,parent,logoPath,dark,false) {}
    public ITMActivity(string text, Rectangle bounds, IntPtr parent, string logoPath, bool dark, bool transition) {
        darkMode=dark;
        transitionMode=transition;
        message=String.IsNullOrWhiteSpace(text)?"Traitement en cours":text;
        placement=bounds;
        owner=parent;
        var thread=new Thread(Run);
        thread.IsBackground=true;
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void Run() {
        try { RunCore(); }
        catch { /* L'indicateur ne doit jamais interrompre une transaction. */ }
        finally { lock(gate){finished=true;Monitor.PulseAll(gate);} }
    }

    // Bounded handshake: the outgoing screen remains until the first card is painted.
    public bool WaitUntilVisible(int timeoutMilliseconds) {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        lock(gate){
            while(!visible && !finished && !stopped){
                int remaining=timeoutMilliseconds-(int)clock.ElapsedMilliseconds;
                if(remaining<=0)return false;
                Monitor.Wait(gate,remaining);
            }
            return visible && !stopped;
        }
    }

    private void RunCore() {
        // Aucun objet Excel/COM ni callback PowerShell ne traverse ce thread UI.
        using(var form=new Toast())
        using(var timer=new System.Windows.Forms.Timer()) {
            form.FormBorderStyle=FormBorderStyle.None;
            form.ControlBox=false;
            form.ShowInTaskbar=false;
            form.StartPosition=FormStartPosition.Manual;
            form.AutoScaleMode=AutoScaleMode.None;
            form.Bounds=placement;
            if(transitionMode){
                float scale;
                using(var graphics=form.CreateGraphics()){scale=Math.Max(1f,graphics.DpiX/96f);}
                Rectangle area=Screen.GetWorkingArea(placement);
                int width=Math.Min((int)(480*scale),Math.Max(120,area.Width-32));
                int height=Math.Min((int)(164*scale),Math.Max(42,area.Height-32));
                int x=placement.Left+placement.Width/2-width/2,y=placement.Top+placement.Height/2-height/2;
                form.Bounds=new Rectangle(Math.Max(area.Left,Math.Min(x,area.Right-width)),Math.Max(area.Top,Math.Min(y,area.Bottom-height)),width,height);
            }
            Rectangle safeArea=Screen.GetWorkingArea(form.Bounds);
            int safeWidth=Math.Min(form.Width,Math.Max(100,safeArea.Width-16));
            int safeHeight=Math.Min(form.Height,Math.Max(40,safeArea.Height-16));
            form.Bounds=new Rectangle(Math.Max(safeArea.Left+8,Math.Min(form.Left,safeArea.Right-safeWidth-8)),Math.Max(safeArea.Top+8,Math.Min(form.Top,safeArea.Bottom-safeHeight-8)),safeWidth,safeHeight);
            using(var shape=ActivityCard.WindowShape(form.ClientRectangle,10))form.Region=new Region(shape);
            form.BackColor=darkMode?Color.FromArgb(39,45,54):Color.White;
            form.TopMost=(owner==IntPtr.Zero);
            try{form.Opacity=transitionMode ? .98 : .04;}catch{}

            string initialMessage;
            lock(gate){initialMessage=message;}
            var currentCard=new ActivityCard(initialMessage,darkMode);
            currentCard.TransitionMode=transitionMode;
            currentCard.Dock=DockStyle.Fill;
            currentCard.AccessibleName=transitionMode?"Préparation de votre espace":"Traitement en cours";
            // Full text remains available when the visual card must ellipsize it.
            using(var tip=new ToolTip()) {
            tip.AutoPopDelay=15000;tip.SetToolTip(currentCard,initialMessage);
            form.Controls.Add(currentCard);
            timer.Interval=32; // ~31 FPS : animation fluide, charge UI faible.
            var clock=System.Diagnostics.Stopwatch.StartNew();
            timer.Tick+=(s,e)=>{
                if(stopped){
                    try{form.Opacity=Math.Max(0,form.Opacity-.20);}catch{}
                    if(form.Opacity<=.05){timer.Stop();form.Close();}
                    return;
                }
                try{if(form.Opacity<.98)form.Opacity=Math.Min(.98,form.Opacity+.16);}catch{}
                string latest;
                lock(gate){latest=message;}
                if(currentCard.Message!=latest){currentCard.Message=latest;tip.SetToolTip(currentCard,latest);}
                currentCard.ElapsedSeconds=clock.Elapsed.TotalSeconds;
                currentCard.Angle=(float)((clock.Elapsed.TotalMilliseconds*.30)%360);
            };
            form.Shown+=(s,e)=>{
                if(stopped){form.Close();return;}
                lock(gate){currentCard.Message=message;}
                form.Refresh();
                lock(gate){visible=true;Monitor.PulseAll(gate);}
                timer.Start();
            };

            lock(gate){window=form;card=currentCard;}
            try {
                if(!stopped) {
                    if(owner!=IntPtr.Zero)form.Show(new Owner(owner));
                    else form.Show();
                    Application.Run(form);
                }
            }
            finally {
                lock(gate){window=null;card=null;}
            }
            }
        }
    }

    public void Update(string text) {
        lock(gate) {
            message=String.IsNullOrWhiteSpace(text)?"Traitement en cours":text;
        }
    }

    public void Dispose() {
        // La fermeture est reprise par le timer UI afin de produire une disparition douce
        // sans bloquer le thread qui termine l'enregistrement.
        lock(gate){stopped=true;Monitor.PulseAll(gate);}
    }

    public static void BufferGrid(DataGridView grid) {
        typeof(DataGridView).GetProperty("DoubleBuffered",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(grid,true,null);
    }
}
