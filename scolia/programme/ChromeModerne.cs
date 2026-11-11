using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public static class ITMDesign {
    public static float InterfaceTextSize=10f;
    public static Color Blue=Color.FromArgb(38,91,155);
    public static Color Divider(bool dark){return dark?Color.FromArgb(75,84,97):Color.FromArgb(219,224,231);}
    public static GraphicsPath Rounded(RectangleF r,float radius){
        var path=new GraphicsPath();radius=Math.Max(0,Math.Min(radius,Math.Min(r.Width,r.Height)/2));
        if(radius<1){path.AddRectangle(r);return path;}
        float d=radius*2;path.AddArc(r.Left,r.Top,d,d,180,90);path.AddArc(r.Right-d,r.Top,d,d,270,90);
        path.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);path.AddArc(r.Left,r.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    public static Color SymbolColor(string symbol,bool dark){
        Color c;
        switch(symbol){
            case "pdf":case "delete":case "close":c=Color.FromArgb(193,53,57);break;
            case "csv":case "workbook":case "check":case "save":c=Color.FromArgb(27,126,84);break;
            case "sponsor":case "account":case "lock":case "settings":case "grades":c=Color.FromArgb(112,75,176);break;
            case "restore":case "calendar":c=Color.FromArgb(171,94,16);break;
            default:c=Blue;break;
        }
        if(dark)return Color.FromArgb(Math.Min(255,c.R+75),Math.Min(255,c.G+75),Math.Min(255,c.B+60));return c;
    }
}

public sealed class ITMWorkspaceTab : Button {
    private bool active,hover,compact,iconOnly;
    private float hoverBlend,activeBlend,pressBlend;
    private float hoverFrom,hoverTo,activeFrom,activeTo,pressFrom,pressTo;
    private readonly Timer motionTimer=new Timer();
    private readonly System.Diagnostics.Stopwatch motionClock=new System.Diagnostics.Stopwatch();
    private static Color Mix(Color a,Color b,float t){t=Math.Max(0f,Math.Min(1f,t));return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
    private void Animate(float h,float a,float p){hoverFrom=hoverBlend;hoverTo=h;activeFrom=activeBlend;activeTo=a;pressFrom=pressBlend;pressTo=p;motionClock.Restart();motionTimer.Start();}
    public bool Compact {get{return compact;} set{if(compact!=value){compact=value;Invalidate();}}}
    public bool IconOnly {get{return iconOnly;} set{if(iconOnly!=value){iconOnly=value;Invalidate();}}}
    public bool Active { get{return active;} set{if(active!=value){active=value;Animate(hover?1f:0f,active?1f:0f,pressBlend);}} }
    public ITMWorkspaceTab(){
        DoubleBuffered=true;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint,true);
        motionTimer.Interval=16;motionTimer.Tick+=delegate{float p=Math.Min(1f,(float)(motionClock.Elapsed.TotalMilliseconds/145.0));float ease=1f-(1f-p)*(1f-p);hoverBlend=hoverFrom+(hoverTo-hoverFrom)*ease;activeBlend=activeFrom+(activeTo-activeFrom)*ease;pressBlend=pressFrom+(pressTo-pressFrom)*ease;Invalidate();if(p>=1f)motionTimer.Stop();};
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Animate(1f,active?1f:0f,pressBlend);base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Animate(0f,active?1f:0f,0f);base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){Animate(hover?1f:hoverBlend,active?1f:0f,1f);}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){bool inside=ClientRectangle.Contains(PointToClient(Cursor.Position));Animate(inside?1f:0f,active?1f:0f,0f);base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){
        bool dark=BackColor.GetBrightness()<0.35f;
        e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color idle=BackColor,over=dark?Color.FromArgb(43,51,62):Color.FromArgb(243,245,248),selected=dark?Color.FromArgb(55,72,96):Color.FromArgb(255,255,255);
        Color tabFill=Mix(Mix(idle,over,hoverBlend),selected,activeBlend);
        if(hoverBlend>.01f||activeBlend>.01f){
            using(var fill=new SolidBrush(tabFill))e.Graphics.FillRectangle(fill,new RectangleF(0,0,Width,Math.Max(1,Height-1)));
        }
        float q=e.Graphics.DpiX/96f;int pad=(int)((compact?7:10)*q),icon=(int)((compact?16:18)*q),offset=(int)Math.Round(pressBlend*q);
        int gap=(int)((compact?4:6)*q),textLeft=pad;
        if(iconOnly){
            int centered=Math.Max((int)(20*q),icon);int ix=(Width-centered)/2,iy=(Height-centered)/2+offset;
            if(Image!=null)e.Graphics.DrawImage(Image,new Rectangle(ix,iy,centered,centered));
        }else{
            if(Image!=null){e.Graphics.DrawImage(Image,new Rectangle(pad,(Height-icon)/2+offset,icon,icon));textLeft=pad+icon+gap;}
            var r=new Rectangle(textLeft,(int)(2*q)+offset,Math.Max(1,Width-textLeft-pad),Math.Max(1,Height-(int)(6*q)));
            Color textIdle=Enabled?(dark?Color.FromArgb(232,237,244):Color.FromArgb(57,68,84)):(dark?Color.FromArgb(193,206,224):Color.FromArgb(144,151,170));
            Color textActive=dark?Color.FromArgb(211,231,255):Color.FromArgb(27,95,171);
            var flags=TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine;
            TextRenderer.DrawText(e.Graphics,Text,Font,r,Mix(textIdle,textActive,activeBlend),flags);
        }
        if(activeBlend>.01f){int full=Math.Max(1,Width-pad*2);int width=Math.Max(3,(int)(full*activeBlend));int x=pad+(full-width)/2;using(var brush=new SolidBrush(dark?Color.FromArgb(173,211,255):Color.FromArgb(27,95,171)))e.Graphics.FillRectangle(brush,x,Height-Math.Max(3,(int)(3*q)),width,Math.Max(3,(int)(3*q)));}
        if(Focused && ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(4,4,Width-8,Height-8));
    }
    protected override void Dispose(bool disposing){if(disposing){motionTimer.Stop();motionTimer.Dispose();}base.Dispose(disposing);}
}
public sealed class ITMRibbonPanel : Panel {
    public ITMRibbonPanel(){DoubleBuffered=true;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaintBackground(PaintEventArgs e){
        if(Width<=0||Height<=0)return;
        e.Graphics.Clear(BackColor);
        using(var pen=new Pen(BackColor.GetBrightness()<0.35f?ITMDesign.Divider(true):Color.FromArgb(216,224,239)))e.Graphics.DrawLine(pen,0,Height-1,Width,Height-1);
    }
}
public sealed class ITMModernTabs : TabControl {
    private bool sizing;
    protected override void OnResize(EventArgs e){base.OnResize(e);FitTabs();}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);FitTabs();}
    protected override void OnControlAdded(ControlEventArgs e){base.OnControlAdded(e);FitTabs();}
    private void FitTabs(){
        if(IsDisposed || Disposing || sizing || TabPages.Count==0 || ClientSize.Width<40)return;sizing=true;
        try{
            int reserve=(int)(20*DeviceScale()),w=90,h=24;
            foreach(TabPage page in TabPages)w=Math.Max(w,TextRenderer.MeasureText(page.Text,Font).Width+reserve);
            w=Math.Max(40,Math.Min(w,ClientSize.Width-8));
            foreach(TabPage page in TabPages)h=Math.Max(h,TextRenderer.MeasureText(page.Text,Font,new Size(Math.Max(20,w-reserve),10000),TextFormatFlags.WordBreak).Height+12);
            var next=new Size(w,h);if(ItemSize!=next)ItemSize=next;
        }finally{sizing=false;}
    }
    private float DeviceScale(){using(var g=CreateGraphics())return Math.Max(1,g.DpiX/96f);}
    public ITMModernTabs(){Multiline=true;DrawMode=TabDrawMode.OwnerDrawFixed;ItemSize=new Size(160,40);Padding=new Point(18,10);}
    protected override void OnDrawItem(DrawItemEventArgs e){
        bool selected=e.Index==SelectedIndex;Rectangle r=e.Bounds;bool dark=Parent!=null&&Parent.BackColor.GetBrightness()<0.35f;
        using(var brush=new SolidBrush(dark?(selected?Color.FromArgb(61,87,126):Color.FromArgb(46,57,74)):(selected?Color.White:Color.FromArgb(244,247,252))))e.Graphics.FillRectangle(brush,r);
        TextRenderer.DrawText(e.Graphics,TabPages[e.Index].Text,Font,Rectangle.Inflate(r,-9,-4),dark?Color.FromArgb(232,237,244):ITMDesign.Blue,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        if(selected)using(var brush=new SolidBrush(dark?Color.FromArgb(173,211,255):ITMDesign.Blue))e.Graphics.FillRectangle(brush,r.Left+8,r.Bottom-3,Math.Max(1,r.Width-16),3);
        if((e.State&DrawItemState.Focus)!=0 && ShowFocusCues)e.DrawFocusRectangle();
    }
}
public static class ITMVisualTheme {
    private static readonly System.Collections.Generic.HashSet<Form> prepared=new System.Collections.Generic.HashSet<Form>();
    public static void PrepareWindow(Form form){
        if(form==null || form.IsDisposed || !prepared.Add(form))return;
        form.Disposed+=delegate{prepared.Remove(form);};
        form.Shown+=delegate{
            if(!form.TopLevel||form.WindowState!=FormWindowState.Normal)return;
            Rectangle area=Screen.FromControl(form.Owner??form).WorkingArea;
            int maxWidth=Math.Max(100,area.Width-24),maxHeight=Math.Max(100,area.Height-24);
            form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,maxWidth),Math.Min(form.MinimumSize.Height,maxHeight));
            form.Size=new Size(Math.Min(form.Width,maxWidth),Math.Min(form.Height,maxHeight));
            form.Location=new Point(Math.Max(area.Left+12,Math.Min(form.Left,area.Right-form.Width-12)),Math.Max(area.Top+12,Math.Min(form.Top,area.Bottom-form.Height-12)));
        };
        PrepareFooters(form);
    }
    private static void PrepareFooters(Control root){
        var flow=root as FlowLayoutPanel;
        if(flow!=null && flow.Dock==DockStyle.Bottom){
            flow.WrapContents=true;flow.AutoSize=true;flow.AutoSizeMode=AutoSizeMode.GrowAndShrink;
            var original=new System.Collections.Generic.Dictionary<Control,int>();
            foreach(Control c in flow.Controls)if(c is ButtonBase)original[c]=Math.Max(c.Width,c.PreferredSize.Width);
            EventHandler arrange=delegate{
                if(flow.IsDisposed)return;
                int available=Math.Max(24,flow.ClientSize.Width-flow.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth);
                foreach(Control c in flow.Controls){
                    if(!(c is ButtonBase))continue;
                    if(!original.ContainsKey(c))original[c]=Math.Max(c.Width,c.PreferredSize.Width);
                    int width=Math.Max(20,Math.Min(original[c],available-c.Margin.Horizontal));
                    c.MaximumSize=new Size(width,0);
                    c.Width=width;
                    int measured=TextRenderer.MeasureText(c.Text,c.Font,new Size(Math.Max(1,width-c.Padding.Horizontal-(width>=140?24:0)),10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height;
                    c.Height=Math.Max(36,Math.Max(c.Font.Height+16,measured+c.Padding.Vertical+12));
                }
            };
            flow.SizeChanged+=arrange;flow.ControlAdded+=delegate{arrange(flow,EventArgs.Empty);};
            var owner=flow.FindForm();if(owner!=null)owner.Shown+=arrange;
        }
        foreach(Control child in root.Controls)PrepareFooters(child);
    }

    public static void Apply(Control root){
        var form=root as Form;if(form!=null)form.BackColor=Color.FromArgb(246,247,249);
        if(root is Panel || root is TabPage || root is Label || root is CheckBox){
            int color=root.BackColor.ToArgb();
            if(color==Color.FromArgb(245,247,251).ToArgb() || color==Color.FromArgb(244,247,251).ToArgb() || color==Color.FromArgb(241,243,248).ToArgb() || color==Color.FromArgb(244,247,252).ToArgb())root.BackColor=Color.FromArgb(246,247,249);
        }
        var label=root as Label;if(label!=null && label.ForeColor.ToArgb()==Color.FromArgb(30,41,59).ToArgb())label.ForeColor=Color.FromArgb(36,51,73);
        var grid=root as DataGridView;
        if(grid!=null){grid.BackgroundColor=Color.White;grid.GridColor=Color.FromArgb(230,233,239);grid.BorderStyle=BorderStyle.None;grid.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(245,247,250);grid.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(57,77,104);grid.DefaultCellStyle.SelectionBackColor=Color.FromArgb(229,238,253);grid.DefaultCellStyle.SelectionForeColor=Color.FromArgb(26,54,97);grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(250,250,250);}
        var text=root as TextBox;
        if(text!=null){text.BorderStyle=(text.Parent is ITMFieldSurface || text.Parent is ITMPasswordSurface)?BorderStyle.None:BorderStyle.FixedSingle;text.BackColor=text.ReadOnly?Color.FromArgb(241,243,249):Color.White;text.ForeColor=Color.FromArgb(35,43,65);}
        var combo=root as ComboBox;if(combo!=null){combo.FlatStyle=FlatStyle.Standard;combo.BackColor=Color.White;combo.ForeColor=Color.FromArgb(35,43,65);}
        foreach(Control child in root.Controls)Apply(child);
    }
}

public sealed class ITMSectionPanel : Panel {
    private int headerHeight=-1;
    public int HeaderHeight {get{return headerHeight;}set{if(headerHeight!=value){headerHeight=value;Invalidate();}}}
    public ITMSectionPanel(){DoubleBuffered=true;BackColor=Color.White;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        using(var border=new Pen(BackColor.GetBrightness()<0.35f?ITMDesign.Divider(true):ITMDesign.Divider(false)))e.Graphics.DrawRectangle(border,0,0,Math.Max(0,Width-1),Math.Max(0,Height-1));
        int y=HeaderHeight>=0?HeaderHeight:(int)(51*e.Graphics.DpiY/96f);
        if(y<Height)using(var pen=new Pen(BackColor.GetBrightness()<0.35f?ITMDesign.Divider(true):ITMDesign.Divider(false)))e.Graphics.DrawLine(pen,12,y,Math.Max(12,Width-12),y);
    }
}

// The registration keeps the native editors, their keyboard behavior and accessibility.
public sealed class ITMFormSection : Panel {
    public ITMFormSection(){DoubleBuffered=true;BackColor=Color.White;TabStop=false;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaintBackground(PaintEventArgs e){
        e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=ITMDesign.Rounded(new RectangleF(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3)),6*e.Graphics.DpiX/96f))
        using(var brush=new SolidBrush(BackColor))e.Graphics.FillPath(brush,shape);
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Width<4||Height<4)return;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=ITMDesign.Rounded(new RectangleF(1,1,Width-3,Height-3),6*e.Graphics.DpiX/96f))
        using(var pen=new Pen(ITMDesign.Divider(BackColor.GetBrightness()<.35f)))e.Graphics.DrawPath(pen,shape);
    }
}
public sealed class ITMFieldSurface : Panel {
    private readonly Control editor;
    private bool invalid;
    public Control Editor {get{return editor;}}
    public bool Invalid {get{return invalid;}set{if(invalid!=value){invalid=value;Invalidate();}}}
    public ITMFieldSurface(Control input){
        if(input==null)throw new ArgumentNullException("input");
        DoubleBuffered=true;TabStop=false;BackColor=Color.White;Padding=new Padding(12,6,12,6);
        SetStyle(ControlStyles.ResizeRedraw,true);editor=input;
        input.Dock=DockStyle.None;input.Anchor=AnchorStyles.Top|AnchorStyles.Left;input.TabIndex=0;
        var text=input as TextBoxBase;if(text!=null){text.BorderStyle=BorderStyle.None;text.AutoSize=false;}
        var combo=input as ComboBox;if(combo!=null)combo.FlatStyle=FlatStyle.Flat;
        Controls.Add(input);
        input.Enter+=delegate{Invalidate();};input.Leave+=delegate{Invalidate();};
        input.FontChanged+=delegate{PerformLayout();};input.EnabledChanged+=delegate{Invalidate();};
    }
    protected override void OnLayout(LayoutEventArgs e){
        base.OnLayout(e);if(editor==null||editor.IsDisposed||IsDisposed)return;
        int width=Math.Max(1,ClientSize.Width-Padding.Horizontal);
        int available=Math.Max(1,ClientSize.Height-Padding.Vertical);
        var text=editor as TextBoxBase;
        int height=text!=null&&text.Multiline?available:Math.Min(available,Math.Max(editor.Font.Height+4,editor.PreferredSize.Height));
        editor.SetBounds(Padding.Left,Padding.Top+(available-height)/2,width,Math.Max(1,height));
    }
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(editor.Enabled)editor.Focus();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Width<4||Height<4)return;
        bool dark=BackColor.GetBrightness()<.35f;
        Color stroke=invalid?(dark?Color.FromArgb(241,143,144):Color.FromArgb(173,54,61)):
            (ContainsFocus?(dark?Color.FromArgb(154,192,237):ITMDesign.Blue):(dark?Color.FromArgb(105,119,139):Color.FromArgb(135,148,166)));
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=ITMDesign.Rounded(new RectangleF(1,1,Width-3,Height-3),5*e.Graphics.DpiX/96f))
        using(var pen=new Pen(stroke,ContainsFocus?1.6f:1f))e.Graphics.DrawPath(pen,shape);
    }
}

// Native desktop login: visual controls only. Authentication remains in Securite.ps1.
public sealed class ITMLoginButton : Button {
    public bool Primary {get;set;}
    public bool SquareCorners {get;set;}
    private bool hover,pressed;
    private float hoverBlend;
    private readonly Timer hoverTimer=new Timer();
    private readonly System.Diagnostics.Stopwatch hoverClock=new System.Diagnostics.Stopwatch();
    private float blendStart,blendEnd;
    private void AnimateHover(bool active){if(IsDisposed||Disposing)return;blendStart=hoverBlend;blendEnd=active?1f:0f;hoverClock.Restart();hoverTimer.Start();}
    public ITMLoginButton(){DoubleBuffered=true;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;SetStyle(ControlStyles.ResizeRedraw,true);
        hoverTimer.Interval=16;hoverTimer.Tick+=delegate{float elapsed=Math.Min(1f,(float)(hoverClock.Elapsed.TotalMilliseconds/140));float ease=1-(1-elapsed)*(1-elapsed);hoverBlend=blendStart+(blendEnd-blendStart)*ease;Invalidate();if(elapsed>=1)hoverTimer.Stop();};
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;AnimateHover(true);Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;AnimateHover(false);Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);}
    protected override void OnPaint(PaintEventArgs e){
        bool dark=BackColor.GetBrightness()<0.35f;
        Color fill=Primary?(dark?Color.FromArgb(47,82,127):ITMDesign.Blue):(dark?Color.FromArgb(39,45,54):Color.White);
        Color idle=fill;
        if(Enabled&&(hover||hoverBlend>0||pressed))fill=Primary?(pressed?Color.FromArgb(35,65,103):Color.FromArgb(57,98,149)):(dark?Color.FromArgb(49,57,69):Color.FromArgb(246,248,251));
        if(!dark&&Enabled&&(hover||hoverBlend>0||pressed))fill=Primary?(pressed?Color.FromArgb(30,73,125):Color.FromArgb(46,106,177)):Color.FromArgb(244,247,250);
        if(Enabled&&!pressed&&hoverBlend>0)fill=Color.FromArgb((int)(idle.R+(fill.R-idle.R)*hoverBlend),(int)(idle.G+(fill.G-idle.G)*hoverBlend),(int)(idle.B+(fill.B-idle.B)*hoverBlend));
        if(!Enabled)fill=dark?Color.FromArgb(51,62,79):Color.FromArgb(241,245,250);
        e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=ITMDesign.Rounded(new RectangleF(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3)),SquareCorners?0:7*e.Graphics.DpiX/96f)){
            using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
            using(var border=new Pen(Focused?(dark?Color.FromArgb(173,211,255):ITMDesign.Blue):ITMDesign.Divider(dark),Focused?2f:1f))e.Graphics.DrawPath(border,shape);
        }
        Color ink=Enabled?(Primary?Color.White:(dark?Color.FromArgb(236,242,250):Color.FromArgb(35,43,58))):(dark?Color.FromArgb(174,187,207):Color.FromArgb(132,141,155));
        TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-8,-3),ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-4,-4));
    }
    protected override void Dispose(bool disposing){if(disposing)hoverTimer.Dispose();base.Dispose(disposing);}
}

public sealed class ITMPasswordReveal : CheckBox {
    public ITMPasswordReveal(){DoubleBuffered=true;Appearance=Appearance.Button;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Text="";AccessibleName="Afficher le mot de passe";Cursor=Cursors.Hand;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnCheckedChanged(EventArgs e){AccessibleName=Checked?"Masquer le mot de passe":"Afficher le mot de passe";Invalidate();base.OnCheckedChanged(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float q=e.Graphics.DpiX/96f;float w=20*q,h=11*q,x=(Width-w)/2,y=(Height-h)/2;
        using(var p=new Pen(ForeColor,1.5f*q)){
            using(var path=new GraphicsPath()){
                path.AddBezier(x,y+h/2,x+w*.3f,y-h*.2f,x+w*.7f,y-h*.2f,x+w,y+h/2);
                path.AddBezier(x+w,y+h/2,x+w*.7f,y+h*1.2f,x+w*.3f,y+h*1.2f,x,y+h/2);
                e.Graphics.DrawPath(p,path);
            }
            e.Graphics.DrawEllipse(p,x+w/2-2.5f*q,y+h/2-2.5f*q,5*q,5*q);
            if(Checked)e.Graphics.DrawLine(p,x,y+h,x+w,y);
        }
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-3,-3));
    }
}
public sealed class ITMPasswordSurface : Panel {
    public ITMPasswordSurface(){DoubleBuffered=true;BackColor=Color.White;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaintBackground(PaintEventArgs e){
        e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);
        if(Width<8||Height<8)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=ITMDesign.Rounded(new RectangleF(1,1,Width-3,Height-3),8*e.Graphics.DpiX/96f))
        using(var fill=new SolidBrush(BackColor))e.Graphics.FillPath(fill,path);
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Width<8||Height<8)return;
        float q=e.Graphics.DpiX/96f,r=Math.Min(8*q,Math.Min(Width-3,Height-3)/2f);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=ITMDesign.Rounded(new RectangleF(1,1,Width-3,Height-3),r)){
            using(var pen=new Pen(ContainsFocus?(BackColor.GetBrightness()<.35f?Color.FromArgb(173,211,255):ITMDesign.Blue):ITMDesign.Divider(BackColor.GetBrightness()<.35f),q))e.Graphics.DrawPath(pen,path);
            if(ContainsFocus){
                var state=e.Graphics.Save();e.Graphics.SetClip(path);
                using(var pen=new Pen(BackColor.GetBrightness()<.35f?Color.FromArgb(173,211,255):ITMDesign.Blue,2*q))e.Graphics.DrawLine(pen,r+2*q,Height-2*q,Width-r-2*q,Height-2*q);
                e.Graphics.Restore(state);
            }
        }
    }
}

public sealed class ITMLoginForm : Form {
    public TextBox Password {get;private set;}
    public CheckBox Reveal {get;private set;}
    public Label MessageLabel {get;private set;}
    public ITMLoginButton SignIn {get;private set;}
    public ITMLoginButton CancelAction {get;private set;}
    public LinkLabel Recover {get;private set;}
    private PictureBox mark;
    private ITMPasswordSurface passwordFrame;
    public Control PasswordFieldSurface {get{return passwordFrame;}}
    private Label school,title,reasonLabel,passwordLabel,caps,foot;
    private bool arranging,busy;
    private System.Collections.Generic.List<Font> fonts=new System.Collections.Generic.List<Font>();
    private System.Collections.Generic.Dictionary<string,Font> faceCache=new System.Collections.Generic.Dictionary<string,Font>();
    public ITMLoginForm(){
        Text="ITM CD | Connexion";StartPosition=FormStartPosition.CenterScreen;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        BackColor=Color.White;MinimizeBox=false;MaximizeBox=false;ShowInTaskbar=true;AutoScroll=true;
        var work=Screen.PrimaryScreen.WorkingArea;
        ClientSize=new Size(Math.Min(510,work.Width-48),Math.Min(620,work.Height-64));
        MinimumSize=new Size(Math.Min(280,work.Width-32),Math.Min(300,work.Height-32));
        mark=new PictureBox();mark.SizeMode=PictureBoxSizeMode.Zoom;mark.TabStop=false;Controls.Add(mark);
        school=LabelOf("ITM Cœur de Dieu",16,true,ITMDesign.Blue);
        title=LabelOf("Connexion",23,true,Color.FromArgb(23,38,61));
        reasonLabel=LabelOf("Connectez-vous à votre espace de travail.",10,false,Color.FromArgb(101,116,139));
        passwordLabel=LabelOf("Mot de passe",10,true,Color.FromArgb(43,58,80));
        passwordFrame=new ITMPasswordSurface();Controls.Add(passwordFrame);
        Password=new TextBox();Password.BorderStyle=BorderStyle.None;Password.AutoSize=false;Password.Multiline=false;Password.WordWrap=false;Password.BackColor=Color.White;Password.Font=Face(12,false);Password.UseSystemPasswordChar=true;Password.MaxLength=128;Password.TabIndex=0;Password.AccessibleName="Mot de passe";passwordFrame.Controls.Add(Password);
        Password.GotFocus+=delegate{UpdateCaps();passwordFrame.Invalidate();};Password.LostFocus+=delegate{passwordFrame.Invalidate();};Password.KeyUp+=delegate{UpdateCaps();};
        Reveal=new ITMPasswordReveal();Reveal.Text="";Reveal.Font=Face(10,false);Reveal.ForeColor=Color.FromArgb(74,90,112);Reveal.TabIndex=1;passwordFrame.Controls.Add(Reveal);
        Reveal.GotFocus+=delegate{passwordFrame.Invalidate();};Reveal.LostFocus+=delegate{passwordFrame.Invalidate();};
        caps=LabelOf("",9,false,Color.FromArgb(158,94,12));
        MessageLabel=LabelOf("",10,false,Color.FromArgb(173,44,47));MessageLabel.AccessibleName="Message de connexion";
        SignIn=new ITMLoginButton();SignIn.Primary=true;SignIn.SquareCorners=false;SignIn.Text="Se connecter";SignIn.Font=Face(11,true);SignIn.TabIndex=2;Controls.Add(SignIn);
        CancelAction=new ITMLoginButton();CancelAction.SquareCorners=false;CancelAction.Text="Quitter";CancelAction.Font=Face(10,false);CancelAction.TabIndex=4;CancelAction.DialogResult=DialogResult.Cancel;Controls.Add(CancelAction);
        Recover=new LinkLabel();Recover.Text="Mot de passe oublié ?";Recover.LinkColor=ITMDesign.Blue;Recover.ActiveLinkColor=Recover.LinkColor;Recover.VisitedLinkColor=Recover.LinkColor;Recover.LinkBehavior=LinkBehavior.HoverUnderline;Recover.Font=Face(10,false);Recover.TabIndex=3;Recover.TextAlign=ContentAlignment.MiddleCenter;Controls.Add(Recover);
        foot=LabelOf("Accès réservé au secrétariat.",9,false,Color.FromArgb(110,125,145));
        school.TextAlign=ContentAlignment.MiddleCenter;
        title.TextAlign=ContentAlignment.MiddleCenter;
        reasonLabel.TextAlign=ContentAlignment.MiddleCenter;
        foot.TextAlign=ContentAlignment.MiddleCenter;
        passwordLabel.TextAlign=ContentAlignment.MiddleLeft;
        caps.TextAlign=ContentAlignment.MiddleLeft;
        MessageLabel.TextAlign=ContentAlignment.MiddleLeft;
        AcceptButton=SignIn;CancelButton=CancelAction;
        Password.TextChanged+=delegate{MessageLabel.Text="";UpdateSubmit();};UpdateSubmit();
        Shown+=delegate{
            float q;using(var g=CreateGraphics())q=Math.Max(1,g.DpiX/96f);
            var area=Screen.FromControl(this).WorkingArea;
            MinimumSize=new Size(Math.Min((int)(280*q),Math.Max(240,area.Width-32)),Math.Min((int)(300*q),Math.Max(240,area.Height-32)));
            Size=new Size(Math.Min(Width,area.Width-32),Math.Min(Height,area.Height-32));
            Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
            Arrange();Password.Focus();
        };
        MessageLabel.TextChanged+=delegate{Arrange();};caps.TextChanged+=delegate{Arrange();};
        Arrange();
    }
    private void UpdateSubmit(){SignIn.Enabled=!busy&&!String.IsNullOrWhiteSpace(Password.Text);}
    public void SetBusy(bool value){busy=value;Password.Enabled=Reveal.Enabled=Recover.Enabled=!value;UpdateSubmit();UseWaitCursor=false;}
    public void SetInstitution(string name){school.Text=name;Arrange();}
    public void SetBrand(Image logo,string reason){mark.Image=logo;reasonLabel.Text=reason;Arrange();}
    private Font Face(float size,bool bold){
        size=ITMDesign.InterfaceTextSize;
        size=(float)(Math.Round(Math.Max(9,size)*2)/2);string key=size.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+bold;
        Font f;if(faceCache.TryGetValue(key,out f))return f;
        f=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);fonts.Add(f);faceCache[key]=f;return f;
    }
    private int PlaceText(Control c,int left,int y,int width,int minimum){
        int h=Math.Max(minimum,TextRenderer.MeasureText(c.Text,c.Font,new Size(Math.Max(20,width-8),10000),TextFormatFlags.WordBreak).Height+10);
        c.SetBounds(left,y,width,h);return c.Bottom;
    }
    private Label LabelOf(string text,float size,bool bold,Color color){var l=new ITMReadableLabel();l.Text=text;l.Font=Face(size,bold);l.ForeColor=color;l.BackColor=Color.White;Controls.Add(l);return l;}
    private void UpdateCaps(){caps.Text=Control.IsKeyLocked(Keys.CapsLock)?"La touche Verr. Maj est activée.":"";}
    protected override void OnResize(EventArgs e){base.OnResize(e);if(Password!=null)Arrange();}
    private void Arrange(){
        if(Password==null||arranging)return;arranging=true;
        try{
            float q;using(var g=CreateGraphics())q=Math.Max(1,g.DpiX/96f);
            float logical=ClientSize.Width/q,ratio=Math.Min(1f,Math.Max(.82f,logical/510f));
            int pad=(int)(Math.Max(12,Math.Min(32,logical*.06f))*q),gap=(int)(8*q);
            int w=Math.Max(60,Math.Min((int)(430*q),ClientSize.Width-2*pad-SystemInformation.VerticalScrollBarWidth));
            int left=(ClientSize.Width-w)/2+AutoScrollPosition.X,y=AutoScrollPosition.Y+(int)(18*q);
            school.Font=Face(16*ratio,true);title.Font=Face(23*ratio,true);reasonLabel.Font=Face(10*ratio,false);
            passwordLabel.Font=Face(10*ratio,true);Password.Font=Face(12*ratio,false);Reveal.Font=Face(10*ratio,false);
            caps.Font=Face(9*ratio,false);MessageLabel.Font=Face(10*ratio,false);foot.Font=Face(9*ratio,false);
            SignIn.Font=Face(11*ratio,true);CancelAction.Font=Face(10*ratio,false);Recover.Font=Face(10*ratio,false);
            int logo=(int)(40*q);mark.SetBounds(left+(w-logo)/2,y,logo,logo);y=mark.Bottom+gap;
            y=PlaceText(school,left,y,w,(int)(28*q))+gap;
            y=PlaceText(title,left,y,w,(int)(38*q))+gap;
            y=PlaceText(reasonLabel,left,y,w,(int)(32*q))+gap;
            y=PlaceText(passwordLabel,left,y,w,(int)(24*q));
            int inputHeight=Math.Max((int)(46*q),Password.PreferredHeight+(int)(20*q)),eyeWidth=(int)(40*q),inset=(int)(12*q);
            passwordFrame.SetBounds(left,y,w,inputHeight);
            // A single-line TextBox with AutoSize enabled ignores the requested height on some DPI settings.
            // Give it a roomy inner rectangle so the glyphs and caret are never clipped vertically.
            int textHeight=Math.Min(inputHeight-(int)(8*q),Math.Max(Password.PreferredHeight+(int)(4*q),inputHeight-(int)(10*q)));
            Password.SetBounds(inset,Math.Max((int)(3*q),(inputHeight-textHeight)/2),Math.Max(1,w-eyeWidth-inset-(int)(8*q)),Math.Max(1,textHeight));
            Reveal.SetBounds(w-eyeWidth-(int)(4*q),(int)(4*q),eyeWidth,inputHeight-(int)(8*q));
            y=passwordFrame.Bottom+gap;
            if(caps.Text.Length>0)y=PlaceText(caps,left,y,w,(int)(22*q))+gap;else caps.SetBounds(left,y,w,0);
            if(MessageLabel.Text.Length>0)y=PlaceText(MessageLabel,left,y,w,(int)(28*q))+gap;else MessageLabel.SetBounds(left,y,w,0);
            if(w/q<280){y=PlaceText(SignIn,left,y,w,(int)(36*q))+gap;y=PlaceText(CancelAction,left,y,w,(int)(34*q))+gap;}
            else{
                int cancelWidth=(int)(90*q),saveWidth=w-cancelWidth-gap;
                int sy=PlaceText(SignIn,left,y,saveWidth,(int)(36*q));
                y=Math.Max(sy,PlaceText(CancelAction,left+w-cancelWidth,y,cancelWidth,(int)(36*q)))+gap;
            }
            y=PlaceText(Recover,left,y,w,(int)(28*q))+gap;
            y=PlaceText(foot,left,y,w,(int)(24*q));
            // Fit the window to the last visible label, retaining a small bottom margin.
            int contentHeight=y-AutoScrollPosition.Y+(int)(12*q);
            int frameHeight=Height-ClientSize.Height;
            int availableHeight=Math.Max(120,Screen.FromControl(this).WorkingArea.Height-frameHeight-32);
            int fittedHeight=Math.Min(contentHeight,availableHeight);
            if(ClientSize.Height!=fittedHeight)ClientSize=new Size(ClientSize.Width,fittedHeight);
            AutoScrollMinSize=new Size(0,contentHeight);
        }finally{arranging=false;}
    }

    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing&&fonts!=null){foreach(var f in fonts)f.Dispose();fonts.Clear();}}
}

public sealed class ITMQuietSurface : Panel {
    public ITMQuietSurface(){DoubleBuffered=true;BackColor=Color.White;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Width<2||Height<2)return;
        using(var pen=new Pen(ITMDesign.Divider(BackColor.GetBrightness()<.35f)))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
    }

}

// Office-style ribbon: quiet commands, feedback only during interaction.
public sealed class ITMRibbonGroup : Panel {
    public ITMRibbonGroup(){DoubleBuffered=true;BackColor=Color.White;SetStyle(ControlStyles.ResizeRedraw,true);}
    private bool showSeparator=true;
    public bool ShowSeparator {get{return showSeparator;}set{if(showSeparator!=value){showSeparator=value;Invalidate();}}}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(!showSeparator||Width<1||Height<2)return;
        int inset=Math.Min(Height/2,(int)(6*e.Graphics.DpiY/96f));
        using(var p=new Pen(BackColor.GetBrightness()<0.35f?ITMDesign.Divider(true):Color.FromArgb(218,223,230)))e.Graphics.DrawLine(p,Width-1,inset,Width-1,Height-inset);
    }
}
public sealed class ITMRibbonCommand : Button {
    public string Symbol {get;set;}
    public bool Selected {get;set;}
    public bool IconOnly {get;set;}
    private bool hover;
    private float hoverBlend,pressBlend;
    private float hoverFrom,hoverTo,pressFrom,pressTo;
    private readonly Timer motionTimer=new Timer();
    private readonly System.Diagnostics.Stopwatch motionClock=new System.Diagnostics.Stopwatch();
    private static Color Mix(Color a,Color b,float t){t=Math.Max(0f,Math.Min(1f,t));return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));}
    private void Animate(float h,float p){hoverFrom=hoverBlend;hoverTo=h;pressFrom=pressBlend;pressTo=p;motionClock.Restart();motionTimer.Start();}
    public ITMRibbonCommand(){
        DoubleBuffered=true;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;BackColor=Color.White;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint,true);
        motionTimer.Interval=16;motionTimer.Tick+=delegate{float t=Math.Min(1f,(float)(motionClock.Elapsed.TotalMilliseconds/120.0));float ease=1f-(1f-t)*(1f-t);hoverBlend=hoverFrom+(hoverTo-hoverFrom)*ease;pressBlend=pressFrom+(pressTo-pressFrom)*ease;Invalidate();if(t>=1f)motionTimer.Stop();};
    }
    protected override void OnMouseEnter(EventArgs e){hover=true;Animate(1f,pressBlend);base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=false;Animate(0f,0f);base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){Animate(hover?1f:hoverBlend,1f);}base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){bool inside=ClientRectangle.Contains(PointToClient(Cursor.Position));Animate(inside?1f:0f,0f);base.OnMouseUp(e);}
    protected override void OnEnabledChanged(EventArgs e){if(!Enabled){hoverBlend=pressBlend=0f;motionTimer.Stop();}Invalidate();base.OnEnabledChanged(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e){
        bool dark=BackColor.GetBrightness()<0.35f;
        Color idle=BackColor,over=dark?Color.FromArgb(68,86,112):Color.FromArgb(232,240,250),down=dark?Color.FromArgb(57,76,105):Color.FromArgb(211,226,245);
        Color bg=Enabled?Mix(Mix(Selected?over:idle,over,hoverBlend),down,pressBlend):BackColor;
        e.Graphics.Clear(bg);float q=e.Graphics.DpiX/96f;int pad=(int)(6*q),size=Math.Max(1,Math.Min((int)(18*q),Math.Min(Width,Height)-Math.Max(2,(int)(2*q)))),offset=(int)Math.Round(pressBlend*q);
        Color ink=Enabled?(dark?Color.FromArgb(236,242,250):Color.FromArgb(42,57,76)):(dark?Color.FromArgb(193,206,224):Color.FromArgb(151,158,168));
        var r=new Rectangle(pad,offset,Math.Max(1,Width-size-pad*3),Math.Max(1,Height-offset));
        if(!IconOnly)TextRenderer.DrawText(e.Graphics,Text,Font,r,ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl|TextFormatFlags.NoPrefix);
        var state=e.Graphics.Save();e.Graphics.TranslateTransform(IconOnly?(Width-size)/2f:Width-size-pad,(Height-size)/2f+offset);e.Graphics.ScaleTransform(size/24f,size/24f);e.Graphics.SetClip(new RectangleF(0,0,24,24));e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var p=new Pen(Enabled?ITMDesign.SymbolColor(Symbol,dark):ink,1.6f)){
            p.StartCap=p.EndCap=LineCap.Round;p.LineJoin=LineJoin.Round;
            switch(Symbol){
                case "shield":e.Graphics.DrawPolygon(p,new Point[]{new Point(12,2),new Point(21,6),new Point(19,16),new Point(12,22),new Point(5,16),new Point(3,6)});e.Graphics.DrawLines(p,new Point[]{new Point(7,12),new Point(11,16),new Point(17,9)});break;
                case "people":e.Graphics.DrawEllipse(p,8,2,8,8);e.Graphics.DrawArc(p,5,12,14,12,180,180);e.Graphics.DrawArc(p,1,6,6,6,100,260);e.Graphics.DrawArc(p,17,6,6,6,180,260);e.Graphics.DrawLine(p,2,17,2,22);e.Graphics.DrawLine(p,22,17,22,22);break;
                case "history":e.Graphics.DrawEllipse(p,3,3,18,18);e.Graphics.DrawLine(p,12,6,12,12);e.Graphics.DrawLine(p,12,12,17,15);break;
                case "shortcut":e.Graphics.DrawRectangle(p,3,3,18,15);e.Graphics.DrawLine(p,8,22,16,22);e.Graphics.DrawLine(p,12,18,12,22);e.Graphics.DrawLines(p,new Point[]{new Point(8,13),new Point(8,8),new Point(16,8),new Point(13,5)});break;
                case "account":e.Graphics.DrawEllipse(p,8,2,8,8);e.Graphics.DrawArc(p,3,12,18,12,180,180);break;
                case "payment":e.Graphics.DrawRectangle(p,2,5,20,14);e.Graphics.DrawEllipse(p,9,8,6,8);e.Graphics.DrawLine(p,5,11,6,11);e.Graphics.DrawLine(p,18,13,19,13);break;
                case "sponsor":e.Graphics.DrawEllipse(p,3,3,7,7);e.Graphics.DrawEllipse(p,14,3,7,7);e.Graphics.DrawArc(p,1,12,11,12,180,180);e.Graphics.DrawArc(p,12,12,11,12,180,180);e.Graphics.DrawLines(p,new Point[]{new Point(9,17),new Point(12,20),new Point(17,14)});break;
                case "card":e.Graphics.DrawRectangle(p,2,4,20,16);e.Graphics.DrawEllipse(p,5,7,5,5);e.Graphics.DrawArc(p,4,13,7,7,180,180);e.Graphics.DrawLine(p,14,8,19,8);e.Graphics.DrawLine(p,14,12,19,12);e.Graphics.DrawLine(p,14,16,17,16);break;
                case "workbook":e.Graphics.DrawRectangle(p,3,2,18,20);e.Graphics.DrawLine(p,3,8,21,8);e.Graphics.DrawLine(p,3,14,21,14);e.Graphics.DrawLine(p,9,2,9,22);e.Graphics.DrawLine(p,15,8,15,22);break;
                case "theme":e.Graphics.DrawEllipse(p,3,3,18,18);e.Graphics.DrawArc(p,7,3,13,18,90,180);e.Graphics.DrawLine(p,12,3,12,21);break;
                case "settings":e.Graphics.DrawEllipse(p,8,8,8,8);for(int i=0;i<8;i++){double a=i*Math.PI/4;float x1=12+(float)Math.Cos(a)*6,y1=12+(float)Math.Sin(a)*6,x2=12+(float)Math.Cos(a)*10,y2=12+(float)Math.Sin(a)*10;e.Graphics.DrawLine(p,x1,y1,x2,y2);}break;
                case "person-add":e.Graphics.DrawEllipse(p,3,3,7,7);e.Graphics.DrawArc(p,1,12,11,10,180,180);e.Graphics.DrawLine(p,17,7,17,17);e.Graphics.DrawLine(p,12,12,22,12);break;
                case "calendar":e.Graphics.DrawRectangle(p,3,5,18,16);e.Graphics.DrawLine(p,3,10,21,10);e.Graphics.DrawLine(p,7,2,7,7);e.Graphics.DrawLine(p,17,2,17,7);break;
                case "grades":e.Graphics.DrawRectangle(p,4,2,16,20);e.Graphics.DrawLine(p,8,7,16,7);e.Graphics.DrawLine(p,8,12,16,12);e.Graphics.DrawLines(p,new Point[]{new Point(8,17),new Point(11,20),new Point(17,14)});break;
                case "delete":e.Graphics.DrawLine(p,5,6,19,6);e.Graphics.DrawRectangle(p,7,7,10,14);e.Graphics.DrawLine(p,9,3,15,3);break;
                case "search":e.Graphics.DrawEllipse(p,3,3,13,13);e.Graphics.DrawLine(p,14,14,21,21);break;
                case "pdf":case "word":case "csv":e.Graphics.DrawLines(p,new Point[]{new Point(15,2),new Point(4,2),new Point(4,22),new Point(20,22),new Point(20,7),new Point(15,2),new Point(15,7),new Point(20,7)});if(Symbol=="csv"){e.Graphics.DrawRectangle(p,7,11,10,7);e.Graphics.DrawLine(p,7,14.5f,17,14.5f);e.Graphics.DrawLine(p,12,11,12,18);}else if(Symbol=="pdf"){e.Graphics.DrawBezier(p,8,18,17,7,7,5,11,16);e.Graphics.DrawBezier(p,11,16,21,19,17,10,8,18);}else{e.Graphics.DrawLine(p,7,11,17,11);e.Graphics.DrawLine(p,7,14,17,14);e.Graphics.DrawLine(p,7,17,14,17);}break;
                case "print":e.Graphics.DrawRectangle(p,6,3,12,7);e.Graphics.DrawRectangle(p,6,15,12,7);e.Graphics.DrawRectangle(p,3,9,18,9);break;
                case "export":e.Graphics.DrawLine(p,12,3,12,16);e.Graphics.DrawLines(p,new Point[]{new Point(7,11),new Point(12,16),new Point(17,11)});e.Graphics.DrawLines(p,new Point[]{new Point(4,16),new Point(4,21),new Point(20,21),new Point(20,16)});break;
                case "save":e.Graphics.DrawRectangle(p,4,3,16,18);e.Graphics.DrawRectangle(p,8,3,8,6);e.Graphics.DrawRectangle(p,8,14,8,7);break;
                case "restore":e.Graphics.DrawArc(p,3,3,18,18,35,290);e.Graphics.DrawLines(p,new Point[]{new Point(4,3),new Point(4,9),new Point(10,9)});break;
                case "open":e.Graphics.DrawLines(p,new Point[]{new Point(3,19),new Point(3,5),new Point(10,5),new Point(13,8),new Point(21,8)});e.Graphics.DrawPolygon(p,new Point[]{new Point(3,19),new Point(6,11),new Point(22,11),new Point(19,19)});break;
                case "refresh":e.Graphics.DrawArc(p,4,4,16,16,35,290);e.Graphics.DrawLines(p,new Point[]{new Point(20,3),new Point(20,9),new Point(14,9)});break;
                case "close":e.Graphics.DrawLine(p,5,5,19,19);e.Graphics.DrawLine(p,19,5,5,19);break;
                case "help":e.Graphics.DrawEllipse(p,3,3,18,18);e.Graphics.DrawArc(p,8,7,8,7,200,250);e.Graphics.DrawLine(p,12,15,12,16);e.Graphics.DrawEllipse(p,11.5f,18,1,1);break;
                case "lock":e.Graphics.DrawRectangle(p,5,10,14,11);e.Graphics.DrawArc(p,7,3,10,12,180,-180);break;
                case "add":e.Graphics.DrawLine(p,12,4,12,20);e.Graphics.DrawLine(p,4,12,20,12);break;
                case "edit":e.Graphics.DrawPolygon(p,new Point[]{new Point(4,20),new Point(6,14),new Point(16,4),new Point(20,8),new Point(10,18)});break;
                case "next":e.Graphics.DrawLine(p,5,12,18,12);e.Graphics.DrawLines(p,new Point[]{new Point(13,7),new Point(18,12),new Point(13,17)});break;
                case "check":e.Graphics.DrawLines(p,new Point[]{new Point(4,12),new Point(10,18),new Point(20,6)});break;
                case "document":e.Graphics.DrawLines(p,new Point[]{new Point(15,2),new Point(5,2),new Point(5,22),new Point(19,22),new Point(19,6),new Point(15,2)});e.Graphics.DrawLine(p,15,2,15,6);e.Graphics.DrawLine(p,15,6,19,6);break;
                case "merge":e.Graphics.DrawLines(p,new Point[]{new Point(3,5),new Point(8,5),new Point(15,19),new Point(21,19)});e.Graphics.DrawLines(p,new Point[]{new Point(3,19),new Point(8,19),new Point(15,5),new Point(21,5)});break;
                default:e.Graphics.DrawRectangle(p,5,4,14,16);e.Graphics.DrawLine(p,8,9,16,9);e.Graphics.DrawLine(p,8,14,16,14);break;
            }
        }
        e.Graphics.Restore(state);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(3,3,Width-6,Height-6));
    }
    protected override void Dispose(bool disposing){if(disposing){motionTimer.Stop();motionTimer.Dispose();}base.Dispose(disposing);}
}
public static class ITMReadableColors {
    private static double Linear(byte v){double c=v/255.0;return c<=0.04045?c/12.92:Math.Pow((c+0.055)/1.055,2.4);}
    private static double Lum(Color c){return 0.2126*Linear(c.R)+0.7152*Linear(c.G)+0.0722*Linear(c.B);}
    public static double Contrast(Color a,Color b){double x=Lum(a),y=Lum(b);return (Math.Max(x,y)+0.05)/(Math.Min(x,y)+0.05);}
    public static Color Ink(Color preferred,Color background,bool enabled){
        if(background.GetBrightness()>=0.35f)return enabled?preferred:SystemColors.GrayText;
        if(!enabled)return Color.FromArgb(195,205,220);
        if(Contrast(preferred,background)>=4.5)return preferred;
        if(preferred.R>130 && preferred.R>preferred.G*1.3)return Color.FromArgb(255,190,160);
        if(preferred.G>90 && preferred.G>preferred.R*1.3)return Color.FromArgb(120,220,168);
        if(preferred.B>preferred.R*1.3)return Color.FromArgb(173,211,255);
        return Color.FromArgb(232,237,244);
    }
}
public sealed class ITMReadableLabel : Label {
    public ITMReadableLabel(){SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        if(Image!=null){base.OnPaint(e);return;}
        Rectangle r=new Rectangle(Padding.Left,Padding.Top,Math.Max(1,Width-Padding.Horizontal),Math.Max(1,Height-Padding.Vertical));
        TextFormatFlags flags=TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl;
        if(!UseMnemonic)flags|=TextFormatFlags.NoPrefix;
        if(AutoEllipsis)flags|=TextFormatFlags.EndEllipsis;
        switch(TextAlign){
            case ContentAlignment.TopCenter:case ContentAlignment.MiddleCenter:case ContentAlignment.BottomCenter:flags|=TextFormatFlags.HorizontalCenter;break;
            case ContentAlignment.TopRight:case ContentAlignment.MiddleRight:case ContentAlignment.BottomRight:flags|=TextFormatFlags.Right;break;
        }
        if(TextAlign==ContentAlignment.MiddleLeft||TextAlign==ContentAlignment.MiddleCenter||TextAlign==ContentAlignment.MiddleRight)flags|=TextFormatFlags.VerticalCenter;
        if(TextAlign==ContentAlignment.BottomLeft||TextAlign==ContentAlignment.BottomCenter||TextAlign==ContentAlignment.BottomRight)flags|=TextFormatFlags.Bottom;
        TextRenderer.DrawText(e.Graphics,Text,Font,r,ITMReadableColors.Ink(ForeColor,BackColor,Enabled),flags);
    }
}
public sealed class ITMThemeComboBox : ComboBox {
    public ITMThemeComboBox(){DrawMode=DrawMode.OwnerDrawFixed;FitItemHeight();}
    private void FitItemHeight(){if(Font!=null)ItemHeight=Math.Max(18,Font.Height+6);}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);FitItemHeight();}
    protected override void OnDropDown(EventArgs e){
        int width=Width;foreach(object item in Items)width=Math.Max(width,TextRenderer.MeasureText(GetItemText(item),Font).Width+SystemInformation.VerticalScrollBarWidth+16);
        DropDownWidth=Math.Max(1,Math.Min(width,Screen.FromControl(this).WorkingArea.Width-24));base.OnDropDown(e);
    }
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnDrawItem(DrawItemEventArgs e){
        if(e.Bounds.Width<=0||e.Bounds.Height<=0)return;
        bool dark=BackColor.GetBrightness()<0.35f;
        bool selected=Enabled&&(e.State&DrawItemState.Selected)!=0;
        Color bg=selected?(dark?Color.FromArgb(61,87,126):SystemColors.Highlight):BackColor;
        Color ink=selected?(dark?Color.FromArgb(232,237,244):SystemColors.HighlightText):ITMReadableColors.Ink(ForeColor,bg,Enabled);
        using(var brush=new SolidBrush(bg))e.Graphics.FillRectangle(brush,e.Bounds);
        string text=e.Index>=0&&e.Index<Items.Count?GetItemText(Items[e.Index]):Text;
        var rect=Rectangle.Inflate(e.Bounds,-3,0);
        TextRenderer.DrawText(e.Graphics,text,Font,rect,ink,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
        if(Enabled&&(e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
    }
}
public sealed class ITMThemeStripRenderer : ToolStripProfessionalRenderer {
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){using(var brush=new SolidBrush(e.ToolStrip.BackColor))e.Graphics.FillRectangle(brush,e.AffectedBounds);}
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=ITMReadableColors.Ink(e.Item.ForeColor,e.ToolStrip.BackColor,e.Item.Enabled);base.OnRenderItemText(e);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var pen=new Pen(ITMDesign.Divider(true)))e.Graphics.DrawLine(pen,0,0,e.ToolStrip.Width,0);}
}

// Account dialogs share the login form's native fields and keyboard behavior.
public sealed class ITMAccountForm : Form {
    public TextBox Username {get;private set;}
    public TextBox DisplayName {get;private set;}
    public TextBox Password {get;private set;}
    public TextBox Confirmation {get;private set;}
    public Label MessageLabel {get;private set;}
    public ITMLoginButton Submit {get;private set;}
    public ITMLoginButton WindowsAction {get;private set;}
    public ITMLoginButton RegisterAction {get;private set;}
    public ITMLoginButton RecoverAction {get;private set;}
    public ITMLoginButton RolesAction {get;private set;}
    public ITMLoginButton LogoutAction {get;private set;}
    public ITMLoginButton CloseAction {get;private set;}
    private readonly bool registration,recovery,required;
    private bool busy,rolesEnabled,completed;
    private bool arranging,ready;
    private PictureBox logo;
    private Label brand,title,intro,session,hint,foot;
    private Label[] labels;
    private ITMPasswordSurface[] fields;
    private ITMPasswordReveal[] eyes;
    private System.Collections.Generic.List<Font> fonts=new System.Collections.Generic.List<Font>();
    private System.Collections.Generic.Dictionary<string,Font> fontCache=new System.Collections.Generic.Dictionary<string,Font>();
    public ITMAccountForm(bool create,bool recover,bool mandatory) {
        registration=create;recovery=recover;required=mandatory;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        Text="ITM CD | "+(recover?"Récupérer mon compte":create?"Créer mon compte":"Compte");
        BackColor=Color.FromArgb(250,251,253);StartPosition=FormStartPosition.CenterParent;
        MinimizeBox=false;MaximizeBox=false;DoubleBuffered=true;AutoScroll=true;
        ClientSize=new Size(560,create?540:600);MinimumSize=new Size(360,480);
        logo=new PictureBox();logo.SizeMode=PictureBoxSizeMode.Zoom;logo.TabStop=false;Controls.Add(logo);
        brand=TextLabel("ITM Cœur de Dieu",16,true,true);
        title=TextLabel(recover?"Récupérer le compte":create?"Créer votre compte":"Votre compte",23,true,true);
        intro=TextLabel(recover?"Saisissez votre code personnel et un nouveau mot de passe.":create?"Votre espace de travail au secrétariat.":"Choisissez votre session Windows ou votre compte ITM.",10,false,true);
        session=TextLabel("",10,true,true);
        Username=Field("Identifiant",false,0);Username.MaxLength=32;
        DisplayName=Field(recover?"Code personnel":"Nom à afficher",recover,1);DisplayName.MaxLength=80;
        Password=Field("Mot de passe",true,2);
        Confirmation=Field("Confirmer le mot de passe",true,3);
        fields=fieldList.ToArray();labels=labelList.ToArray();eyes=eyeList.ToArray();
        hint=TextLabel("",9,false,false);
        MessageLabel=TextLabel("",10,false,false);MessageLabel.ForeColor=Color.FromArgb(173,44,47);MessageLabel.AccessibleName="Message du compte";
        WindowsAction=Action("Utiliser ma session Windows",false,5);
        Submit=Action(recover?"Réinitialiser mon compte":create?"Créer mon compte":"Se connecter",true,6);
        RegisterAction=Action("Créer un compte",false,7);RecoverAction=Action("Récupérer un compte",false,8);
        RolesAction=Action("Gérer les rôles",false,9);LogoutAction=Action("Déconnecter",false,10);
        CloseAction=Action(mandatory?"Quitter":create?"Annuler":"Fermer",false,11);CloseAction.DialogResult=DialogResult.Cancel;
        foot=TextLabel(create?(recover?"Le nouveau code de récupération remplacera le précédent.":"Premier compte : administrateur. Autres comptes : consultation."):"La session Windows utilise l’identité déjà ouverte sur cet ordinateur.",9,false,true);
        AcceptButton=Submit;CancelButton=CloseAction;
        Username.TextChanged+=delegate{UpdateInput();};Password.TextChanged+=delegate{UpdateInput();};Confirmation.TextChanged+=delegate{UpdateInput();};DisplayName.TextChanged+=delegate{UpdateInput();};
        Password.KeyUp+=delegate{UpdateInput();};Password.GotFocus+=delegate{UpdateInput();};
        MessageLabel.TextChanged+=delegate{Arrange();};
        Shown+=delegate{
            var area=Screen.FromControl(this).WorkingArea;
            Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));
            Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
            Arrange();if(Username.Text.Length==0)Username.Focus();else Password.Focus();
        };
        ready=true;UpdateInput();Arrange();
    }
    private System.Collections.Generic.List<ITMPasswordSurface> fieldList=new System.Collections.Generic.List<ITMPasswordSurface>();
    private System.Collections.Generic.List<Label> labelList=new System.Collections.Generic.List<Label>();
    private System.Collections.Generic.List<ITMPasswordReveal> eyeList=new System.Collections.Generic.List<ITMPasswordReveal>();
    private Font Face(float size,bool bold) {
        size=ITMDesign.InterfaceTextSize;
        size=(float)(Math.Round(Math.Max(9,size)*2)/2);
        string key=size.ToString(System.Globalization.CultureInfo.InvariantCulture)+bold;
        Font font;if(fontCache.TryGetValue(key,out font))return font;
        font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);fontCache[key]=font;fonts.Add(font);return font;
    }
    private Label TextLabel(string text,float size,bool bold,bool center) {
        var l=new ITMReadableLabel();l.Text=text;l.Font=Face(size,bold);l.ForeColor=Color.FromArgb(44,56,76);l.BackColor=BackColor;
        l.TextAlign=center?ContentAlignment.MiddleCenter:ContentAlignment.MiddleLeft;Controls.Add(l);return l;
    }
    private TextBox Field(string label,bool secret,int index) {
        labelList.Add(TextLabel(label,10,true,false));
        var surface=new ITMPasswordSurface();Controls.Add(surface);fieldList.Add(surface);
        var input=new TextBox();input.BorderStyle=BorderStyle.None;input.AutoSize=false;input.Multiline=false;input.WordWrap=false;input.Font=Face(11,false);input.UseSystemPasswordChar=secret;input.MaxLength=128;surface.TabIndex=index;surface.TabStop=false;input.TabIndex=0;input.AccessibleName=label;surface.Controls.Add(input);
        input.GotFocus+=delegate{surface.Invalidate();};input.LostFocus+=delegate{surface.Invalidate();};
        ITMPasswordReveal eye=null;
        if(secret){eye=new ITMPasswordReveal();eye.TabIndex=1;eye.AccessibleDescription=label;eye.BackColor=surface.BackColor;eye.ForeColor=Color.FromArgb(74,90,112);surface.Controls.Add(eye);eye.CheckedChanged+=delegate{input.UseSystemPasswordChar=!eye.Checked;};}
        eyeList.Add(eye);return input;
    }
    private ITMLoginButton Action(string text,bool primary,int index) {
        var b=new ITMLoginButton();b.Text=text;b.Primary=primary;b.Font=Face(10,primary);b.TabIndex=20+index;
        b.BackColor=primary?ITMDesign.Blue:Color.White;b.ForeColor=primary?Color.White:Color.FromArgb(35,43,58);Controls.Add(b);return b;
    }
    public void SetInstitution(string name){brand.Text=name;Arrange();}
    public void SetBrand(Image image,string identity){logo.Image=image;session.Text=identity;Arrange();}
    public void SetBusy(bool busy) {
        UseWaitCursor=false;
        if(busy==this.busy)return;
        this.busy=busy;if(busy){rolesEnabled=RolesAction.Enabled;RolesAction.Enabled=false;}else RolesAction.Enabled=rolesEnabled;
        foreach(var b in new[]{Submit,WindowsAction,RegisterAction,RecoverAction,CloseAction,LogoutAction})b.Enabled=!busy;
        foreach(var f in fields)f.Enabled=!busy;
        if(!busy)UpdateInput();
    }
    public void ShowSavedRecovery(string code) {
        completed=true;SetBusy(false);
        foreach(var f in fields)f.Enabled=false;
        labels[1].Text="Code personnel";DisplayName.Text=code;DisplayName.ReadOnly=true;DisplayName.UseSystemPasswordChar=false;fields[1].Enabled=true;
        if(eyes[1]!=null)eyes[1].Visible=false;
        Submit.Enabled=false;CloseAction.Text="Continuer";CloseAction.DialogResult=DialogResult.OK;AcceptButton=CloseAction;
        hint.Text="";MessageLabel.ForeColor=BackColor.GetBrightness()<.35f?Color.FromArgb(120,220,168):Color.FromArgb(31,112,62);
        MessageLabel.Text="Compte enregistré. Copiez votre code personnel (Ctrl+A puis Ctrl+C) et conservez-le.";
        DisplayName.Focus();DisplayName.SelectAll();Arrange();
    }
    private void UpdateInput() {
        bool validId=System.Text.RegularExpressions.Regex.IsMatch(Username.Text.Trim(),"^[a-zA-Z0-9._-]{3,32}$");
        bool validName=recovery?DisplayName.Text.Trim().Length>0:DisplayName.Text.Trim().Length>=2;
        Submit.Enabled=!busy&&!completed&&validId&&Password.Text.Length>0&&(!registration||(validName&&Password.Text.Length>=8&&Password.Text==Confirmation.Text));
        if(Control.IsKeyLocked(Keys.CapsLock)&&Password.Focused)hint.Text="Verr. Maj est activée.";
        else if(Username.Text.Length>0&&!validId)hint.Text="Identifiant : 3 à 32 lettres, chiffres, points ou tirets.";
        else if(registration&&Password.Text.Length>0)hint.Text=Password.Text.Length<8?"Utilisez au moins 8 caractères.":Confirmation.Text.Length>0&&Password.Text!=Confirmation.Text?"Les deux mots de passe doivent être identiques.":"Évitez un mot de passe déjà utilisé ailleurs.";
        else hint.Text=registration?"Mot de passe : 8 à 128 caractères.":"";
        Arrange();
    }
    private int TextAt(Control c,int x,int y,int width,int min) {
        int h=Math.Max(min,TextRenderer.MeasureText(c.Text,c.Font,new Size(Math.Max(30,width),10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+4);c.SetBounds(x,y,width,h);return c.Bottom;
    }
    private int FieldAt(int i,int x,int y,int width,int height) {
        float q;using(var g=CreateGraphics())q=Math.Max(1,g.DpiX/96f);
        int labelHeight=Math.Max((int)(25*q),TextRenderer.MeasureText(labels[i].Text,labels[i].Font,new Size(Math.Max(1,width),10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+(int)(6*q));
        var input=(TextBox)fields[i].Controls[0];
        height=Math.Max(height,input.PreferredHeight+(int)(18*q));
        labels[i].SetBounds(x,y,width,labelHeight);fields[i].SetBounds(x,y+labelHeight,width,height);
        int eye=eyes[i]!=null?(int)(38*q):0,inset=(int)(12*q);
        int textHeight=Math.Max(1,height-(int)(12*q));
        input.AutoSize=false;input.Multiline=false;input.WordWrap=false;
        input.SetBounds(inset,(height-textHeight)/2,Math.Max(1,width-2*inset-eye),textHeight);
        if(eyes[i]!=null)eyes[i].SetBounds(width-eye-(int)(4*q),(int)(4*q),eye,height-(int)(8*q));
        return fields[i].Bottom;
    }
    private int ActionsAt(Control[] actions,int x,int y,int width,int gap,float q){
        int columns=Math.Max(1,Math.Min(actions.Length,(width+gap)/Math.Max(1,(int)(180*q)+gap)));
        int cell=Math.Max(1,(width-(columns-1)*gap)/columns);
        for(int first=0;first<actions.Length;first+=columns){
            int height=(int)(40*q);
            for(int i=first;i<Math.Min(first+columns,actions.Length);i++)
                height=Math.Max(height,TextRenderer.MeasureText(actions[i].Text,actions[i].Font,new Size(Math.Max(1,cell-16),10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+(int)(16*q));
            for(int i=first;i<Math.Min(first+columns,actions.Length);i++)actions[i].SetBounds(x+(i-first)*(cell+gap),y,cell,height);
            y+=height+gap;
        }
        return y;
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);if(fields!=null)Arrange();}
    protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);if(fields!=null&&!arranging)Arrange();}
    private void Arrange() {
        if(!ready||fields==null||arranging)return;arranging=true;SuspendLayout();
        int scrollY=-AutoScrollPosition.Y;
        try{
            AutoScrollPosition=Point.Empty;
            float q;using(var g=CreateGraphics())q=Math.Max(1,g.DpiX/96f);
            int margin=(int)(20*q),gap=(int)(12*q);
            int w=Math.Max(40,Math.Min((int)(620*q),ClientSize.Width-2*margin-SystemInformation.VerticalScrollBarWidth));
            int x=Math.Max(margin,(ClientSize.Width-w-SystemInformation.VerticalScrollBarWidth)/2),fh=(int)(44*q);
            int logoSize=(int)(40*q);logo.SetBounds(x+(w-logoSize)/2,gap,logoSize,logoSize);
            int y=TextAt(brand,x,logo.Bottom+gap,w,(int)(26*q));
            y=TextAt(title,x,y+4,w,(int)(30*q));y=TextAt(intro,x,y+4,w,(int)(26*q))+gap;
            foreach(int i in new[]{0,1,2,3}){bool show=registration||i==0||i==2;fields[i].Visible=labels[i].Visible=show;}
            session.Visible=!registration;WindowsAction.Visible=!registration;
            RegisterAction.Visible=RecoverAction.Visible=RolesAction.Visible=!registration;
            LogoutAction.Visible=!registration&&!required;
            if(registration){
                if(w/q>=480){
                    int fw=(w-gap)/2;
                    int leftBottom=FieldAt(0,x,y,fw,fh),rightBottom=FieldAt(1,x+fw+gap,y,fw,fh);
                    y=Math.Max(leftBottom,rightBottom)+gap;
                }else{y=FieldAt(0,x,y,w,fh)+gap;y=FieldAt(1,x,y,w,fh)+gap;}
                y=FieldAt(2,x,y,w,fh)+gap;y=FieldAt(3,x,y,w,fh)+gap;
            }else{
                y=TextAt(session,x,y,w,(int)(28*q))+gap;
                y=ActionsAt(new Control[]{WindowsAction},x,y,w,gap,q);
                y=FieldAt(0,x,y,w,fh)+gap;y=FieldAt(2,x,y,w,fh)+gap;
            }
            hint.Visible=hint.Text.Length>0;if(hint.Visible)y=TextAt(hint,x,y,w,(int)(24*q))+gap;
            MessageLabel.Visible=MessageLabel.Text.Length>0;if(MessageLabel.Visible)y=TextAt(MessageLabel,x,y,w,(int)(26*q))+gap;
            if(registration){y=ActionsAt(new Control[]{Submit,CloseAction},x,y,w,gap,q);}
            else{
                y=ActionsAt(new Control[]{Submit},x,y,w,gap,q);
                y=ActionsAt(new Control[]{RegisterAction,RecoverAction},x,y,w,gap,q);
                y=ActionsAt(required?new Control[]{RolesAction,CloseAction}:new Control[]{RolesAction,LogoutAction,CloseAction},x,y,w,gap,q);
            }
            TextAt(foot,x,y,w,(int)(24*q));
            AutoScrollMinSize=new Size(0,foot.Bottom+margin);
            AutoScrollPosition=new Point(0,scrollY);
        }finally{ResumeLayout(true);arranging=false;}
    }
    protected override void OnFormClosing(FormClosingEventArgs e){if(busy&&DialogResult!=DialogResult.OK)e.Cancel=true;base.OnFormClosing(e);}
    protected override void Dispose(bool disposing) {
        base.Dispose(disposing);if(disposing){foreach(var font in fonts)font.Dispose();fonts.Clear();fontCache.Clear();}
    }
}


public sealed class ITMTopicList : ListBox {
    private bool reflow;
    public ITMTopicList(){DrawMode=DrawMode.OwnerDrawVariable;BorderStyle=BorderStyle.None;IntegralHeight=false;HorizontalScrollbar=false;DoubleBuffered=true;}
    protected override void OnMeasureItem(MeasureItemEventArgs e){
        if(e.Index<0||e.Index>=Items.Count)return;
        int width=Math.Max(30,ClientSize.Width-42);
        e.ItemHeight=Math.Max(Font.Height+18,TextRenderer.MeasureText(Convert.ToString(Items[e.Index]),Font,new Size(width,10000),TextFormatFlags.WordBreak).Height+16);
    }
    protected override void OnDrawItem(DrawItemEventArgs e){
        if(e.Index<0||e.Index>=Items.Count)return;bool dark=BackColor.GetBrightness()<.35f;bool selected=(e.State&DrawItemState.Selected)!=0;
        Color bg=selected?(dark?Color.FromArgb(54,79,112):Color.FromArgb(225,237,255)):BackColor;
        using(var fill=new SolidBrush(bg))e.Graphics.FillRectangle(fill,e.Bounds);
        Color ink=dark?Color.FromArgb(236,242,250):Color.FromArgb(35,46,64);
        using(var p=new Pen(dark?Color.FromArgb(173,211,255):ITMDesign.Blue,1.5f)){
            int x=e.Bounds.Left+12,y=e.Bounds.Top+(e.Bounds.Height-12)/2;e.Graphics.DrawRectangle(p,x,y,10,12);e.Graphics.DrawLine(p,x+3,y+4,x+7,y+4);e.Graphics.DrawLine(p,x+3,y+8,x+7,y+8);
        }
        TextRenderer.DrawText(e.Graphics,Convert.ToString(Items[e.Index]),Font,new Rectangle(e.Bounds.Left+34,e.Bounds.Top+8,Math.Max(1,e.Bounds.Width-40),e.Bounds.Height-16),ink,TextFormatFlags.WordBreak|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);
        if((e.State&DrawItemState.Focus)!=0)e.DrawFocusRectangle();
    }
    protected override void OnSizeChanged(EventArgs e){
        base.OnSizeChanged(e);if(!IsHandleCreated||IsDisposed||Disposing||reflow)return;
        reflow=true;BeginUpdate();int selected=SelectedIndex,top=TopIndex;
        try{RefreshItems();if(selected>=0&&selected<Items.Count)SelectedIndex=selected;if(top>=0&&top<Items.Count)TopIndex=top;}
        finally{EndUpdate();reflow=false;}
    }
}

public sealed class ITMReadableCheckBox : CheckBox {
    public override Size GetPreferredSize(Size proposedSize){var size=base.GetPreferredSize(proposedSize);return new Size(size.Width+8,size.Height);}
    public ITMReadableCheckBox(){FlatStyle=FlatStyle.Flat;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnCheckedChanged(EventArgs e){base.OnCheckedChanged(e);Invalidate();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        if(Appearance==Appearance.Button||Image!=null){base.OnPaint(e);return;}
        float q=e.Graphics.DpiX/96f;int size=(int)(14*q),gap=(int)(6*q);
        bool right=CheckAlign==ContentAlignment.TopRight||CheckAlign==ContentAlignment.MiddleRight||CheckAlign==ContentAlignment.BottomRight||RightToLeft==RightToLeft.Yes;
        int x=right?Width-Padding.Right-size:Padding.Left,y=Padding.Top+Math.Max(0,(Height-Padding.Vertical-size)/2);
        Color surface=BackColor;Control parent=Parent;while(surface.A<255&&parent!=null){surface=parent.BackColor;parent=parent.Parent;}if(surface.A<255)surface=SystemColors.Control;
        e.Graphics.Clear(surface);var glyph=new Rectangle(x,y,size,size);bool dark=surface.GetBrightness()<.35f;
        if(dark){
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=ITMDesign.Rounded(glyph,3*q)){
                using(var b=new SolidBrush(Checked&&Enabled?Color.FromArgb(47,83,131):Color.FromArgb(51,64,84)))e.Graphics.FillPath(b,path);
                using(var pen=new Pen(Color.FromArgb(173,193,218),q))e.Graphics.DrawPath(pen,path);
            }
            using(var pen=new Pen(ITMReadableColors.Ink(ForeColor,surface,Enabled),1.7f*q)){
                pen.StartCap=pen.EndCap=LineCap.Round;
                if(CheckState==CheckState.Checked)e.Graphics.DrawLines(pen,new PointF[]{new PointF(x+3*q,y+7*q),new PointF(x+6*q,y+10*q),new PointF(x+11*q,y+4*q)});
                else if(CheckState==CheckState.Indeterminate)e.Graphics.DrawLine(pen,x+3*q,y+7*q,x+11*q,y+7*q);
            }
        }else{ButtonState state=Enabled?ButtonState.Normal:ButtonState.Inactive;if(CheckState!=CheckState.Unchecked)state|=ButtonState.Checked;ControlPaint.DrawCheckBox(e.Graphics,glyph,state);}
        var text=new Rectangle(right?Padding.Left:x+size+gap,Padding.Top,Math.Max(1,Width-Padding.Horizontal-size-gap),Math.Max(1,Height-Padding.Vertical));
        TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.WordBreak|TextFormatFlags.TextBoxControl;
        if(!UseMnemonic)flags|=TextFormatFlags.NoPrefix;
        if(right)flags|=TextFormatFlags.Right;
        TextRenderer.DrawText(e.Graphics,Text,Font,text,ITMReadableColors.Ink(ForeColor,surface,Enabled),flags);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,text);
    }
}

public static class ITMBrandIcon {
    public static void Write(string imagePath,string path){
        int[] sizes={16,24,32,48,64,128};var images=new System.Collections.Generic.List<byte[]>();
        using(var source=Image.FromFile(imagePath))foreach(int size in sizes){
            using(var bitmap=new Bitmap(size,size)){
                using(var g=Graphics.FromImage(bitmap)){g.Clear(Color.Transparent);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;float scale=Math.Min((float)size/source.Width,(float)size/source.Height);float w=source.Width*scale,h=source.Height*scale;g.DrawImage(source,(size-w)/2,(size-h)/2,w,h);}
                using(var data=new System.IO.MemoryStream())using(var writer=new System.IO.BinaryWriter(data)){
                    int maskStride=((size+31)/32)*4;byte[] mask=new byte[maskStride*size];
                    writer.Write(40);writer.Write(size);writer.Write(size*2);writer.Write((short)1);writer.Write((short)32);writer.Write(0);writer.Write(size*size*4+mask.Length);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);
                    for(int y=size-1;y>=0;y--)for(int x=0;x<size;x++){Color c=bitmap.GetPixel(x,y);writer.Write(c.B);writer.Write(c.G);writer.Write(c.R);writer.Write(c.A);if(c.A<128)mask[(size-1-y)*maskStride+x/8]|=(byte)(0x80>>(x%8));}
                    writer.Write(mask);writer.Flush();images.Add(data.ToArray());
                }
            }
        }
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=new System.IO.BinaryWriter(System.IO.File.Create(temp))){
            file.Write((short)0);file.Write((short)1);file.Write((short)sizes.Length);int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++){file.Write((byte)sizes[i]);file.Write((byte)sizes[i]);file.Write((byte)0);file.Write((byte)0);file.Write((short)1);file.Write((short)32);file.Write(images[i].Length);file.Write(offset);offset+=images[i].Length;}
            foreach(byte[] image in images)file.Write(image);
        }if(System.IO.File.Exists(path))System.IO.File.Replace(temp,path,null);else System.IO.File.Move(temp,path);}finally{if(System.IO.File.Exists(temp))System.IO.File.Delete(temp);}
    }
}

public sealed class ScoliaPasswordBox : UserControl {
 private TextBox input;private ITMPasswordSurface surface;private ITMPasswordReveal eye;
 public ScoliaPasswordBox(){
  Height=46;MinimumSize=new Size(120,46);Margin=new Padding(0,5,0,10);TabStop=false;
  surface=new ITMPasswordSurface();surface.Dock=DockStyle.Fill;surface.Padding=new Padding(13,7,7,7);surface.TabStop=false;Controls.Add(surface);
  input=new TextBox();input.BorderStyle=BorderStyle.None;input.UseSystemPasswordChar=true;input.Dock=DockStyle.Fill;input.Font=new Font("Segoe UI",10.5f,FontStyle.Regular);input.Margin=Padding.Empty;input.ShortcutsEnabled=true;input.AccessibleName="Mot de passe";surface.Controls.Add(input);
  eye=new ITMPasswordReveal();eye.Dock=DockStyle.Right;eye.Width=38;eye.Margin=Padding.Empty;eye.TabStop=true;eye.AccessibleRole=AccessibleRole.CheckButton;eye.AccessibleDescription="Afficher ou masquer le mot de passe";surface.Controls.Add(eye);eye.BringToFront();
  eye.CheckedChanged+=delegate{input.UseSystemPasswordChar=!eye.Checked;eye.AccessibleName=eye.Checked?"Masquer le mot de passe":"Afficher le mot de passe";input.Focus();};
  BackColorChanged+=delegate{surface.BackColor=BackColor;input.BackColor=BackColor;eye.BackColor=BackColor;};
  ForeColorChanged+=delegate{input.ForeColor=ForeColor;eye.ForeColor=ForeColor;};
  FontChanged+=delegate{input.Font=Font;};EnabledChanged+=delegate{input.Enabled=Enabled;eye.Enabled=Enabled;surface.Invalidate();};
  input.TextChanged+=delegate{OnTextChanged(EventArgs.Empty);};
  input.GotFocus+=delegate{surface.Invalidate();};input.LostFocus+=delegate{surface.Invalidate();};eye.GotFocus+=delegate{surface.Invalidate();};eye.LostFocus+=delegate{surface.Invalidate();};
 }
 public override string Text{get{return input==null?base.Text:input.Text;}set{if(input==null)base.Text=value;else input.Text=value;}}
 public TextBox InputControl{get{return input;}}
 public void Clear(){input.Clear();eye.Checked=false;}
 public new bool Focus(){return input.Focus();}
}

public sealed class ScoliaBusySpinner : Control {
    private readonly Timer timer;
    private float angle;
    public ScoliaBusySpinner(){
        DoubleBuffered=true;
        Size=new Size(24,24);
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint|ControlStyles.SupportsTransparentBackColor,true);
        BackColor=Color.Transparent;
        AccessibleRole=AccessibleRole.Animation;
        AccessibleName="Traitement en cours";
        timer=new Timer();
        timer.Interval=32;
        timer.Tick+=(s,e)=>{angle=(angle+10.5f)%360f;Invalidate();};
    }
    public void Start(){angle=0;timer.Start();Invalidate();}
    public void Stop(){timer.Stop();Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);
        float q=e.Graphics.DpiX/96f;
        bool dark=Parent!=null&&Parent.BackColor.GetBrightness()<.36f;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float diameter=Math.Max(4,Math.Min(Width,Height)-5*q);
        float x=(Width-diameter)/2f,y=(Height-diameter)/2f;
        using(var pen=new Pen(dark?Color.FromArgb(69,79,94):Color.FromArgb(229,235,243),2.2f*q))e.Graphics.DrawEllipse(pen,x,y,diameter,diameter);
        using(var pen=new Pen(dark?Color.FromArgb(166,198,237):ITMDesign.Blue,2.2f*q)){
            pen.StartCap=pen.EndCap=LineCap.Round;e.Graphics.DrawArc(pen,x,y,diameter,diameter,angle-90,245);
        }
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}

// Barre de progression sobre utilisée par l'installateur SCOLIA.
// Elle reste déterminée par la vraie progression (0-100) et n'invente aucune étape.
public sealed class ScoliaProgressBar : Control {
    private readonly Timer timer;
    private int value;
    private int maximum=100;
    private float shimmer;
    private bool running;
    public ScoliaProgressBar(){
        DoubleBuffered=true;
        Height=12;
        MinimumSize=new Size(80,8);
        SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.UserPaint|ControlStyles.SupportsTransparentBackColor,true);
        BackColor=Color.Transparent;
        AccessibleRole=AccessibleRole.ProgressBar;
        AccessibleName="Progression du traitement";
        timer=new Timer();timer.Interval=48;timer.Tick+=(s,e)=>{shimmer=(shimmer+0.035f)%1f;Invalidate();};
    }
    public int Maximum {get{return maximum;} set{maximum=Math.Max(1,value);if(this.value>maximum)this.value=maximum;Invalidate();}}
    public int Value {get{return value;} set{this.value=Math.Max(0,Math.Min(maximum,value));AccessibleDescription=this.value+" %";Invalidate();}}
    public void Start(){running=true;timer.Start();Invalidate();}
    public void Stop(){running=false;timer.Stop();Invalidate();}
    private static GraphicsPath Rounded(RectangleF r,float radius){return ITMDesign.Rounded(r,radius);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        float q=e.Graphics.DpiX/96f;
        Color parentColor=(Parent!=null&&!Parent.IsDisposed)?Parent.BackColor:SystemColors.Control;
        bool dark=parentColor.GetBrightness()<.36f;
        Color track=dark?Color.FromArgb(62,74,94):Color.FromArgb(226,231,239);
        Color accent=dark?Color.FromArgb(166,198,237):ITMDesign.Blue;
        var trackRect=new RectangleF(0,Math.Max(0,(Height-Math.Max(7*q,Height*.52f))/2f),Width,Math.Max(7*q,Height*.52f));
        using(var path=Rounded(trackRect,trackRect.Height/2f))using(var brush=new SolidBrush(track))e.Graphics.FillPath(brush,path);
        float fraction=maximum<=0?0:(float)value/maximum;
        float fillWidth=Math.Max(0,trackRect.Width*fraction);
        if(running && value==0){
            float segment=Math.Min(Math.Max(42*q,trackRect.Width*.16f),Math.Max(42*q,trackRect.Width*.32f));
            float start=(trackRect.Width+segment)*shimmer-segment;
            var moving=new RectangleF(start,trackRect.Y,segment,trackRect.Height);
            var state=e.Graphics.Save();
            using(var clipPath=Rounded(trackRect,trackRect.Height/2f))e.Graphics.SetClip(clipPath);
            using(var movePath=Rounded(moving,trackRect.Height/2f))using(var moveBrush=new SolidBrush(accent))e.Graphics.FillPath(moveBrush,movePath);
            e.Graphics.Restore(state);
        }
        if(fillWidth>0.5f){
            var fillRect=new RectangleF(trackRect.X,trackRect.Y,fillWidth,trackRect.Height);
            using(var path=Rounded(fillRect,Math.Min(fillRect.Height/2f,fillWidth/2f)))using(var brush=new SolidBrush(accent))e.Graphics.FillPath(brush,path);
            if(running && fillWidth>28*q){
                float band=Math.Min(54*q,fillWidth*.38f);
                float start=(fillWidth+band)*shimmer-band;
                var clip=e.Graphics.Save();
                using(var path=Rounded(fillRect,Math.Min(fillRect.Height/2f,fillWidth/2f)))e.Graphics.SetClip(path);
                using(var brush=new LinearGradientBrush(new RectangleF(start,trackRect.Y,band,trackRect.Height),Color.FromArgb(0,255,255,255),Color.FromArgb(dark?65:92,255,255,255),0f))e.Graphics.FillRectangle(brush,start,trackRect.Y,band,trackRect.Height);
                e.Graphics.Restore(clip);
            }
        }
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}

// Retour immédiat sur la connexion, puis relais par la transition animée indépendante.
public sealed class ScoliaConnectionForm : Form {
    public ITMThemeComboBox Account { get; private set; }
    public ScoliaPasswordBox Password { get; private set; }
    public ITMReadableLabel MessageLabel { get; private set; }
    public ITMLoginButton Submit { get; private set; }
    public ScoliaBusySpinner BusySpinner { get; private set; }
    private ITMReadableLabel busyLabel;
    private Panel body;
    private PictureBox logo;
    private ITMReadableLabel brand,intro,school,accountLabel,passwordLabel;
    private ITMFieldSurface accountSurface;
    private bool ready,arranging,busy,passwordRequired=true;
    private readonly Font headingFont=new Font("Segoe UI",22,FontStyle.Bold);
    private readonly Font actionFont=new Font("Segoe UI",11,FontStyle.Bold);
    public ScoliaConnectionForm() {
        Text="SCOLIA | Connexion";AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);
        StartPosition=FormStartPosition.CenterScreen;MaximizeBox=false;MinimizeBox=true;
        BackColor=Color.FromArgb(246,247,249);Font=new Font("Segoe UI",10);
        ClientSize=new Size(460,440);MinimumSize=new Size(340,360);
        body=new Panel();body.Dock=DockStyle.Fill;body.AutoScroll=true;Controls.Add(body);
        logo=new PictureBox();logo.SizeMode=PictureBoxSizeMode.Zoom;logo.TabStop=false;body.Controls.Add(logo);
        brand=Label("SCOLIA");brand.Font=headingFont;
        intro=Label("Connectez-vous à votre espace de travail.");school=Label("");
        accountLabel=Label("Compte");accountLabel.Tag="scolia:field-label";
        Account=new ITMThemeComboBox();Account.DropDownStyle=ComboBoxStyle.DropDownList;Account.AccessibleName="Compte";Account.TabIndex=0;
        accountSurface=new ITMFieldSurface(Account);accountSurface.TabIndex=0;body.Controls.Add(accountSurface);
        passwordLabel=Label("Mot de passe");passwordLabel.Tag="scolia:field-label";
        Password=new ScoliaPasswordBox();Password.AccessibleName="Mot de passe";Password.TabIndex=1;body.Controls.Add(Password);
        MessageLabel=Label("");MessageLabel.Tag="scolia:feedback:error";MessageLabel.AccessibleRole=AccessibleRole.Alert;
        MessageLabel.TextChanged+=delegate{Arrange();};
        Submit=new ITMLoginButton();Submit.Text="Se connecter";Submit.Primary=true;Submit.TabIndex=2;Submit.Font=actionFont;body.Controls.Add(Submit);AcceptButton=Submit;
        BusySpinner=new ScoliaBusySpinner();BusySpinner.Visible=false;BusySpinner.TabStop=false;body.Controls.Add(BusySpinner);
        busyLabel=Label("Vérification de votre compte…");busyLabel.Visible=false;busyLabel.AccessibleRole=AccessibleRole.StatusBar;
        body.Layout+=delegate{Arrange();};
        Account.SelectedIndexChanged+=delegate{MessageLabel.Text="";UpdateSubmit();};Password.TextChanged+=delegate{MessageLabel.Text="";UpdateSubmit();};
        Shown+=delegate{Rectangle area=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,area.Width-24),Math.Min(Height,area.Height-24));Arrange();Account.Focus();};
        ready=true;UpdateSubmit();Arrange();
    }
    private ITMReadableLabel Label(string text){var label=new ITMReadableLabel();label.Text=text;label.Font=Font;body.Controls.Add(label);return label;}
    public void Configure(Image image,string institution){logo.Image=image;school.Text=institution;Arrange();}
    private void UpdateSubmit(){Submit.Enabled=!busy&&Account.SelectedIndex>=0&&(!passwordRequired||!String.IsNullOrWhiteSpace(Password.Text));}
    public void RequirePassword(bool value){passwordRequired=value;Password.Clear();UpdateSubmit();Arrange();}
    public void SetBusy(bool value){
        if(IsDisposed||Disposing)return;
        busy=value;Account.Enabled=Password.Enabled=!value;
        Submit.Text=value?"Connexion en cours…":"Se connecter";
        BusySpinner.Visible=busyLabel.Visible=value;
        if(value)BusySpinner.Start();else BusySpinner.Stop();
        UpdateSubmit();UseWaitCursor=false;Cursor=Cursors.Default;Arrange();
        // Refresh peint immédiatement sans réentrance des événements de connexion.
        if(value){body.ScrollControlIntoView(Submit);Refresh();}
    }
    protected override void OnFormClosing(FormClosingEventArgs e){
        if(busy&&e.CloseReason==CloseReason.UserClosing&&DialogResult!=DialogResult.OK)e.Cancel=true;
        base.OnFormClosing(e);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);Arrange();}
    private int PlaceText(Control c,int x,int y,int w,int minimum){int height=Math.Max(minimum,TextRenderer.MeasureText(c.Text,c.Font,new Size(w,10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+4);c.SetBounds(x,y,w,height);return c.Bottom;}
    private void Arrange(){
        if(!ready||arranging||IsDisposed)return;arranging=true;body.SuspendLayout();
        try {
            int scroll=-body.AutoScrollPosition.Y;body.AutoScrollPosition=Point.Empty;
            float q;using(var g=CreateGraphics())q=g.DpiX/96f;
            int pad=(int)(20*q),w=Math.Max(40,body.ClientSize.Width-2*pad-SystemInformation.VerticalScrollBarWidth),x=pad;
            int y=(int)(28*q),mark=(int)(40*q),gap=(int)(16*q),fh=Math.Max((int)(44*q),Account.PreferredHeight+(int)(14*q));
            logo.SetBounds(x,y,mark,mark);brand.SetBounds(x+mark+(int)(12*q),y,w-mark-(int)(12*q),mark);
            y=PlaceText(school,x,y+mark+(int)(12*q),w,(int)(24*q));
            y=PlaceText(intro,x,y+(int)(4*q),w,(int)(24*q))+gap;
            y=PlaceText(accountLabel,x,y,w,(int)(24*q));accountSurface.SetBounds(x,y+(int)(4*q),w,fh);y=accountSurface.Bottom+gap;
            Password.Visible=passwordLabel.Visible=passwordRequired;
            if(passwordRequired){y=PlaceText(passwordLabel,x,y,w,(int)(24*q));Password.SetBounds(x,y+(int)(4*q),w,fh);y=Password.Bottom+gap;}
            MessageLabel.Visible=MessageLabel.Text.Length>0;
            if(MessageLabel.Visible)y=PlaceText(MessageLabel,x,y,w,(int)(24*q))+gap;
            if(busy){
                int wheel=(int)(24*q);BusySpinner.SetBounds(x,y,wheel,wheel);
                y=PlaceText(busyLabel,x+wheel+(int)(10*q),y,w-wheel-(int)(10*q),wheel)+gap;
            }
            PlaceText(Submit,x,y,w,Math.Max((int)(46*q),TextRenderer.MeasureText(Submit.Text,Submit.Font,new Size(Math.Max(1,w-16),10000),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix).Height+(int)(16*q)));body.AutoScrollMinSize=new Size(0,Submit.Bottom+pad);body.AutoScrollPosition=new Point(0,scroll);
        }finally{body.ResumeLayout();arranging=false;}
    }
    protected override void Dispose(bool disposing){if(disposing){if(logo.Image!=null){logo.Image.Dispose();logo.Image=null;}headingFont.Dispose();actionFont.Dispose();}base.Dispose(disposing);}
}

public sealed class ITMSearchFields : TableLayoutPanel {
    public Label SearchLabel {get;private set;}
    public Label KindLabel {get;private set;}
    public ITMFieldSurface SearchSurface {get;private set;}
    public ITMFieldSurface KindSurface {get;private set;}
    private bool arranging;
    private readonly Font labelFont=new Font("Segoe UI",10,FontStyle.Bold);
    public ITMSearchFields(TextBox search,ComboBox kind){
        AutoSize=false;Padding=Padding.Empty;Margin=Padding.Empty;Size=new Size(600,76);
        SearchLabel=new ITMReadableLabel(){Text="Nom, matricule ou référence",Font=labelFont,Dock=DockStyle.Top};
        KindLabel=new ITMReadableLabel(){Text="Rechercher dans",Font=labelFont,Dock=DockStyle.Top};
        SearchSurface=new ITMFieldSurface(search){Dock=DockStyle.Top};KindSurface=new ITMFieldSurface(kind){Dock=DockStyle.Top};
        Controls.Add(SearchLabel);Controls.Add(KindLabel);Controls.Add(SearchSurface);Controls.Add(KindSurface);
        SearchLabel.FontChanged+=delegate{ArrangeFields();};KindLabel.FontChanged+=delegate{ArrangeFields();};
        SearchLabel.TextChanged+=delegate{ArrangeFields();};KindLabel.TextChanged+=delegate{ArrangeFields();};
        search.FontChanged+=delegate{ArrangeFields();};kind.FontChanged+=delegate{ArrangeFields();};
        ArrangeFields();
    }
    public void ArrangeFields(){
        if(arranging||SearchSurface==null||IsDisposed)return;arranging=true;SuspendLayout();
        try{
            float q=1;using(var graphics=CreateGraphics())q=Math.Max(1,graphics.DpiX/96f);
            bool narrow=Width/q<580;int labelHeight=Math.Max(Math.Max(SearchLabel.Font.Height,KindLabel.Font.Height)+6,(int)(25*q)),fieldHeight=Math.Max((int)(42*q),Math.Max(SearchSurface.Editor.PreferredSize.Height,KindSurface.Editor.PreferredSize.Height)+(int)(14*q));
            int searchWidth=Math.Max(20,narrow?Width:(int)(Width*.72)-(int)(12*q));
            int kindWidth=Math.Max(20,narrow?Width:(int)(Width*.28));
            var flags=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.TextBoxControl;
            labelHeight=Math.Max(labelHeight,Math.Max(TextRenderer.MeasureText(SearchLabel.Text,SearchLabel.Font,new Size(searchWidth,10000),flags).Height,TextRenderer.MeasureText(KindLabel.Text,KindLabel.Font,new Size(kindWidth,10000),flags).Height)+(int)(10*q));
            ColumnStyles.Clear();RowStyles.Clear();ColumnCount=narrow?1:2;RowCount=narrow?4:2;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent,narrow?100:72));if(!narrow)ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));
            RowStyles.Add(new RowStyle(SizeType.Absolute,labelHeight));RowStyles.Add(new RowStyle(SizeType.Absolute,fieldHeight+(narrow?(int)(10*q):0)));
            if(narrow){RowStyles.Add(new RowStyle(SizeType.Absolute,labelHeight));RowStyles.Add(new RowStyle(SizeType.Absolute,fieldHeight));}
            SetCellPosition(SearchLabel,new TableLayoutPanelCellPosition(0,0));SetCellPosition(SearchSurface,new TableLayoutPanelCellPosition(0,1));
            SetCellPosition(KindLabel,new TableLayoutPanelCellPosition(narrow?0:1,narrow?2:0));SetCellPosition(KindSurface,new TableLayoutPanelCellPosition(narrow?0:1,narrow?3:1));
            SearchLabel.Height=KindLabel.Height=labelHeight-(int)(4*q);SearchLabel.Margin=KindLabel.Margin=new Padding(0,0,0,(int)(4*q));
            SearchSurface.Margin=new Padding(0,0,narrow?0:(int)(12*q),narrow?(int)(10*q):0);KindSurface.Margin=Padding.Empty;
            SearchSurface.Height=KindSurface.Height=fieldHeight;
            SearchSurface.Padding=KindSurface.Padding=new Padding((int)(11*q),(int)(6*q),(int)(11*q),(int)(6*q));
            Height=(narrow?2:1)*(labelHeight+fieldHeight)+(narrow?(int)(10*q):0);
        }finally{ResumeLayout(true);arranging=false;}
    }
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);ArrangeFields();}
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)labelFont.Dispose();}
}
