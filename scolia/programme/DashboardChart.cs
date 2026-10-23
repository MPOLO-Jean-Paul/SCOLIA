using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Drawing.Drawing2D;

// Small local chart: no Excel calls during painting or resizing.
public sealed class ITMDashboardChart : Control {
    private string[] labels = new string[0];
    private double[] values = new double[0];
    public Color Accent = Color.FromArgb(0,102,204);
    public ITMDashboardChart() {
        DoubleBuffered = true; BackColor = Color.White;
        SetStyle(ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.Chart;
    }
    public void SetData(string[] names, double[] amounts) {
        labels = names == null ? new string[0] : (string[])names.Clone();
        values = amounts == null ? new double[0] : (double[])amounts.Clone();
        AccessibleDescription = "";
        for(int i=0;i<Math.Min(labels.Length,values.Length);i++)
            AccessibleDescription += labels[i]+": "+values[i].ToString("N0",CultureInfo.GetCultureInfo("fr-FR"))+" FC. ";
        Invalidate();
    }
    public int RequiredHeight(int width){
        int n=Math.Min(labels.Length,values.Length);float scale;using(var g=CreateGraphics())scale=Math.Max(1,g.DpiX/96f);
        int row=Math.Max((int)(26*scale),Font.Height+12);
        if(width/scale<430){
            for(int i=0;i<n;i++){
                string text=labels[i]+" · "+values[i].ToString("N0",CultureInfo.GetCultureInfo("fr-FR"))+" FC";
                int h=TextRenderer.MeasureText(text,Font,new Size(Math.Max(20,width-8),10000),TextFormatFlags.WordBreak).Height;
                row=Math.Max(row,h+(int)(18*scale));
            }
        }
        return Math.Max((int)(80*scale),n*row+(int)(12*scale));
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
        int n=Math.Min(labels.Length,values.Length);
        float scale=Math.Max(1f,g.DpiX/96f);bool dark=BackColor.GetBrightness()<0.35f;
        using(var ink=new SolidBrush(dark?Color.FromArgb(218,225,234):Color.FromArgb(100,116,139)))
        using(var bar=new SolidBrush(dark?Color.FromArgb(125,180,250):Accent))
        using(var track=new SolidBrush(dark?Color.FromArgb(47,59,77):Color.FromArgb(238,244,252)))
        using(var font=(Font)Font.Clone()) {
            if(n==0) { g.DrawString("Aucune donnée à afficher pour cette sélection.",font,ink,new RectangleF(12,20,Width-24,Height-24));return; }
            double max=0; for(int i=0;i<n;i++)max=Math.Max(max,values[i]);
            float row=Math.Max(26f*scale,(Height-12f*scale)/n), labelWidth=Math.Min(140f*scale,Width*.28f);
            float valueWidth=Math.Min(165f*scale,Width*.40f), start=labelWidth+8*scale;
            bool compact=Width/scale<430;
            float plotWidth=Math.Max(10f,Width-start-valueWidth-12*scale);
            using(var guide=new Pen(dark?Color.FromArgb(85,104,134):Color.FromArgb(228,234,242))){
                for(int tick=0;tick<=4;tick++){float xx=start+plotWidth*tick/4f;g.DrawLine(guide,xx,0,xx,Height);}
            }
            using(var fmt=new StringFormat()){fmt.Trimming=StringTrimming.EllipsisCharacter;fmt.FormatFlags=StringFormatFlags.NoWrap;
                for(int i=0;i<n;i++){
                    float y=6*scale+i*row, h=Math.Min(16*scale,row-10*scale);
                    if(compact){
                        float textHeight=Math.Max(font.GetHeight(g)+2,row-18*scale);
                        string compactText=labels[i]+" · "+values[i].ToString("N0",CultureInfo.GetCultureInfo("fr-FR"))+" FC";
                        TextRenderer.DrawText(g,compactText,font,new Rectangle(0,(int)y,Math.Max(20,Width-8),(int)textHeight),dark?Color.FromArgb(218,225,234):Color.FromArgb(100,116,139),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                        float bh=Math.Max(4,10*scale);
                        g.FillRectangle(track,0,y+textHeight+2,Math.Max(1,Width-8),bh);
                        if(max>0 && values[i]>0)g.FillRectangle(bar,0,y+textHeight+2,(float)((Width-8)*values[i]/max),bh);
                        continue;
                    }
                    g.DrawString(labels[i],font,ink,new RectangleF(0,y-2,labelWidth,row),fmt);
                    g.FillRectangle(track,start,y,plotWidth,h);
                    if(max>0 && values[i]>0)g.FillRectangle(bar,start,y,(float)(plotWidth*values[i]/max),h);
                    g.DrawString(values[i].ToString("N0",CultureInfo.GetCultureInfo("fr-FR"))+" FC",font,ink,new RectangleF(start+plotWidth+8*scale,y-2,valueWidth,row),fmt);
                }
            }
        }
    }
}

// Plain dashboard groups, consistent with native desktop forms.
public sealed class ITMDashboardSurface : Panel {
    public Color Accent=Color.Transparent;
    public ITMDashboardSurface(){DoubleBuffered=true;BackColor=Color.White;SetStyle(ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Width<20||Height<20)return;
        e.Graphics.Clear(BackColor);
        using(var pen=new Pen(BackColor.GetBrightness()<0.35f?Color.FromArgb(85,104,134):Color.FromArgb(226,232,241))){
            e.Graphics.DrawLine(pen,Width-1,8,Width-1,Height-8);
            e.Graphics.DrawLine(pen,8,Height-1,Width-8,Height-1);
        }
    }
}
