using System;
using System.Linq;
using System.Threading.Tasks;
namespace Pulse {
internal sealed partial class Dashboard {
    bool focusCard,pressedFocus;
    void ToggleFocus(){
        if(desktop==null)return;
        if(desktop.Focusing)desktop.EndFocus(AgentFeed.Now);else desktop.StartFocus(AgentFeed.Now);
        RefreshSurface();
    }
    readonly DateTime[] highSince=new DateTime[2];
    readonly bool[] loadAnnounced=new bool[2];
    DateTime previousLoadSample;
    void CheckLoad(Sample sample){
        for(int metric=0;metric<2;metric++){
            double value=sample.values[metric];
            if(double.IsNaN(value) || value<90 || (sample.time-previousLoadSample).TotalSeconds>5)
                highSince[metric]=DateTime.MinValue;
            if(double.IsNaN(value) || value<85)loadAnnounced[metric]=false;
            if(double.IsNaN(value) || value<90)continue;
            if(highSince[metric]==DateTime.MinValue)highSince[metric]=sample.time;
            if(loadAnnounced[metric] || (sample.time-highSince[metric]).TotalSeconds<20)continue;
            loadAnnounced[metric]=true;ReportLoad(metric);
        }
        previousLoadSample=sample.time;
    }
    async void ReportLoad(int metric){
        if(ranking==null || desktop==null)return;
        await ranking.UpdateRows();
        if(IsDisposed || Disposing)return;
        if(metric==0){
            await Task.Delay(1200);
            if(IsDisposed || Disposing)return;
            await ranking.UpdateRows();
            if(IsDisposed || Disposing)return;
        }
        var top=ranking.Sorted.Where(r=>r.Value(metric)>0).OrderByDescending(r=>r.Value(metric)).FirstOrDefault();
        desktop.AddIslandNotice("性能提醒",ids[metric]+" 持续高占用",top==null?"点击查看进程排行":top.name+" 占用最高",ids[metric]);
    }
}
}
