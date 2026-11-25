using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pulse {
internal sealed partial class Dashboard {
    const string Settings=@"Software\ChengJing\Pulse";
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    const int compactHeight=56, expandedHeader=70;
    int HeaderHeight{get{return Math.Min(ClientSize.Height,expandedHeader);}}
    const int tickerWidth=166, detailWidth=218, metricsWidth=252;
    readonly Size widgetSize=new Size(metricsWidth+tickerWidth,compactHeight);
    int AgentWidth{get{return ClientSize.Width-metricsWidth;}}
    GlassMenu options;
    Ranking ranking;
    AgentTicker agents;
    DesktopActivities desktop;
    enum Section { AI, Messages, Music, Files, Processes }
    Section section,shownSection,previewSection,pressedSection;
    long previewUntil;
    int wheelRemainder;
    void ActivityWheel(object sender,MouseEventArgs e){
        if(clearClock.IsRunning || !compact || !ClientRectangle.Contains(e.Location) || (!expanded && e.X>=AgentWidth))return;
        wheelRemainder+=e.Delta;
        int steps=wheelRemainder/SystemInformation.MouseWheelScrollDelta;
        wheelRemainder%=SystemInformation.MouseWheelScrollDelta;
        if(steps==0)return;
        if(expanded && section==Section.Processes){ranking.Scroll(-steps*3);RefreshSurface();return;}
        if(expanded){StepPage(-Math.Sign(steps));return;}
        int next=((int)shownSection-steps)%3;
        previewSection=(Section)((next+3)%3);previewUntil=AgentFeed.Now+10;
        RefreshSurface();
    }
    IslandMessage displayedMessage,pressedMessage;
    Song displayedSong,pressedSong;
    readonly Timer resizeFrames=new Timer{Interval=15};
    readonly System.Diagnostics.Stopwatch resizeTime=new System.Diagnostics.Stopwatch();
    int fromWidth,targetWidth,anchorRight,anchorTop,page;
    const int workHeight=235, rowsPerPage=3;
    bool expanded,hoverOpen,suppressHover;
    GlassLens wideLens,lastLens;
    void PrepareAgents(){
        if(agents==null)agents=new AgentTicker(run=>{if(desktop!=null)desktop.AddAgent(run);if(desktop==null || !desktop.Focusing || run.state=="等你确认")tray.ShowBalloonTip(4000,run.source+" · "+run.state,run.title,ToolTipIcon.Info);});
    }
    void OpenAgents(){if(!compact)SwitchMode(true);PrepareAgents();SetExpanded(true);SelectSection(Section.AI);page=0;}
    void SetExpanded(bool value){
        if(expanded==value)return;
        if(value){closingClearedPanel=false;usedWorkbench=true;}
        if(!value){summoned=false;EndClearMotion();StopRanking();}
        if(!resizeFrames.Enabled){anchorRight=Right;if(!expanded)anchorTop=Top;}
        expanded=value;page=0;if(value)section=desktop!=null && desktop.HasUnread && shownSection==Section.AI && displayedActivity==null?Section.Messages:shownSection;
        fromWidth=Width;targetWidth=widgetSize.Width+(value?detailWidth:0);
        resizeTime.Restart();resizeFrames.Start();ResizeFrame();
    }
    void ResizeFrame(){
        var a=Screen.FromControl(this).WorkingArea;
        double t=System.Windows.SystemParameters.ClientAreaAnimation?Math.Min(1,resizeTime.Elapsed.TotalMilliseconds/185):1;
        int w=(int)Math.Round(targetWidth+(fromWidth-targetWidth)*Math.Pow(1-t,3));
        int h=widgetSize.Height+(w-widgetSize.Width)*(workHeight-widgetSize.Height)/detailWidth;
        var frame=new Rectangle(Math.Max(a.Left,Math.Min(anchorRight-w,a.Right-w)),Math.Max(a.Top,Math.Min(anchorTop,a.Bottom-h)),w,h);
        if(t>=1){resizeFrames.Stop();resizeTime.Stop();}
        if(Visible)PresentFrame(frame,true);
    }
    void StepPage(int direction){
        int count=DetailCount;
        if(count>0){
            int pages=(count+rowsPerPage-1)/rowsPerPage;page=(page+direction+pages)%pages;RefreshSurface();
        }
    }
    int DetailCount{get{return section==Section.Files?shelf.Count:section==Section.AI?(agents==null?0:agents.Rows.Length):section==Section.Messages && desktop!=null?desktop.Messages.Count:0;}}
    AgentRun pressedNotice,displayedActivity,pressedActivity;
    bool InActivityBody(Point p){return p.Y<HeaderHeight && p.X>=AgentWidth-tickerWidth && p.X<AgentWidth-23;}
    AgentRun NoticeAt(Point p){
        int index=RowAt(p);
        return section==Section.AI && agents!=null && index>=0 && index<agents.Rows.Length?agents.Rows[index]:null;
    }
    void OpenNotice(AgentRun notice){
        if(notice.sessionLink!=null){
            try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(notice.sessionLink.AbsoluteUri){UseShellExecute=true});return;}
            catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
        }
        tips.Show("无法打开此会话，请检查来源应用。",this,12,Height,2400);
    }
    Point pressPosition;
    bool moved,scrollingProcesses;
    GlassLens lens;
    readonly Timer lensFrames=new Timer{Interval=120};
    readonly NotifyIcon tray=new NotifyIcon();
    bool startupEnabled;
    Point widgetLocation,dragOffset;
    bool compact,pinned,dragging,quitting;

    void InitializeWidget(){
        InitializeShelf();
        resizeFrames.Tick+=(s,e)=>ResizeFrame();
        wideLens=new GlassLens(new Size(widgetSize.Width+detailWidth,workHeight));
        Deactivate+=(s,e)=>{if(capturingShortcut){capturingShortcut=false;tips.Hide(this);}};
        MouseWheel+=ActivityWheel;
        MouseLeave+=(s,e)=>{suppressHover=false;if(hoverOpen && !Bounds.Contains(Cursor.Position)){hoverOpen=false;SetExpanded(false);}};
        Shown+=(s,e)=>{desktop=new DesktopActivities();};
        Shown+=(s,e)=>BeginInvoke((Action)(()=>{if(!IsDisposed){PrepareAgents();if(Environment.GetCommandLineArgs().Contains("--ai"))OpenAgents();}}));
        lens=new GlassLens(widgetSize);
        lensFrames.Tick+=(s,e)=>{AdvanceClearMotion();if(compact && Visible && !resizeFrames.Enabled)PresentWidget();};

        Shown+=async(s,e)=>{if(ranking==null){ranking=new Ranking();await ranking.UpdateRows();}};
        StartPosition=FormStartPosition.Manual;
        var area=Screen.PrimaryScreen.WorkingArea;
        widgetLocation=new Point(area.Right-(compact?ClientSize.Width:widgetSize.Width)-20,area.Top+20);
        using(var k=Registry.CurrentUser.OpenSubKey(Settings)){
            if(k!=null){
                widgetLocation=new Point(Convert.ToInt32(k.GetValue("X",widgetLocation.X)),Convert.ToInt32(k.GetValue("Y",widgetLocation.Y)));
                shortcut=(Keys)Convert.ToInt32(k.GetValue("SummonShortcut",(int)shortcut));
                pinned=Convert.ToInt32(k.GetValue("Pinned",0))!=0;
            }
        }
        using(var k=Registry.CurrentUser.OpenSubKey(RunKey)){
            string command=k==null?null:k.GetValue("ChengJingPulse") as string;
            string oldPath=System.IO.Path.Combine(Application.StartupPath,"Pulse.exe");
            startupEnabled=string.Equals(command,QuotePath(Application.ExecutablePath),StringComparison.OrdinalIgnoreCase);
            if(!startupEnabled && string.Equals(command,QuotePath(oldPath),StringComparison.OrdinalIgnoreCase))SetStartup(true);
        }
        options=new GlassMenu(()=>SwitchMode(!compact),()=>{
            pinned=!pinned; TopMost=compact && pinned;  SaveWidget();
        },()=>SetStartup(!startupEnabled),OpenAgents,CaptureShortcut,ToggleFocus,()=>{quitting=true;Close();});
        tray.Icon=SystemIcons.Information;tray.Text="性能监视器";tray.Visible=true;
        tray.MouseClick+=(s,e)=>{if(e.Button==MouseButtons.Right)OpenOptions(Cursor.Position);else if(e.Button==MouseButtons.Left){if(!compact)SwitchMode(true);WindowState=FormWindowState.Normal;Show();BringToFront();Activate();}};
        MouseUp+=(s,e)=>{if(e.Button==MouseButtons.Right)OpenOptions(Cursor.Position);};
        MouseDown+=(s,e)=>{if(compact && e.Button==MouseButtons.Left && e.Clicks==1){
            if(e.Y<(expanded?32:21) && e.X>Width-31){OpenOptions(PointToScreen(new Point(Width-28,28)));return;}
            if(expanded && section==Section.Processes && ProcessRail(Width).Contains(e.Location)){
                scrollingProcesses=true;Capture=true;ScrollRail(e.Y);return;
            }
            int fileIndex=section==Section.Files?RowAt(e.Location):-1;
            pressedFile=fileIndex>=0?shelf[fileIndex]:null;
            pressedFocus=focusCard && InActivityBody(e.Location);pressedSection=shownSection;pressedMessage=null;pressedSong=null;
            if(InActivityBody(e.Location)){pressedMessage=displayedMessage;pressedSong=displayedSong;}
            else if(expanded && e.Y>=HeaderHeight && desktop!=null){
                if(section==Section.Messages && RowAt(e.Location)>=0){
                    int idx=RowAt(e.Location);
                    if(idx<desktop.Messages.Count)pressedMessage=desktop.Messages[idx];
                }
                if(section==Section.Music)pressedSong=desktop.Music;
            }
            pressedActivity=InActivityBody(e.Location)?displayedActivity:null;pressedNotice=NoticeAt(e.Location);pressPosition=Cursor.Position;moved=false;dragOffset=e.Location;dragging=true;Capture=true;
        }};
        MouseCaptureChanged+=(s,e)=>{if(!Capture)scrollingProcesses=false;};
        MouseMove+=(s,e)=>{
            if(scrollingProcesses){ScrollRail(e.Y);return;}
            if(!dragging && compact && !expanded && !suppressHover && agents!=null && agents.LiveCount>1 && e.X>=AgentWidth-23 && e.X<AgentWidth){hoverOpen=true;SetExpanded(true);}
            if(compact && !dragging){var actionable=(expanded && (RowAt(e.Location)>=0 || (CanClearMessages && ClearMessagesButton.Contains(e.Location)) || WorkTabAt(e.Location)>=0 || FooterButtonAt(e.Location)!=0 || MusicButtonAt(e.Location)!=0)) || e.Y<HeaderHeight;
                Cursor=actionable?Cursors.Hand:Cursors.Default;return;}
            if(!dragging || !compact)return;
            var pos=Cursor.Position;var threshold=SystemInformation.DragSize;
            moved|=Math.Abs(pos.X-pressPosition.X)>=threshold.Width/2 || Math.Abs(pos.Y-pressPosition.Y)>=threshold.Height/2;
            if(moved && pressedFile!=null){
                var path=pressedFile;pressedFile=null;dragging=false;Capture=false;
                int rowY=rowTop+(dragOffset.Y-rowTop)/rowHeight*rowHeight;
                if(!ReadAction(Width,rowY).Contains(dragOffset))ExportFile(path);
                return;
            }
            if(moved)Location=new Point(pos.X-dragOffset.X,pos.Y-dragOffset.Y);
        };
        MouseUp+=(s,e)=>{
            if(scrollingProcesses && e.Button==MouseButtons.Left){scrollingProcesses=false;Capture=false;return;}
            if(!dragging || e.Button!=MouseButtons.Left)return;
            dragging=false;Capture=false;
            if(moved){Location=Constrain(Location);anchorRight=Right;anchorTop=Top;widgetLocation=new Point(Right-widgetSize.Width,Top);SaveWidget();}
            else if(ClientRectangle.Contains(e.Location)){
                if(expanded && (e.Y>=HeaderHeight || e.X<AgentWidth-tickerWidth)){WorkbenchClick(e.Location);return;}
                if(e.X>=AgentWidth)OpenRanking(Math.Min(2,(e.X-AgentWidth)*3/metricsWidth));
                else if(InActivityBody(e.Location)){
                    if(pressedFocus && desktop!=null)desktop.PauseOrResume(AgentFeed.Now);else if(pressedMessage!=null)OpenMessage(pressedMessage);
                    else if(pressedSong!=null)ControlMusic(pressedSong,0);
                    else if(pressedActivity!=null)OpenNotice(pressedActivity);
                    else if(pressedSection!=Section.AI){SetExpanded(true);SelectSection(pressedSection);page=0;RefreshSurface();}
                    else OpenAgents();
                }else{hoverOpen=false;suppressHover=true;SetExpanded(!expanded);}
            }
        };
        FormClosing+=(s,e)=>{if(!compact && !quitting && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;SwitchMode(true);}};
        FormClosed+=(s,e)=>{if(desktop!=null)desktop.Dispose();if(agents!=null)agents.Dispose();EndClearMotion();resizeFrames.Dispose();ReleaseFaces();wideLens.Dispose();if(ranking!=null)ranking.Dispose();lensFrames.Dispose();lens.Dispose();tray.Visible=false;tray.Dispose();options.Close();};
        SwitchMode(true);
    }
    void StopRanking(){if(ranking!=null)ranking.Hide();TrackOutsideClicks(summoned);}
    void SelectSection(Section target){EndClearMotion();if(target!=Section.Processes)StopRanking();section=target;}
    void OpenRanking(int metric){
        if(ranking==null)ranking=new Ranking();
        hoverOpen=false;SetExpanded(true);SelectSection(Section.Processes);
        ranking.Open(metric);TrackOutsideClicks(true);RefreshSurface();
    }
    Point Constrain(Point point){
        var a=Screen.FromRectangle(new Rectangle(point,compact?ClientSize:widgetSize)).WorkingArea;
        return new Point(Math.Max(a.Left,Math.Min(point.X,a.Right-(compact?ClientSize.Width:widgetSize.Width))),Math.Max(a.Top,Math.Min(point.Y,a.Bottom-(compact?ClientSize.Height:widgetSize.Height))));
    }
    void SaveWidget(){
        using(var k=Registry.CurrentUser.CreateSubKey(Settings)){k.SetValue("X",widgetLocation.X);k.SetValue("Y",widgetLocation.Y);k.SetValue("Pinned",pinned?1:0);}
    }
    void SwitchMode(bool small){
        
        if(compact && !small){summoned=false;EndClearMotion();widgetLocation=new Point(Right-widgetSize.Width,Top);resizeFrames.Stop();expanded=false;hoverOpen=false;StopRanking();}
        compact=small;dragging=false;Capture=false;WindowState=FormWindowState.Normal;

        MinimumSize=Size.Empty;FormBorderStyle=small?FormBorderStyle.None:FormBorderStyle.Sizable;
        ShowInTaskbar=!small;TopMost=small && pinned;
        pause.Visible=range.Visible=!small;
        if(small){
            ClientSize=widgetSize;Location=Constrain(widgetLocation);

        }else{
            var a=Screen.FromPoint(widgetLocation).WorkingArea;
            MinimumSize=new Size(760,590);Size=new Size(Math.Min(a.Width,1004),Math.Min(a.Height,689));
            Location=new Point(a.Left+(a.Width-Width)/2,a.Top+(a.Height-Height)/2);
        }
        if(IsHandleCreated)RecreateHandle();
        tips.SetToolTip(this,small?"在收起的卡片上滚动切换 AI、消息和音乐，10 秒后恢复自动显示，箭头展开活动中心，可切换 AI、消息和音乐，单击通知打开会话，箭头或滚轮翻页，单击指标查看进程排行，拖动调整位置，右键菜单可打开详细趋势。":null);
        
        lensFrames.Enabled=small;
        RefreshSurface();
    }
    static string QuotePath(string path){return ((char)34)+path+((char)34);}
    void SetStartup(bool enabled){
        try{using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){
            if(enabled)key.SetValue("ChengJingPulse",QuotePath(Application.ExecutablePath));
            else key.DeleteValue("ChengJingPulse",false);
            startupEnabled=enabled;
        }}catch(UnauthorizedAccessException){MessageBox.Show("当前账户无法更改登录启动设置。",Text);}
    }
    void OpenOptions(Point position){
        using(var g=CreateGraphics())options.Show(position,g.DpiX,compact,pinned,startupEnabled,ShortcutLabel(),desktop!=null && desktop.Focusing);
    }
    void WidgetText(Graphics g,string text,float x,float y,float width,float height,float size){
        using(var font=new Font("Segoe UI",size,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var format=new StringFormat{FormatFlags=StringFormatFlags.NoWrap})
            g.DrawString(text,font,Brushes.White,new RectangleF(x,y,width,height),format);
    }
    void PaintWidget(Graphics g,Rectangle frame,bool resizing){
        int width=frame.Width,agentWidth=width-metricsWidth;
        var rect=new RectangleF(1,1,width-2,frame.Height-2);
        if(!clearClock.IsRunning && !flip.IsRunning && !resizing && (width==widgetSize.Width || width==widgetSize.Width+detailWidth)){
            lastLens=width==widgetSize.Width?lens:wideLens;
            lastLens.Capture(frame.Location);
        }
        if(lastLens!=null){
            var backdrop=g.Save();g.ScaleTransform(1,frame.Height/(float)lastLens.Height);
            lastLens.Paint(g,width);g.Restore(backdrop);
        }
        using(var path=Shape(rect,20))
        using(var tint=new LinearGradientBrush(rect,Color.FromArgb(94,71,78,88),Color.FromArgb(64,43,46,56),90))
        using(var rim=new LinearGradientBrush(rect,Color.FromArgb(95,248,252,255),Color.FromArgb(23,202,212,226),90))
        using(var edge=new Pen(rim,1f)){
            g.FillPath(tint,path);
            var state=g.Save();g.SetClip(path);
            Glow(g,-130,-76,395,158,Color.FromArgb(40,163,215,229));
            Glow(g,width-206,10,336,131,Color.FromArgb(36,200,180,224));
            Glow(g,width-385,-93,355,136,Color.FromArgb(27,252,251,246));
            g.Restore(state);g.DrawPath(edge,path);
        }
        int header=Math.Min(frame.Height,expandedHeader);
        float layout=(header-compactHeight)/(float)(expandedHeader-compactHeight);
        PaintTicker(g,agentWidth,header);
        if(frame.Height>expandedHeader)PaintWorkbench(g,frame.Size);
        using(var rule=new Pen(Color.FromArgb(48,255,255,255)))g.DrawLine(rule,agentWidth,13+3*layout,agentWidth,header-13-3*layout);
        var accents=new[]{Color.FromArgb(158,244,234),Color.FromArgb(220,201,255),Color.FromArgb(255,215,179)};
        var last=history.LastOrDefault();
        for(int i=0;i<3;i++){
            float x=agentWidth+13+i*79;
            Dot(g,x,13+layout,4,accents[i]);WidgetText(g,ids[i],x+8,6+layout,50,17,11);
            double v=last==null?double.NaN:last.values[i];
            WidgetText(g,double.IsNaN(v)?"—":v.ToString("0")+"%",x-2,19+2*layout,75,30,21+3*layout);
            using(var track=new SolidBrush(Color.FromArgb(74,255,255,255)))g.FillRectangle(track,x,header-10-4*layout,62,2);
            if(!double.IsNaN(v))using(var fill=new SolidBrush(accents[i]))g.FillRectangle(fill,x,header-10-4*layout,(float)(v*.62),2);
        }
        for(int j=0;j<3;j++)Dot(g,width-21+j*4,13+layout,2,Color.White);
    }
}
}









