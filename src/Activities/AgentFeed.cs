using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse {
// Only the session metadata queries below are executed. All attached stores are opened read-only.
internal sealed class AgentStore : IDisposable {
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int length,out IntPtr stmt,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text16(IntPtr stmt,int index);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_column_count(IntPtr stmt);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
    IntPtr db;
    static byte[] Utf8(string s){return Encoding.UTF8.GetBytes(s+"\0");}
    internal static string UriFor(string path){return new Uri(Path.GetFullPath(path)).AbsoluteUri+"?mode=ro";}
    internal AgentStore(string path){
        if(sqlite3_open_v2(Utf8(UriFor(path)),out db,0x41,IntPtr.Zero)!=0){Dispose();throw new IOException();}
        sqlite3_busy_timeout(db,220);
    }
    internal List<string[]> Query(string sql){
        IntPtr stmt;
        if(sqlite3_prepare_v2(db,Utf8(sql),-1,out stmt,IntPtr.Zero)!=0)throw new IOException();
        try{
            var rows=new List<string[]>();int rc;
            while((rc=sqlite3_step(stmt))==100){
                var row=new string[sqlite3_column_count(stmt)];
                for(int i=0;i<row.Length;i++)row[i]=Marshal.PtrToStringUni(sqlite3_column_text16(stmt,i));
                rows.Add(row);
            }
            if(rc!=101)throw new IOException();
            return rows;
        }finally{sqlite3_finalize(stmt);}
    }
    public void Dispose(){if(db!=IntPtr.Zero){sqlite3_close(db);db=IntPtr.Zero;}}
}

internal sealed class AgentRun {
    internal string source,id,title,state;
    internal Uri sessionLink;
    internal long start,end,wait;
    internal bool Terminal{get{return state=="已完成" || state=="已中断" || state=="失败";}}
    internal string Elapsed(long now){
        if(state=="状态待确认" || state=="未知状态")return "—";
        long duration=Math.Max(0,(end>0?end:now)-start-wait);
        return duration>=3600?string.Format("{0}:{1:00}:{2:00}",duration/3600,duration/60%60,duration%60):string.Format("{0:00}:{1:00}",duration/60,duration%60);
    }
}
internal sealed class AgentFeed {
    internal readonly List<AgentRun> runs=new List<AgentRun>();
    internal readonly List<string> issues=new List<string>();
    internal static long Now{get{return DateTimeOffset.UtcNow.ToUnixTimeSeconds();}}
    // Latest round per conversation; a held request belongs to a node of that exact run.
    const string BuddySql=@"
WITH recent AS (
 SELECT r.*,row_number() OVER(PARTITION BY r.thread_id ORDER BY r.ordinal DESC) n
 FROM orchestration_v2_projection_runs r
 WHERE r.completed_at IS NULL OR julianday(r.completed_at)>julianday('now','-1 day')
), holds AS (
 SELECT n.run_id, unixepoch(q.created_at) begin_at, unixepoch(q.resolved_at) finish_at
 FROM orchestration_v2_projection_runtime_requests q
 JOIN orchestration_v2_projection_nodes n ON n.node_id=q.node_id
 WHERE q.kind IN ('user_input','approval') AND q.created_at IS NOT NULL
), spans AS (
 SELECT *,max(coalesce(finish_at,unixepoch())) OVER(PARTITION BY run_id ORDER BY begin_at ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) prior_end FROM holds
)
SELECT r.run_id,t.title,r.status,unixepoch(r.requested_at),unixepoch(r.completed_at),
 (SELECT min(begin_at) FROM holds WHERE run_id=r.run_id AND finish_at IS NULL),
 (SELECT sum(max(0,coalesce(finish_at,unixepoch())-max(begin_at,coalesce(prior_end,begin_at)))) FROM spans WHERE run_id=r.run_id),NULL,r.thread_id
FROM recent r JOIN orchestration_v2_projection_threads t ON t.thread_id=r.thread_id
WHERE (r.n=1 OR r.completed_at IS NOT NULL) AND t.deleted_at IS NULL AND t.archived_at IS NULL";
    const string DesktopSql=@"
WITH latest AS (
 SELECT s.id AS thread_id,coalesce(nullif(trim(s.name),''),s.title) AS title,s.updated_at,s.rollout_path,t.turn_id,t.status,t.started_at,t.completed_at,
 row_number() OVER(PARTITION BY s.id ORDER BY t.rollout_ordinal DESC) n
 FROM threads s JOIN hist.thread_turns t ON t.thread_id=s.id
 WHERE s.originator='Codex Desktop' AND s.archived=0
 AND (s.thread_source='user' OR (s.thread_source IS NULL AND s.source='vscode'))
 AND s.updated_at>=strftime('%s','now','-1 day')
)
SELECT turn_id,title,status,started_at,completed_at,NULL,NULL,rollout_path,thread_id FROM latest WHERE n=1 OR completed_at>=strftime('%s','now','-1 day')+0";
    static long Number(string s){long v;return long.TryParse(s,out v)?v:0;}
    internal static AgentFeed Read(){
        var feed=new AgentFeed();
        string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        feed.Source("GameBuddy",Path.Combine(local,@"GameBuddy\data\t3\userdata\statev2.sqlite"),BuddySql,null);
        string home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex");
        feed.Source("ChatGPT · Codex",Path.Combine(home,"state_5.sqlite"),DesktopSql,Path.Combine(home,"thread_history_1.sqlite"));
        return feed;
    }
    static Uri SessionLink(string threadId,bool desktop,string environmentId){
        Guid thread,environment;
        if(!Guid.TryParseExact(threadId,"D",out thread))return null;
        if(desktop)return new Uri("codex://threads/"+thread.ToString("D"));
        return Guid.TryParseExact(environmentId,"D",out environment)
            ?new Uri("gamebuddy://threads/"+environment.ToString("D")+"/"+thread.ToString("D")):null;
    }
    void Source(string source,string path,string sql,string extra){
        try{
            using(var store=new AgentStore(path)){
                if(extra!=null)store.Query("ATTACH '"+AgentStore.UriFor(extra).Replace("'","''")+"' AS hist");
                string environment=null;
                if(extra==null){
                    try{environment=File.ReadAllText(Path.Combine(Path.GetDirectoryName(path),"environment-id")).Trim();}
                    catch(IOException){}catch(UnauthorizedAccessException){}
                }
                foreach(var r in store.Query(sql)){
                    long start=Number(r[3]),end=Number(r[4]);if(start<=0)continue;
                    string state;
                    switch(r[2]){
                        case "completed":state="已完成";break;
                        case "failed":state="失败";break;
                        case "interrupted":case "cancelled":case "canceled":state="已中断";break;
                        case "running":case "inProgress":state="执行中";break;
                        default:state="未知状态";break;
                    }
                    long wait=Number(r[6]);
                    if(state=="执行中" && Number(r[5])>0){state="等你确认";end=Now;}
                    if(state=="执行中" && extra!=null && (!File.Exists(r[7]) || DateTime.UtcNow-File.GetLastWriteTimeUtc(r[7])>TimeSpan.FromMinutes(3)))state="状态待确认";
                    if(string.IsNullOrEmpty(r[0]))continue;
                    runs.Add(new AgentRun{source=source,id=r[0],sessionLink=SessionLink(r[8],extra!=null,environment),title=string.IsNullOrWhiteSpace(r[1])?"未命名会话":r[1],state=state,start=start,end=end,wait=wait});
                }
            }
        }catch(IOException){issues.Add(source);}catch(UnauthorizedAccessException){issues.Add(source);}
    }
}
}


