using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;

namespace Pulse {
internal sealed class Ranking : IDisposable {
    internal sealed class Row {
        internal int pid;
        internal string name;
        internal long memory,start,cpuTicks=-1;
        internal double cpu=double.NaN,gpu=double.NaN;
        internal double Value(int index){return index==0?cpu:index==1?memory:gpu;}
    }
    readonly ProcessIcons icons=new ProcessIcons();
    readonly Dictionary<int,Tuple<long,string,Bitmap>> loaded=new Dictionary<int,Tuple<long,string,Bitmap>>();
    readonly Timer refresh=new Timer{Interval=1200};
    Dictionary<int,Row> previous=new Dictionary<int,Row>();
    Sensors gpu;
    long stamp;
    bool closed,busy,loading;
    Row[] rows=new Row[0];
    internal Row[] Sorted=new Row[0];
    internal int Metric,Offset;
    internal const int VisibleRows=4;
    internal string Status="正在采样";
    internal Ranking(){refresh.Tick+=async(s,e)=>await UpdateRows();}
    internal async void Open(int metric){
        Metric=metric;Offset=0;Sort();if(!refresh.Enabled){refresh.Start();await UpdateRows();}
    }
    internal void Hide(){refresh.Stop();}
    internal void Scroll(int delta){Offset=Math.Max(0,Math.Min(Offset+delta,Math.Max(0,Sorted.Length-VisibleRows)));LoadIcons();}
    void Sort(){Sorted=rows.OrderByDescending(r=>r.Value(Metric)).ThenBy(r=>r.pid).ToArray();Scroll(0);}
    internal Bitmap ImageFor(Row r){
        Tuple<long,string,Bitmap> cached;
        return loaded.TryGetValue(r.pid,out cached) && cached.Item1==r.start && cached.Item2==r.name?cached.Item3:null;
    }
    async void LoadIcons(){
        if(loading || closed || !refresh.Enabled)return;
        var batch=Sorted.Skip(Offset).Take(VisibleRows).Where(r=>ImageFor(r)==null).ToArray();
        if(batch.Length==0)return;
        loading=true;
        try{
            var images=await Task.Run(()=>batch.Select(r=>icons.Read(r.pid)).ToArray());
            if(!closed)for(int i=0;i<batch.Length;i++)
                loaded[batch[i].pid]=Tuple.Create(batch[i].start,batch[i].name,images[i]);
        }finally{loading=false;if(closed)icons.Dispose();}
        LoadIcons();
    }
    Row[] Collect(){
        if(gpu==null)gpu=new Sensors();
        var engines=new Dictionary<int,double>();
        bool gpuAvailable=!double.IsNaN(gpu.ReadGpu(engines));
        long now=Stopwatch.GetTimestamp();
        double elapsed=(now-stamp)/(double)Stopwatch.Frequency;
        var next=new Dictionary<int,Row>();
        foreach(var process in Process.GetProcesses())using(process){
            try{
                if(process.Id==0)continue;
                var r=new Row{pid=process.Id,name=process.ProcessName,memory=process.WorkingSet64};
                next[r.pid]=r;
                double value;
                if(gpuAvailable)r.gpu=engines.TryGetValue(r.pid,out value)?value:0;

                r.start=process.StartTime.ToUniversalTime().Ticks;
                r.cpuTicks=process.TotalProcessorTime.Ticks;
                Row old;
                if(previous.TryGetValue(r.pid,out old) && old.start==r.start && old.cpuTicks>=0 && elapsed>0)
                    r.cpu=Math.Min(100,Math.Max(0,(r.cpuTicks-old.cpuTicks)*100.0/TimeSpan.TicksPerSecond/elapsed/Environment.ProcessorCount));
            }catch(InvalidOperationException){}catch(Win32Exception){}catch(NotSupportedException){}
        }
        previous=next;stamp=now;return next.Values.ToArray();
    }
    internal async Task UpdateRows(){
        if(busy || closed)return;busy=true;
        try{
            var data=await Task.Run(()=>Collect());
            if(!closed){
                rows=data;var live=new HashSet<int>(rows.Select(r=>r.pid));
                foreach(int id in loaded.Keys.Where(id=>!live.Contains(id)).ToArray())loaded.Remove(id);
                Sort();Status=rows.Length+" 个进程";
            }
        }catch(Win32Exception){Status="采样暂不可用";}
        finally{busy=false;if(closed && gpu!=null)gpu.Dispose();}
    }
    public void Dispose(){closed=true;refresh.Dispose();if(!busy && gpu!=null)gpu.Dispose();if(!loading)icons.Dispose();}
}
}
