using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pulse {
internal sealed class AgentTicker : IDisposable {
    readonly Action<AgentRun> notify;
    readonly Timer poll=new Timer{Interval=3000};
    readonly Dictionary<string,string> known=new Dictionary<string,string>();
    readonly HashSet<string> initialized=new HashSet<string>();
    readonly Queue<AgentRun> notices=new Queue<AgentRun>();
    AgentRun currentNotice,waiting,live;
    long noticeUntil;
    bool busy,disposed;
    internal int LiveCount{get;private set;}
    internal int HistoryCount{get;private set;}
    internal bool Unavailable{get;private set;}
    internal AgentRun[] Rows=new AgentRun[0];
    internal AgentTicker(Action<AgentRun> notify){
        this.notify=notify;poll.Tick+=(s,e)=>Poll();poll.Start();Poll();
    }
    async void Poll(){
        if(busy || disposed)return;
        busy=true;
        try{var feed=await Task.Run(()=>AgentFeed.Read());if(!disposed)Apply(feed,AgentFeed.Now);}
        finally{busy=false;}
    }
    internal void Apply(AgentFeed feed,long now){
        var next=new Dictionary<string,string>();
        foreach(var r in feed.runs.OrderBy(x=>x.end)){
            string key=r.source+"|"+r.id,old;known.TryGetValue(key,out old);
            if(initialized.Contains(r.source) && old!=r.state &&
                ((r.Terminal && (old!=null || (r.end>0 && now-r.end<10))) || r.state=="等你确认")){
                notify(r);
                if(r.Terminal)notices.Enqueue(r);
            }
            next[key]=r.state;
        }
        foreach(var src in new[]{"GameBuddy","ChatGPT · Codex"}){
            if(feed.issues.Contains(src)){initialized.Remove(src);continue;}
            initialized.Add(src);
        }
        known.Clear();foreach(var kv in next)known[kv.Key]=kv.Value;
        var active=feed.runs.Where(x=>x.state=="执行中").OrderByDescending(x=>x.start).ToArray();
        waiting=feed.runs.Where(x=>x.state=="等你确认").OrderBy(x=>x.start).FirstOrDefault();
        live=active.FirstOrDefault();LiveCount=active.Length;
        var recent=feed.runs.Where(x=>x.Terminal).OrderByDescending(x=>x.end).Take(15).ToArray();
        HistoryCount=recent.Length;Unavailable=feed.issues.Count==2;
        Rows=feed.runs.Where(x=>!x.Terminal).OrderBy(x=>x.state=="等你确认"?0:x.state=="执行中"?1:2)
            .ThenByDescending(x=>x.start).Concat(recent).ToArray();
    }
    string followed=Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\ChengJing\Pulse","FollowedSession","") as string;
    static string SessionKey(AgentRun r){return r.sessionLink!=null?r.sessionLink.AbsoluteUri:r.source+"|"+r.id;}
    internal bool Follows(AgentRun r){return followed==SessionKey(r);}
    internal void ToggleFollow(AgentRun r){
        followed=Follows(r)?"":SessionKey(r);
        Microsoft.Win32.Registry.SetValue(@"HKEY_CURRENT_USER\Software\ChengJing\Pulse","FollowedSession",followed);
    }
    internal AgentRun Primary(long now){
        // Waiting for the user holds the card. Give each queued result its full display time afterwards.
        if(waiting!=null){
            if(currentNotice!=null)noticeUntil=now+6;
            return waiting;
        }
        if(now>=noticeUntil){
            currentNotice=notices.Count==0?null:notices.Dequeue();
            noticeUntil=currentNotice==null?0:now+6;
        }
        return currentNotice??Rows.FirstOrDefault(Follows)??live;
    }
    public void Dispose(){disposed=true;poll.Dispose();}
}
}
