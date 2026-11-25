using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pulse {
internal sealed partial class Dashboard : Form {
    readonly List<Sample> history=new List<Sample>();
    readonly Color ink=Color.FromArgb(33,43,66), muted=Color.FromArgb(75,88,112);
    readonly Color[] colors={Color.FromArgb(0,118,148),Color.FromArgb(118,72,192),Color.FromArgb(190,87,45)};
    readonly string[] labels={"处理器","内存","图形处理器"};
    readonly string[] ids={"CPU","RAM","GPU"};
    readonly Button pause=new Button(), range=new Button();
    readonly Timer timer=new Timer();
    Sensors sensors;
    string cpu="",gpu="";
    bool frozen,reading;
    int seconds=60;
    readonly ToolTip tips=new ToolTip();
    public Dashboard() {
        Text="性能监视器"; Font=new Font("Microsoft YaHei UI",9);
        BackColor=Color.FromArgb(228,238,247); ClientSize=new Size(988,650); MinimumSize=new Size(760,590);
        DoubleBuffered=true; ResizeRedraw=true; StartPosition=FormStartPosition.CenterScreen;
        SetupButton(pause,"暂停"); SetupButton(range,"近 1 分钟");
        tips.SetToolTip(range,"切换最近 1 分钟或 5 分钟的趋势");
        pause.Click+=(s,e)=>{ frozen=!frozen; pause.Text=frozen?"继续":"暂停"; RefreshSurface(); };
        range.Click+=(s,e)=>{seconds=seconds==60?300:60;range.Text=seconds==60?"近 1 分钟":"近 5 分钟"; RefreshSurface();};
        InitializeWidget();
        timer.Interval=1000; timer.Tick+=async(s,e)=>await Tick();
        Shown+=async(s,e)=>{
            RefreshSurface();
            sensors=await Task.Run(()=>new Sensors());
            if(IsDisposed){sensors.Dispose();return;}
            timer.Start();
            var hardware=await Task.Run(()=>new[]{Sensors.Hardware("Win32_Processor"),Sensors.Hardware("Win32_VideoController")});
            if(!IsDisposed){cpu=hardware[0];gpu=hardware[1];RefreshSurface();}
        };
        FormClosed+=(s,e)=>{timer.Stop();timer.Dispose();tips.Dispose();if(!reading && sensors!=null)sensors.Dispose();};
    }
    void RefreshSurface(){
        if(compact){if(Visible && IsHandleCreated && !resizeFrames.Enabled)PresentWidget();}else Invalidate();
    }
    async Task Tick(){
        if(reading || frozen)return;
        reading=true;
        try {
            var sample=await Task.Run(()=>sensors.Read());
            if(!IsDisposed && !frozen){history.Add(sample);CheckLoad(sample);history.RemoveAll(p=>(sample.time-p.time).TotalSeconds>301);RefreshSurface();}
        } finally {reading=false;if(IsDisposed)sensors.Dispose();}
    }
    void SetupButton(Button b,string caption){
        b.Text=caption;b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(239,246,252);b.ForeColor=ink;
        b.FlatAppearance.BorderColor=Color.White;b.Cursor=Cursors.Hand;Controls.Add(b);
    }
    static GraphicsPath Shape(RectangleF r,float radius){
        float d=radius*2;var p=new GraphicsPath();
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);
        p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
    void Glass(Graphics g,RectangleF r){
        var shadow=r;shadow.Offset(0,3);
        using(var path=Shape(shadow,20))using(var b=new SolidBrush(Color.FromArgb(15,57,81,116)))g.FillPath(b,path);
        using(var path=Shape(r,20))
        using(var b=new LinearGradientBrush(r,Color.FromArgb(204,255,255,255),Color.FromArgb(105,249,253,255),75))
        using(var edge=new Pen(Color.FromArgb(225,255,255,255),1.5f)){g.FillPath(b,path);g.DrawPath(edge,path);}
    }
    void Glow(Graphics g,float x,float y,float w,float h,Color c){
        using(var p=new GraphicsPath()){p.AddEllipse(x,y,w,h);using(var b=new PathGradientBrush(p)){b.CenterColor=c;b.SurroundColors=new[]{Color.FromArgb(0,c)};g.FillPath(b,p);}}
    }
    void Label(Graphics g,string text,float x,float y,float width,float height,float size,Color color,bool bold=false,string family="Microsoft YaHei UI"){
        using(var font=new Font(family,Math.Max(11f,size*.9f),bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
        using(var b=new SolidBrush(color))using(var f=new StringFormat()){f.Trimming=StringTrimming.EllipsisCharacter;f.FormatFlags=StringFormatFlags.NoWrap;g.DrawString(text,font,b,new RectangleF(x,y,width,height),f);}
    }
    void Dot(Graphics g,float x,float y,float d,Color c){using(var b=new SolidBrush(c))g.FillEllipse(b,x,y,d,d);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        if(compact)return;
        int w=ClientSize.Width,h=ClientSize.Height;
        using(var bg=new LinearGradientBrush(ClientRectangle,Color.FromArgb(213,239,244),Color.FromArgb(228,219,246),32))g.FillRectangle(bg,ClientRectangle);
        Glow(g,-w*.35f,-h*.66f,w*1.35f,h*1.75f,Color.FromArgb(190,144,218,230));
        Glow(g,w*.46f,-h*.53f,w*.93f,h*1.35f,Color.FromArgb(214,184,182,244));
        Glow(g,w*.2f,h*.48f,w,h,Color.FromArgb(180,248,230,214));
        float m=30, gap=14, cw=(w-2*m-gap*2)/3;
        Label(g,"系统性能",m,24,w-260,45,31,ink,true);
        Dot(g,w-212,44,8,frozen?Color.DarkGray:Color.Teal);
        Label(g,frozen?"显示已暂停":history.Count==0?"正在采样":"实时监测",w-198,36,100,28,15,muted);
        pause.SetBounds(w-99,31,69,31);
        var last=history.LastOrDefault();
        for(int i=0;i<3;i++){
            float x=m+i*(cw+gap),y=85;
            Glass(g,new RectangleF(x,y,cw,144));
            Dot(g,x+18,y+22,9,colors[i]);Label(g,ids[i],x+33,y+13,58,25,16,colors[i],true,"Segoe UI");
            Label(g,labels[i],x+89,y+14,cw-101,24,14,muted);
            double v=last==null?double.NaN:last.values[i];
            Label(g,double.IsNaN(v)?"—":v.ToString("0")+"%",x+15,y+37,cw-30,65,46,ink,true,"Segoe UI");
            string detail=i==0?Environment.ProcessorCount+" 个逻辑处理器":i==1 && last!=null && last.total>0?string.Format("{0:F1} / {1:F1} GiB",last.used,last.total):i==1?"物理内存":"最忙 GPU 引擎";
            if(last!=null && double.IsNaN(v))detail="计数器暂不可用";
            Label(g,detail,x+19,y+101,cw-36,23,13,muted);
            var track=new RectangleF(x+20,y+128,cw-40,3);
            using(var b=new SolidBrush(Color.FromArgb(36,colors[i])))g.FillRectangle(b,track);
            if(!double.IsNaN(v))using(var b=new SolidBrush(colors[i]))g.FillRectangle(b,track.X,track.Y,(float)(track.Width*v/100),3);
        }
        float top=245,bottom=h-91;
        Glass(g,new RectangleF(m,top,w-2*m,bottom-top));
        Label(g,"负载趋势",m+18,top+15,145,33,21,ink,true);
        for(int i=0;i<3;i++){float lx=m+155+i*63;Dot(g,lx,top+28,7,colors[i]);Label(g,ids[i],lx+10,top+18,48,23,13,muted,false,"Segoe UI");}
        range.SetBounds(w-143,(int)top+14,91,30);
        float left=m+62,right=w-m-21,up=top+65,low=bottom-38;
        for(int j=0;j<=4;j++){
            float y=up+(low-up)*j/4;
            using(var p=new Pen(Color.FromArgb(44,91,108,131))){p.DashPattern=new[]{2f,4f};g.DrawLine(p,left,y,right,y);}
            Label(g,(100-25*j)+"%",m+12,y-9,50,22,12,muted,false,"Segoe UI");
        }
        Label(g,"−"+seconds+" 秒",left-3,low+8,85,24,12,muted);
        Label(g,"当前",right-30,low+8,39,24,12,muted);
        if(last!=null) for(int i=0;i<3;i++){
            PointF? prev=null;DateTime previousTime=DateTime.MinValue;
            using(var pen=new Pen(colors[i],2.4f)){
                foreach(var s in history){double age=(last.time-s.time).TotalSeconds;
                    if(age>seconds || double.IsNaN(s.values[i])){prev=null;continue;}
                    var pt=new PointF(right-(float)(age/seconds)*(right-left),low-(float)(s.values[i]/100)*(low-up));
                    if(prev.HasValue && (s.time-previousTime).TotalSeconds<2.8)g.DrawLine(pen,prev.Value,pt);
                    prev=pt;previousTime=s.time;
                }
                if(prev.HasValue)Dot(g,prev.Value.X-3,prev.Value.Y-3,6,colors[i]);
            }
        }
        Label(g,"CPU",m+1,bottom+13,42,23,13,colors[0],true,"Segoe UI");Label(g,cpu==""?"读取硬件信息中":cpu,m+50,bottom+11,w-112,22,13,muted);
        Label(g,"GPU",m+1,bottom+41,42,23,13,colors[2],true,"Segoe UI");Label(g,gpu==""?"读取硬件信息中":gpu,m+50,bottom+39,w-112,22,13,muted);
    }
    [STAThread] static void Main(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var single=new System.Threading.Mutex(false,@"Local\ChengJingPulseWidget")){try{if(!single.WaitOne(0))return;}catch(System.Threading.AbandonedMutexException){}try{Application.Run(new Dashboard());}finally{single.ReleaseMutex();}}}
}
}



