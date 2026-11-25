using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
namespace Pulse {
internal sealed partial class Dashboard {
    void OpenMessage(IslandMessage message){
        if(message.Source=="性能提醒"){
            OpenRanking(message.AppId=="RAM"?1:0);desktop.MarkRead(message);return;
        }
        if(message.Source=="专注计时"){
            SetExpanded(true);SelectSection(Section.Messages);page=0;desktop.MarkRead(message);return;
        }
        if(message.App=="微信"){
            if(ClientNotifications.ActivateWeChat())desktop.MarkRead(message);
            else tips.Show("未能切换到已有微信窗口",this,12,Height,2000);
            return;
        }
        if(message.Source=="会话状态"){
            Uri link;
            if(Uri.TryCreate(message.Session,UriKind.Absolute,out link) && (link.Scheme=="gamebuddy" || link.Scheme=="codex")){
                try{Process.Start(new ProcessStartInfo(link.AbsoluteUri){UseShellExecute=true});desktop.MarkRead(message);}
                catch(System.ComponentModel.Win32Exception){tips.Show("无法打开此会话",this,12,Height,2000);}
            }else tips.Show("此会话没有可用的跳转链接",this,12,Height,2000);
            return;
        }
        if(message.AppId==null || message.AppId.IndexOfAny(new[]{'"','\r','\n'})>=0)return;
        try{if(message.ClientPath!=null)Process.Start(new ProcessStartInfo(message.ClientPath){UseShellExecute=true});else Process.Start(new ProcessStartInfo("explorer.exe","\"shell:AppsFolder\\"+message.AppId+"\""){UseShellExecute=false});desktop.MarkRead(message);}
        catch(System.ComponentModel.Win32Exception){tips.Show("无法打开来源应用",this,12,Height,2000);}
    }
    async void ControlMusic(Song song,int direction){
        if(desktop==null || song==null)return;
        bool ok=await desktop.Control(song,direction);
        if(!ok && !IsDisposed)tips.Show("播放器未接受此操作",this,12,Height,2000);
    }
    void PaintMessage(Graphics g,int x,IslandMessage notice){
        TickerText(g,notice.App+(desktop.IsRead(notice)?" · 已读":" · 消息"),x+11,7,133,11,aiMint,true);
        TickerText(g,notice.Title,x+10,25,134,13,Color.White,true);
        TickerText(g,notice.Body,x+11,47,132,10,Color.White,false);
    }
    void PaintSong(Graphics g,int x,Song song){
        int tx=x+11;
        if(desktop.Cover!=null){g.DrawImage(desktop.Cover,x+10,17,34,34);tx+=39;}
        float width=x+142-tx;
        TickerText(g,song.Playing?"正在播放":"已暂停",tx,6,width,10,aiMint,true);
        TickerText(g,song.Title,tx,22,width,13,Color.White,true);
        TickerText(g,song.Artist,tx,40,width,10,Color.White,false);
        if(song.Duration>0){
            using(var rail=new SolidBrush(Color.FromArgb(52,255,255,255)))g.FillRectangle(rail,x+12,59,125,2);
            using(var fill=new SolidBrush(aiMint))g.FillRectangle(fill,x+12,59,(float)(125*song.Progress(DateTimeOffset.Now)),2);
        }
    }
}
}
