using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Windows.Media;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Pulse {
[System.Runtime.Serialization.DataContract]
internal sealed class IslandMessage {
    [System.Runtime.Serialization.DataMember] public string App,Title,Body,AppId,Key,ClientPath,Source,Session;
    [System.Runtime.Serialization.DataMember] public bool Read;
    [System.Runtime.Serialization.DataMember] public DateTimeOffset Received;
    [System.Runtime.Serialization.DataMember] public DateTimeOffset At;
}
internal sealed class Song {
    internal GlobalSystemMediaTransportControlsSession Session;
    internal string Title,Artist,AppId;
    internal bool Playing,Toggle,Previous,Next;
    internal double Duration,Position,Rate;
    internal DateTimeOffset Measured;
    internal double Progress(DateTimeOffset now){
        if(Duration<=0)return 0;
        return Math.Max(0,Math.Min(1,(Position+(Playing?(now-Measured).TotalSeconds*Rate:0))/Duration));
    }
}
internal sealed class DesktopActivities : IDisposable {
    readonly Timer timer=new Timer{Interval=1800};
    readonly DateTimeOffset started=DateTimeOffset.Now;
    readonly Queue<IslandMessage> pending=new Queue<IslandMessage>();
    internal int UnreadCount{get{return Messages.Count(m=>!m.Read);}}
    internal bool HasUnread{get{return Messages.Any(m=>!m.Read);}}
    internal bool IsRead(IslandMessage msg){return msg!=null && msg.Read;}
    internal void MarkRead(IslandMessage msg){
        var entry=msg==null?null:Messages.FirstOrDefault(m=>m.Key==msg.Key);
        if(entry==null || entry.Read)return;
        entry.Read=true;history.Save(Messages);
        if(Banner!=null && Banner.Key==entry.Key)Banner=null;
    }
    internal bool ClearMessages(){
        if(!history.Clear())return false;
        Messages.Clear();pending.Clear();Banner=null;
        return true;
    }
    readonly MessageHistory history;
    HashSet<string> visibleToasts=new HashSet<string>();
    GlobalSystemMediaTransportControlsSessionManager manager;
    internal List<IslandMessage> Messages=new List<IslandMessage>();
    internal Song Music;
    internal Bitmap Cover;
    internal string MessageStatus="正在读取通知",MusicStatus="正在读取播放器";
    internal bool NeedsAccess;
    internal IslandMessage Banner;
    long until;
    bool busy,disposed,commandBusy;
    readonly ClientNotifications clients;
    internal DesktopActivities(string historyPath=null){
        history=new MessageHistory(historyPath);Messages=history.Load();
        clients=new ClientNotifications(AddClientMessage);timer.Tick+=(s,e)=>{if(Focusing && !FocusPaused && AgentFeed.Now>=FocusUntil)EndFocus(AgentFeed.Now);Poll();};timer.Start();Poll();
    }
    internal void OnFlash(IntPtr window){if(!disposed)clients.OnFlash(window);}
    internal bool Focusing{get;private set;}
    internal long FocusUntil,FocusRemaining;
    internal int QuietCount;
    internal bool FocusPaused{get{return Focusing && FocusUntil==0;}}
    internal void StartFocus(long now){
        Focusing=true;QuietCount=0;FocusRemaining=1500;FocusUntil=now+FocusRemaining;pending.Clear();Banner=null;
    }
    internal void PauseOrResume(long now){
        if(!Focusing)return;
        if(FocusPaused)FocusUntil=now+FocusRemaining;
        else{FocusRemaining=Math.Max(0,FocusUntil-now);FocusUntil=0;}
    }
    internal void EndFocus(long now){
        if(!Focusing)return;
        long left=FocusPaused?FocusRemaining:Math.Max(0,FocusUntil-now);
        Focusing=false;
        AddIslandNotice("专注计时","专注 "+TimeSpan.FromSeconds(Math.Max(0,1500-left)).ToString(@"mm\:ss"),"期间收到 "+QuietCount+" 条提醒","");
    }
    internal void AddIslandNotice(string source,string title,string body,string id){
        Insert(new IslandMessage{App=source,Source=source,Title=title,Body=body,AppId=id,At=DateTimeOffset.Now,Key=Guid.NewGuid().ToString()},true);
        history.Save(Messages);
    }
    void Insert(IslandMessage message,bool announce){
        message.Received=DateTimeOffset.Now;
        Messages.Insert(0,message);if(Messages.Count>40)Messages.RemoveAt(40);
        if(Focusing && !(message.Source=="会话状态" && message.Body=="等你确认")){
            if(announce || message.Source=="会话状态")QuietCount++;
            return;
        }
        if(announce){pending.Enqueue(message);while(pending.Count>40)pending.Dequeue();}
    }
    internal void AddAgent(AgentRun run){
        string key=run.source+"|"+run.id+"|"+run.state+"|"+run.end;
        if(Messages.Any(m=>m.Key==key))return;
        Insert(new IslandMessage{App=run.source,Title=run.title,Body=run.state,Source="会话状态",
            Session=run.sessionLink==null?null:run.sessionLink.AbsoluteUri,At=DateTimeOffset.Now,Key=key},false);
        history.Save(Messages);
    }
    readonly Dictionary<string,DateTimeOffset> lastFlash=new Dictionary<string,DateTimeOffset>();
    internal void AddClientMessage(IslandMessage message){
        if(message.Title=="任务栏提醒"){
            DateTimeOffset last;
            bool continuing=lastFlash.TryGetValue(message.App,out last) && message.At>=last && (message.At-last).TotalSeconds<=8;
            lastFlash[message.App]=message.At;
            if(continuing)return;
            message.Title=message.App+"有新提醒";
            message.Source="任务栏提醒";
        }
        if(Messages.Any(m=>m.App==message.App && Math.Abs((message.At-m.At).TotalSeconds)<2))return;
        if(message.Source==null)message.Source=message.Title;Insert(message,true);history.Save(Messages);
    }
    internal static string Identify(string id,string name){
        if(id==null)id="";
        string leaf=Path.GetFileName(id).ToLowerInvariant();
        if(string.Equals(name,"微信",StringComparison.OrdinalIgnoreCase) || string.Equals(name,"WeChat",StringComparison.OrdinalIgnoreCase) ||
           string.Equals(name,"Weixin",StringComparison.OrdinalIgnoreCase) || leaf.StartsWith("tencent.wechat_",StringComparison.Ordinal) || leaf=="weixin.exe" || leaf=="wechat.exe" || leaf=="tencent.wechat" || leaf=="tencent.weixin")return "微信";
        if(leaf=="xiezuo.exe" || leaf=="com.kingsoft.xiezuo" || string.Equals((name??"").Replace(" ",""),"WPS协作",StringComparison.OrdinalIgnoreCase))return "WPS协作";
        return null;
    }
    internal async void RequestAccess(){
        try{
            var listener=UserNotificationListener.Current;
            if(listener.GetAccessStatus()==UserNotificationListenerAccessStatus.Denied)
                await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-notifications"));
            else await listener.RequestAccessAsync();
            Poll();
        }
        catch(Exception){MessageStatus="请检查 Windows 通知访问权限";}
    }
    internal void Advance(bool mayShow,long now){
        if(Focusing && !FocusPaused && now>=FocusUntil)EndFocus(now);
        if(Banner!=null && now>=until)Banner=null;
        while(Banner==null && mayShow && pending.Count>0){
            var candidate=pending.Dequeue();if(IsRead(candidate) || !Messages.Contains(candidate))continue;
            Banner=candidate;until=now+7;
        }
    }
    internal void ApplyMessages(List<IslandMessage> rows){
        bool changed=false;
        foreach(var row in rows.OrderBy(m=>m.At)){
            if(row.At<=history.ClearedThrough || visibleToasts.Contains(row.Key) || Messages.Any(m=>m.Key==row.Key))continue;
            var client=Messages.FirstOrDefault(m=>m.ClientPath!=null && m.App==row.App && Math.Abs((row.At-m.At).TotalSeconds)<2);
            if(client!=null){
                client.Key=row.Key;client.Title=row.Title;client.Body=row.Body;client.Source="系统通知";
                client.ClientPath=null;client.AppId=row.AppId;
            }else{
                row.Source="系统通知";Insert(row,row.At>=started);
            }
            changed=true;
        }
        visibleToasts=new HashSet<string>(rows.Select(r=>r.Key));
        if(changed)history.Save(Messages);
    }
    async Task ReadMessages(){
        var listener=UserNotificationListener.Current;
        var access=listener.GetAccessStatus();
        NeedsAccess=access!=UserNotificationListenerAccessStatus.Allowed;
        if(NeedsAccess){MessageStatus="客户端提醒已开启 · 点此授权系统通知";return;}
        var notifications=await listener.GetNotificationsAsync(NotificationKinds.Toast);
        if(disposed)return;
        var rows=new List<IslandMessage>();
        foreach(var n in notifications){
            try{
                string id=n.AppInfo.AppUserModelId,app=Identify(id,null);
                if(app==null)app=Identify(id,n.AppInfo.DisplayInfo.DisplayName);
                if(app==null)continue;
                var binding=n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                if(binding==null)continue;
                var lines=binding.GetTextElements().Select(t=>t.Text).Where(t=>!string.IsNullOrWhiteSpace(t)).ToArray();
                string title=lines.FirstOrDefault()??"新通知",body=string.Join(" ",lines.Skip(1));
                rows.Add(new IslandMessage{App=app,AppId=id,Title=title,Body=body,At=n.CreationTime,Key=id+"|"+n.Id+"|"+n.CreationTime.UtcTicks+"|"+title+"|"+body});
            }catch(System.Runtime.InteropServices.COMException){}
        }
        ApplyMessages(rows);MessageStatus="等待微信或 WPS 协作提醒";
    }
    async Task ReadMusic(){
        if(manager==null)manager=await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session=manager.GetCurrentSession();
        if(session==null){SetMusic(null,null);MusicStatus="等待播放器开始播放";return;}
        var play=session.GetPlaybackInfo();var data=await session.TryGetMediaPropertiesAsync();
        if(disposed)return;
        bool playing=play.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if(data==null || data.PlaybackType==MediaPlaybackType.Video || string.IsNullOrWhiteSpace(data.Title) ||
           (!playing && play.PlaybackStatus!=GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)){
            SetMusic(null,null);MusicStatus="等待音乐播放";return;
        }
        var time=session.GetTimelineProperties();
        var song=new Song{Session=session,Title=data.Title,Artist=data.Artist,AppId=session.SourceAppUserModelId,Playing=playing,
            Previous=play.Controls.IsPreviousEnabled,Next=play.Controls.IsNextEnabled,
            Toggle=play.Controls.IsPlayPauseToggleEnabled || (playing?play.Controls.IsPauseEnabled:play.Controls.IsPlayEnabled),
            Rate=play.PlaybackRate??1,Position=(time.Position-time.StartTime).TotalSeconds,
            Duration=(time.EndTime-time.StartTime).TotalSeconds,Measured=time.LastUpdatedTime};
        Bitmap image=null;
        bool changed=Music==null || song.Title!=Music.Title || song.Artist!=Music.Artist || song.AppId!=Music.AppId;
        if(changed && data.Thumbnail!=null){
            try{using(var stream=await data.Thumbnail.OpenReadAsync())if(stream.Size>0 && stream.Size<=8*1024*1024)
                using(var input=stream.AsStreamForRead())using(var original=Image.FromStream(input))image=new Bitmap(original,40,40);
            }catch(Exception){}
        }
        if(disposed){if(image!=null)image.Dispose();return;}
        if(changed)SetMusic(song,image);else Music=song;
        MusicStatus=playing?"播放中":"已暂停";
    }
    void SetMusic(Song song,Bitmap image){if(Cover!=null)Cover.Dispose();Cover=image;Music=song;}
    async Task MessagesSafe(){try{await ReadMessages();}catch(Exception){MessageStatus="等待客户端提醒 · 系统通知暂不可用";}}
    async Task MusicSafe(){try{await ReadMusic();}catch(Exception){manager=null;SetMusic(null,null);MusicStatus="媒体会话暂不可用";}}
    async void Poll(){if(disposed || busy)return;busy=true;try{await Task.WhenAll(MessagesSafe(),MusicSafe());}finally{busy=false;}}
    internal async Task<bool> Control(Song song,int direction){
        if(commandBusy || song==null)return false;
        commandBusy=true;
        try{
            if(direction<0)return song.Previous && await song.Session.TrySkipPreviousAsync();
            if(direction>0)return song.Next && await song.Session.TrySkipNextAsync();
            if(!song.Toggle)return false;
            var play=song.Session.GetPlaybackInfo();
            if(play.Controls.IsPlayPauseToggleEnabled)return await song.Session.TryTogglePlayPauseAsync();
            return play.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing?
                await song.Session.TryPauseAsync():await song.Session.TryPlayAsync();
        }catch(Exception){return false;}finally{commandBusy=false;Poll();}
    }
    public void Dispose(){disposed=true;clients.Dispose();timer.Dispose();SetMusic(null,null);}
}
}

