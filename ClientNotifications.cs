using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Pulse {
// Observe only the top-level windows belonging to supported clients.
internal sealed class ClientNotifications : IDisposable {
    delegate bool EnumWindow(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr data);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out int pid);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int capacity);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder text,int capacity);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [StructLayout(LayoutKind.Sequential)] struct Rect {internal int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    internal static bool ActivateWeChat(){
        var ids=new HashSet<int>();
        foreach(string name in new[]{"Weixin","WeChat"})
            foreach(var process in Process.GetProcessesByName(name))using(process)ids.Add(process.Id);
        IntPtr best=IntPtr.Zero;long bestScore=-1;
        EnumWindows(delegate(IntPtr window,IntPtr unused){
            int pid;GetWindowThreadProcessId(window,out pid);if(!ids.Contains(pid))return true;
            var text=new StringBuilder(256);GetClassName(window,text,text.Capacity);
            string cls=text.ToString();
            bool main=cls=="WeChatMainWndForPC";
            if(!main && !(cls.StartsWith("Qt",StringComparison.Ordinal) && cls.EndsWith("QWindowIcon",StringComparison.Ordinal)))return true;
            text.Clear();GetWindowText(window,text,text.Capacity);
            if(!main && !Regex.IsMatch(text.ToString(),@"^(微信|WeChat|Weixin)(?:\s*[（(\[].*|$)",RegexOptions.IgnoreCase))return true;
            Rect r;if(!GetWindowRect(window,out r))return true;
            int width=r.Right-r.Left,height=r.Bottom-r.Top;
            if(!main && !IsIconic(window) && (width<400 || height<300))return true;
            long score=(main?100000000L:0)+(IsWindowVisible(window)?50000000L:0)+(long)width*height;
            if(score>bestScore){best=window;bestScore=score;}return true;
        },IntPtr.Zero);
        if(best==IntPtr.Zero)return false;
        ShowWindow(best,IsIconic(best)?9:5);
        return SetForegroundWindow(best) || GetForegroundWindow()==best;
    }
    static readonly Regex count=new Regex(@"(?:[\[【(（](\d+)\+?[\]】)）]|(\d+)\s*条未读)");
    readonly Timer timer=new Timer{Interval=450};
    readonly Action<IslandMessage> publish;
    readonly Dictionary<int,string> clients=new Dictionary<int,string>();
    Dictionary<IntPtr,string> previous=new Dictionary<IntPtr,string>();
    bool initialized;
    int ticks;
    bool readingBadges,disposed;
    Dictionary<string,int> badgeCounts;
    async void ReadBadges(){
        if(readingBadges)return;
        readingBadges=true;
        try{
            var counts=await System.Threading.Tasks.Task.Run(()=>ShellUnread.Sample());
            if(disposed)return;
            foreach(var pair in counts){
                int last=0;
                if(badgeCounts==null || pair.Value==0)continue;
                badgeCounts.TryGetValue(pair.Key,out last);
                if(pair.Value<=last)continue;
                foreach(var client in clients){
                    if(client.Value!=pair.Key && !(pair.Key=="Weixin" && client.Value=="WeChat"))continue;
                    Notify(client.Key,client.Value,"新增未读消息",pair.Value);break;
                }
            }
            badgeCounts=counts;
        }catch(System.Windows.Automation.ElementNotAvailableException){}catch(COMException){}catch(InvalidOperationException){}catch(UnauthorizedAccessException){}
        finally{readingBadges=false;}
    }
    internal ClientNotifications(Action<IslandMessage> handler){
        publish=handler;timer.Tick+=(s,e)=>Scan();Scan();timer.Start();
    }
    static int Unread(string title){
        var m=count.Match(title);int value;
        return m.Success && int.TryParse(m.Groups[m.Groups[1].Success?1:2].Value,out value)?value:0;
    }
    void Scan(){
        if(ticks++%12==0){
            clients.Clear();
            foreach(string name in new[]{"Weixin","WeChat","xiezuo"})
                foreach(var p in Process.GetProcessesByName(name))using(p)clients[p.Id]=name;
        }
        if(ticks%6==2)ReadBadges();
        var current=new Dictionary<IntPtr,string>();
        EnumWindows(delegate(IntPtr hwnd,IntPtr unused){
            int pid;GetWindowThreadProcessId(hwnd,out pid);
            string name;if(!clients.TryGetValue(pid,out name) || !IsWindowVisible(hwnd))return true;
            var buffer=new StringBuilder(256);GetClassName(hwnd,buffer,256);
            string cls=buffer.ToString();
            if(!(cls=="Chrome_WidgetWin_1" || cls.StartsWith("Qt",StringComparison.Ordinal) && cls.EndsWith("QWindowIcon",StringComparison.Ordinal) || cls.StartsWith("WeChat",StringComparison.Ordinal)))return true;
            buffer.Clear();GetWindowText(hwnd,buffer,256);string title=buffer.ToString();
            Rect r;if(!GetWindowRect(hwnd,out r))return true;
            var area=Screen.FromHandle(hwnd).WorkingArea;
            int w=r.Right-r.Left,h=r.Bottom-r.Top;
            bool popup=w>=180 && w<=620 && h>=60 && h<=300 && Math.Abs(area.Right-r.Right)<=90 &&
                (Math.Abs(area.Bottom-r.Bottom)<=125 || Math.Abs(area.Top-r.Top)<=100);
            string old;
            bool existed=previous.TryGetValue(hwnd,out old);
            current[hwnd]=title;
            if(!initialized || !(popup && !existed || existed && Unread(title)>Unread(old)))return true;
            Notify(pid,name,popup?"桌面提醒":"新增未读消息",popup?0:Unread(title));
            return true;
        },IntPtr.Zero);
        initialized=true;previous=current;
    }
    internal void OnFlash(IntPtr window){
        int pid;GetWindowThreadProcessId(window,out pid);
        string name;if(clients.TryGetValue(pid,out name))Notify(pid,name,"任务栏提醒");
    }
    void Notify(int pid,string name,string title,int unread=0){
            string path;
            try{using(var p=Process.GetProcessById(pid))path=p.MainModule.FileName;}
            catch(System.ComponentModel.Win32Exception){return;}catch(InvalidOperationException){return;}catch(ArgumentException){return;}
            string app=name=="xiezuo"?"WPS协作":"微信";
            publish(new IslandMessage{App=app,AppId=path,ClientPath=path,Title=title,Body=unread>0?"客户端显示 "+unread+" 条未读":"打开"+app+"查看",At=DateTimeOffset.Now,Key=Guid.NewGuid().ToString()});
    }
    public void Dispose(){disposed=true;timer.Dispose();}
}
}
