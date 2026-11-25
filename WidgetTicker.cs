using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace Pulse {
internal sealed partial class Dashboard {
    static readonly Color aiMint=Color.FromArgb(158,244,234),aiAmber=Color.FromArgb(255,210,158);
    static void TickerText(Graphics g,string text,float x,float y,float width,float size,Color color,bool strong){
        if(width<=0)return;
        using(var font=new Font("Microsoft YaHei UI",Math.Max(11,size),strong?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
        using(var ink=new SolidBrush(color))
        using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap,Trimming=StringTrimming.EllipsisCharacter}){
            g.DrawString(text,font,ink,new RectangleF(x,y,width,21),format);
        }
    }
    void PaintActivity(Graphics g,int x,AgentRun run,long now){
        if(run==null){
            bool ready=agents!=null && !agents.Unavailable;
            TickerText(g,ready?"性能速览":"AI 会话",x+12,7,83,11,Color.White,true);
            TickerText(g,ready?"CPU · 近 30 秒":agents==null?"正在连接":"来源暂不可用",x+12,47,130,10,Color.White,false);
            if(ready)PaintLoadTrace(g,x+13,26,124,16,DateTime.UtcNow);
            return;
        }
        bool active=run.state=="执行中",wait=run.state=="等你确认",success=run.state=="已完成";
        Color tone=wait || run.state=="失败"?aiAmber:active?aiMint:success?Color.FromArgb(188,222,255):Color.Silver;
        string state=active && agents.LiveCount>1?agents.LiveCount+" 个运行中":run.state;
        using(var pen=new Pen(tone,1.6f)){
            pen.StartCap=pen.EndCap=LineCap.Round;
            if(active){
                for(int i=0;i<3;i++){
                    float h=(float)(2+2*(1+Math.Sin(now*1.8+i*2)));
                    g.DrawLine(pen,x+12+i*3,17-h/2,x+12+i*3,17+h/2);
                }
            }else if(success){
                g.DrawLines(pen,new[]{new PointF(x+11,16),new PointF(x+14,19),new PointF(x+19,13)});
            }else{
                g.DrawEllipse(pen,x+10,11,11,11);g.DrawLine(pen,x+15.5f,14,x+15.5f,17);Dot(g,x+14.7f,18.5f,1.6f,tone);
            }
        }
        TickerText(g,state,x+25,8,78,11,tone,true);
        TickerText(g,run.Elapsed(now),x+105,8,55,11,Color.White,true);
        TickerText(g,run.title,x+11,26,132,13,Color.White,true);
        string action=wait?"去处理":success?"看结果":run.state=="失败"?"查原因":"看会话";
        TickerText(g,run.source+" · "+action,x+12,47,138,10,tone,false);
    }
    void PaintLoadTrace(Graphics g,float x,float y,float width,float height,DateTime now){
        using(var grid=new Pen(Color.FromArgb(54,255,255,255)))g.DrawLine(grid,x,y+height,x+width,y+height);
        using(var pen=new Pen(aiMint,1.5f)){
            PointF? last=null;DateTime previous=DateTime.MinValue;
            foreach(var sample in history){
                double age=(now-sample.time).TotalSeconds,value=sample.values[0];
                if(age<0 || age>30 || double.IsNaN(value)){last=null;continue;}
                var point=new PointF(x+width*(float)(1-age/30),y+height*(float)(1-value/100));
                if(last.HasValue && (sample.time-previous).TotalSeconds<2.8)g.DrawLine(pen,last.Value,point);
                last=point;previous=sample.time;
            }
            if(last.HasValue)Dot(g,last.Value.X-1.5f,last.Value.Y-1.5f,3,aiMint);
        }
    }
    Bitmap tickerFace,oldFace;
    string bannerKey;
    readonly System.Diagnostics.Stopwatch flip=new System.Diagnostics.Stopwatch();
    void ReleaseFaces(){
        if(tickerFace!=null)tickerFace.Dispose();
        if(oldFace!=null)oldFace.Dispose();
    }
    static void DrawFace(Graphics g,Bitmap face,int x,double angle,float opacity,float lift){
        float height=(float)(face.Height*Math.Cos(angle*Math.PI/180));
        var dest=new RectangleF(x,lift+(float)(face.Height*(1-Math.Cos(angle*Math.PI/180))*0.5),face.Width,height);
        using(var attrs=new System.Drawing.Imaging.ImageAttributes()){
            var fade=new System.Drawing.Imaging.ColorMatrix();fade.Matrix33=opacity;attrs.SetColorMatrix(fade);
            g.DrawImage(face,Rectangle.Round(dest),0,0,face.Width,face.Height,GraphicsUnit.Pixel,attrs);
        }
    }
    void PaintTicker(Graphics g,int agentWidth,int header){
        long now=AgentFeed.Now;
        int x=agentWidth-tickerWidth;
        var run=agents==null?null:agents.Primary(now);
        bool urgent=run!=null && (run.state=="等你确认" || run.Terminal);
        bool preview=!expanded && now<previewUntil && !urgent;
        if(desktop!=null)desktop.Advance(true,now);
        displayedMessage=desktop==null?null:desktop.Banner;
        displayedSong=desktop!=null && run==null && displayedMessage==null?desktop.Music:null;
        displayedActivity=displayedMessage==null && displayedSong==null?run:null;
        shownSection=displayedMessage!=null?Section.Messages:displayedSong!=null?Section.Music:Section.AI;
        bool unreadSummary=!expanded && !preview && desktop!=null && desktop.HasUnread &&
            displayedMessage==null && displayedActivity==null && displayedSong==null;
        focusCard=desktop!=null && desktop.Focusing && displayedMessage==null && !preview &&
            (run==null || run.state!="等你确认");
        if(focusCard){displayedSong=null;displayedActivity=null;unreadSummary=false;shownSection=Section.AI;}
        if(unreadSummary)shownSection=Section.Messages;
        if(preview && displayedMessage==null){
            shownSection=previewSection;
            displayedMessage=previewSection==Section.Messages && desktop!=null && desktop.Messages.Count>0?desktop.Messages[0]:null;
            displayedSong=previewSection==Section.Music && desktop!=null?desktop.Music:null;
            displayedActivity=previewSection==Section.AI?run:null;
        }
        string key=desktop!=null && desktop.Banner!=null?desktop.Banner.Key:null;
        if(key!=bannerKey){
            if(oldFace!=null){oldFace.Dispose();oldFace=null;}
            if(!expanded && System.Windows.SystemParameters.ClientAreaAnimation && tickerFace!=null){
                oldFace=(Bitmap)tickerFace.Clone();flip.Restart();lensFrames.Interval=16;
            }
            bannerKey=key;
        }
        int faceHeight=header<expandedHeader?compactHeight:expandedHeader;
        if(tickerFace!=null && tickerFace.Height!=faceHeight){tickerFace.Dispose();tickerFace=null;}
        if(tickerFace==null)tickerFace=new Bitmap(tickerWidth-22,faceHeight,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var face=Graphics.FromImage(tickerFace)){
            face.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;face.Clear(Color.Transparent);face.SmoothingMode=SmoothingMode.AntiAlias;
            if(faceHeight==compactHeight){
                face.TranslateTransform(0,4);PaintCompactFace(face,now,unreadSummary);
            }else PaintTickerFace(face,now,unreadSummary);
        }
        if(flip.IsRunning && (expanded || flip.ElapsedMilliseconds>=540)){
            flip.Reset();lensFrames.Interval=clearClock.IsRunning?16:120;if(oldFace!=null){oldFace.Dispose();oldFace=null;}
        }
        if(flip.IsRunning && oldFace!=null){
            double ms=flip.Elapsed.TotalMilliseconds;
            if(ms<260){
                float t=MotionEase(ms/320,.22,1,.36,1);
                DrawFace(g,oldFace,x,82*t,1-t,-4*t);
            }else{
                float t=1-MotionEase((ms-260)/280,.22,1,.36,1);
                DrawFace(g,tickerFace,x,58*t,1-t,-8*t);
            }
        }else g.DrawImageUnscaled(tickerFace,x,0);
        if(!expanded && desktop!=null && desktop.HasUnread && displayedMessage==null)
            Dot(g,x+151,header/2-14,4,aiMint);
        float tip=x+(expanded?157:151),tail=x+(expanded?152:156);
        using(var pen=new Pen(Color.FromArgb(210,255,255,255),1.5f))g.DrawLines(pen,new[]{new PointF(tail,header/2-4),new PointF(tip,header/2),new PointF(tail,header/2+4)});
    }
    void PaintCompactFace(Graphics g,long now,bool unreadSummary){
        string label,detail;int x=11;
        Color tone=aiMint;
        if(displayedMessage!=null){label=displayedMessage.App;detail=displayedMessage.Title;}
        else if(focusCard){
            label=desktop.FocusPaused?"专注 · 已暂停":"专注中";
            detail=TimeSpan.FromSeconds(desktop.FocusPaused?desktop.FocusRemaining:Math.Max(0,desktop.FocusUntil-now)).ToString(@"mm\:ss");
        }else if(unreadSummary){
            label=desktop.Messages.Find(m=>!m.Read).App;detail=desktop.UnreadCount+" 条未读";
        }else if(displayedSong!=null){
            label=displayedSong.Artist;detail=displayedSong.Title;
            if(desktop.Cover!=null){g.DrawImage(desktop.Cover,10,9,30,30);x=45;}
        }else if(displayedActivity!=null){
            label=displayedActivity.state+" · "+displayedActivity.Elapsed(now);detail=displayedActivity.title;
            if(displayedActivity.state=="等你确认")tone=aiAmber;
        }else if(shownSection==Section.AI && agents!=null && !agents.Unavailable){
            TickerText(g,"CPU · 近 30 秒",x,4,133,11,Color.White,true);
            PaintLoadTrace(g,x+1,22,126,14,DateTime.UtcNow);return;
        }else{
            label=shownSection==Section.Messages?"消息":shownSection==Section.Music?"音乐":"AI 会话";
            detail=desktop==null || agents==null?"正在连接":shownSection==Section.Messages?desktop.MessageStatus:shownSection==Section.Music?desktop.MusicStatus:"来源暂不可用";
        }
        TickerText(g,label,x,4,143-x,11,tone,true);
        TickerText(g,detail,x,22,143-x,13,Color.White,true);
    }
    void PaintTickerFace(Graphics g,long now,bool unreadSummary){
        int x=0;
        if(displayedMessage!=null)PaintMessage(g,x,displayedMessage);
        else if(focusCard){
            long left=desktop.FocusPaused?desktop.FocusRemaining:Math.Max(0,desktop.FocusUntil-now);
            TickerText(g,"专注",11,7,130,11,aiMint,true);
            TickerText(g,TimeSpan.FromSeconds(left).ToString(@"mm\:ss"),10,22,134,18,Color.White,true);
            TickerText(g,desktop.FocusPaused?"已暂停 · 点击继续":"点击暂停",11,47,132,11,Color.White,false);
        }
        else if(unreadSummary){
            var latest=desktop.Messages.Find(m=>!m.Read);
            TickerText(g,latest.App,x+11,7,132,11,aiMint,true);
            TickerText(g,desktop.UnreadCount+" 条未读",x+10,24,134,14,Color.White,true);
            TickerText(g,"点击回看消息",x+11,47,132,10,Color.White,false);
        }
        else if(displayedSong!=null)PaintSong(g,x,displayedSong);
        else if(shownSection!=Section.AI){
            bool messages=shownSection==Section.Messages;
            TickerText(g,messages?"消息":"音乐",x+12,9,120,12,aiMint,true);
            TickerText(g,desktop==null?"正在连接":messages?desktop.MessageStatus:desktop.MusicStatus,x+12,29,133,11,Color.White,false);
        }else PaintActivity(g,x,displayedActivity,now);
    }

}
}
