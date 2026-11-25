using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pulse {
internal sealed partial class Dashboard {
    static readonly string[] workTabs={"会话","消息","音乐","暂存"};
    static readonly string[] workGlyphs={"\uE8BD","\uE8F2","\uEC4F","\uE8B7"};
    const int rowTop=104,rowHeight=40;
    int PageCount{get{return Math.Max(1,(DetailCount+rowsPerPage-1)/rowsPerPage);}}
    static void Surface(Graphics g,RectangleF r,int radius,Color color){
        using(var shape=Shape(r,radius))using(var brush=new SolidBrush(color))g.FillPath(brush,shape);
    }
    static void WorkText(Graphics g,string text,float x,float y,float w,float h,int size,Color color,bool bold=false,bool glyph=false,bool center=false){
        if(w<=0 || h<=0)return;
        using(var f=new Font(glyph?"Segoe MDL2 Assets":"Microsoft YaHei UI",size,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel))
        using(var fmt=new StringFormat{LineAlignment=StringAlignment.Center,Alignment=center?StringAlignment.Center:StringAlignment.Near,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap})
        using(var b=new SolidBrush(color))g.DrawString(text,f,b,new RectangleF(x,y,w,h),fmt);
    }
    Rectangle TabRect(int i){if(ShowFileTab)return new Rectangle(10+49*i,19,48,31);return new Rectangle(12+63*i,19,61,31);}
    int WorkTabAt(Point p){
        for(int i=0;i<(ShowFileTab?4:3);i++)if(TabRect(i).Contains(p))return i;
        return -1;
    }
    int RowAt(Point p){
        if(!expanded || resizeFrames.Enabled || section==Section.Music || p.X<15 || p.X>Width-15 || p.Y<rowTop || p.Y>=rowTop+rowHeight*rowsPerPage)return -1;
        int idx=page*rowsPerPage+(p.Y-rowTop)/rowHeight;return idx<DetailCount?idx:-1;
    }
    int FooterButtonAt(Point p){
        if(section==Section.Music || PageCount<2 || p.Y<74 || p.Y>100)return 0;
        if(p.X>=Width-98 && p.X<Width-72)return -1;
        if(p.X>=Width-42 && p.X<=Width-16)return 1;return 0;
    }
    static Rectangle MusicRect(int w,int i){return new Rectangle(w-164+46*i,134,38,36);}
    int MusicButtonAt(Point p){
        if(section!=Section.Music || desktop==null || desktop.Music==null)return 0;
        for(int i=0;i<3;i++)if(MusicRect(Width,i).Contains(p))return i+1;return 0;
    }
    static Rectangle ReadAction(int w,int y){return new Rectangle(w-56,y+3,32,31);}
    Rectangle ClearMessagesButton{get{return new Rectangle(Width-(PageCount>1?166:78),75,58,24);}}
    bool CanClearMessages{get{return expanded && section==Section.Messages && DetailCount>0;}}
    void WorkbenchClick(Point p){
        if(resizeFrames.Enabled || clearClock.IsRunning)return;
        int tab=WorkTabAt(p),dir=FooterButtonAt(p),music=MusicButtonAt(p),idx=RowAt(p);
        if(tab>=0){SelectSection((Section)tab);page=0;hoverOpen=false;}
        else if(section==Section.Processes){
            for(int i=0;i<3;i++)if(ProcessColumn(Width,i,74,27).Contains(p)){ranking.Open(i);break;}
            if(ProcessRail(Width).Contains(p)){
                ScrollRail(p.Y);
            }
        }
        else if(CanClearMessages && ClearMessagesButton.Contains(p)){
            BeginClearMotion(p);
        }
        else if(dir!=0)StepPage(dir);
        else if(music!=0)ControlMusic(pressedSong,music-2);
        else if(idx>=0 && section==Section.Files && pressedFile==shelf[idx] && ReadAction(Width,rowTop+(idx%rowsPerPage)*rowHeight).Contains(p)){
            shelf.RemoveAt(idx);page=Math.Min(page,PageCount-1);
        }
        else if(idx>=0 && section==Section.Files && pressedFile==shelf[idx])CopyShelf(pressedFile);
        else if(idx>=0 && section==Section.AI && pressedNotice!=null){
            if(p.X>=Width-49)agents.ToggleFollow(pressedNotice);else OpenNotice(pressedNotice);
        }
        else if(idx>=0 && section==Section.Messages && pressedMessage!=null){
            if(ReadAction(Width,rowTop+(idx%rowsPerPage)*rowHeight).Contains(p)){
                if(!desktop.IsRead(pressedMessage))desktop.MarkRead(pressedMessage);
            }else OpenMessage(pressedMessage);
        }else if(section==Section.Messages && desktop!=null && desktop.NeedsAccess && p.Y>=110)desktop.RequestAccess();
        RefreshSurface();
    }
    void PaintWorkbench(Graphics g,Size size){
        int w=size.Width,added=w-widgetSize.Width;
        var saved=g.Save();g.SetClip(new Rectangle(0,0,Math.Max(0,added),68));
        g.TranslateTransform(added-detailWidth,0);
        Surface(g,new Rectangle(9,16,195,37),16,Color.FromArgb(132,27,32,39));
        for(int i=0;i<(ShowFileTab?4:3);i++){
            var r=TabRect(i);bool on=(int)section==i;var c=on?Color.FromArgb(32,47,49):Color.White;
            if(on)Surface(g,r,13,Color.FromArgb(231,208,237,230));
            if(ShowFileTab){WorkText(g,workTabs[i],r.X,r.Y,r.Width,r.Height,12,c,on,false,true);continue;}
            WorkText(g,workGlyphs[i],r.X+7,r.Y,14,r.Height,13,c,false,true,true);
            WorkText(g,workTabs[i],r.X+22,r.Y,34,r.Height,12,c,on,false,true);
        }
        g.Restore(saved);
        saved=g.Save();
        using(var outline=Shape(new RectangleF(1,1,w-2,size.Height-2),20))g.SetClip(outline);
        Surface(g,new Rectangle(7,70,w-14,workHeight-77),16,Color.FromArgb(206,25,29,35));
        var fg=Color.FromArgb(242,245,246);var dim=Color.FromArgb(181,190,197);
        if(section==Section.Processes){PaintProcesses(g,w,fg,dim);g.Restore(saved);return;}
        string heading=section==Section.AI?"会话活动":section==Section.Messages?"通知回看":section==Section.Files?"内容暂存":"正在播放";
        WorkText(g,heading,19,73,90,28,14,fg,true);
        if(section==Section.Files)WorkText(g,shelfPreview?"松开以暂存":shelf.Count+" 项 · 拖出使用",108,73,200,28,12,dim);
        else if(section!=Section.Music)WorkText(g,(clearClock.IsRunning || closingClearedPanel) && section==Section.Messages?clearCount:DetailCount+" 条"+(section==Section.Messages && desktop!=null?" · "+desktop.UnreadCount+" 未读":""),108,73,140,28,11,dim);
        if(CanClearMessages && !resizeFrames.Enabled){
            var clear=ClearMessagesButton;
            bool over=clear.Contains(PointToClient(System.Windows.Forms.Cursor.Position));
            Surface(g,clear,10,Color.FromArgb(over?43:19,222,231,235));
            WorkText(g,"清空",clear.X,clear.Y,clear.Width,clear.Height,12,over?fg:dim,false,false,true);
        }
        page=Math.Min(page,PageCount-1);
        if(PageCount>1){
            WorkText(g,"\uE76B",w-98,74,26,26,11,fg,false,true,true);
            WorkText(g,(page+1)+"/"+PageCount,w-72,74,30,26,11,dim,false,false,true);
            WorkText(g,"\uE76C",w-42,74,26,26,11,fg,false,true,true);
        }
        if(clearClock.IsRunning && section==Section.Messages)PaintClearRows(g,w);
        else if(section==Section.Music)PaintPlayer(g,w,fg,dim);
        else if(DetailCount==0 && !closingClearedPanel){
            string empty=section==Section.Files?"将文件、选中文字或链接拖到这里":section==Section.AI?"暂无会话记录":desktop==null?"正在连接":desktop.MessageStatus;
            WorkText(g,empty,18,130,w-36,52,14,dim,false,false,true);
        }else for(int i=0;i<rowsPerPage && page*rowsPerPage+i<DetailCount;i++){
            int idx=page*rowsPerPage+i,y=rowTop+i*rowHeight;
            if(section==Section.Files)PaintShelfRow(g,w,idx,y,fg,dim);else PaintActivityRow(g,w,idx,y,fg,dim);
        }
        g.Restore(saved);
    }
    void PaintActivityRow(Graphics g,int w,int idx,int y,Color fg,Color dim){
            bool hovered=RowAt(PointToClient(System.Windows.Forms.Cursor.Position))==idx;
            Surface(g,new Rectangle(14,y,w-28,37),9,Color.FromArgb(hovered?37:14,255,255,255));
            bool ai=section==Section.AI;
            var run=ai?agents.Rows[idx]:null;var msg=ai?null:desktop.Messages[idx];
            string state=ai?run.state:desktop.IsRead(msg)?"已读":"未读";
            var accent=state=="等你确认" || state=="失败"?aiAmber:state=="执行中" || state=="未读"?aiMint:dim;
            WorkText(g,ai?"\uE8BD":"\uE8F2",21,y,23,37,15,accent,false,true,true);
            if(ai){
                WorkText(g,run.title,50,y,w-235,20,13,fg,true);
                WorkText(g,run.source,50,y+18,w-212,18,12,dim);
                WorkText(g,state,w-181,y,62,37,12,accent,false,false,true);
                WorkText(g,agents.Follows(run)?"★":"☆",w-49,y,32,37,17,agents.Follows(run)?aiAmber:dim,false,false,true);
                WorkText(g,run.Elapsed(AgentFeed.Now),w-115,y,60,37,12,fg,false,false,true);
            }else{
                bool unread=!desktop.IsRead(msg);
                string title=msg.ClientPath!=null?msg.App+"提醒":msg.Title;
                string summary=msg.ClientPath!=null?(msg.Source=="新增未读消息"?msg.Body:msg.Source):msg.App+" · "+(string.IsNullOrEmpty(msg.Body)?msg.Source:msg.Body);
                WorkText(g,title,50,y,w-205,21,14,unread?fg:dim,unread);
                WorkText(g,summary,50,y+19,w-205,17,12,Color.FromArgb(205,213,219));
                var received=msg.Received.LocalDateTime;
                WorkText(g,received.ToString(received.Date==DateTime.Today?"HH:mm:ss":"MM/dd HH:mm"),w-145,y,85,37,12,dim,false,false,true);
                var action=ReadAction(w,y);
                if(unread && hovered){
                    bool over=action.Contains(PointToClient(System.Windows.Forms.Cursor.Position));
                    Surface(g,action,12,Color.FromArgb(over?65:32,157,235,221));
                    WorkText(g,"\uE73E",action.X,action.Y,action.Width,action.Height,15,aiMint,false,true,true);
                }else if(unread)Dot(g,action.X+13,y+16,6,aiMint);
            }
    }
    static Rectangle ProcessColumn(int w,int metric,int y,int h){return new Rectangle(w-291+metric*85,y,83,h);}
    static Rectangle ProcessRail(int w){return new Rectangle(w-24,105,12,114);}
    void ScrollRail(int y){
        var rail=ProcessRail(Width);
        ranking.Scroll((int)Math.Round((y-rail.Top)/(double)rail.Height*Math.Max(0,ranking.Sorted.Length-Ranking.VisibleRows))-ranking.Offset);
        RefreshSurface();
    }
    static string ProcessPercent(double value){return double.IsNaN(value)?"—":value.ToString("0.0")+"%";}
    void PaintProcesses(Graphics g,int w,Color fg,Color dim){
        if(ranking==null)return;
        WorkText(g,"进程活动",19,73,82,28,14,fg,true);
        WorkText(g,ranking.Status,102,73,112,28,12,dim);
        var colors=new[]{aiMint,Color.FromArgb(220,201,255),aiAmber};
        for(int i=0;i<3;i++){
            var col=ProcessColumn(w,i,74,27);
            if(i==ranking.Metric)Surface(g,new Rectangle(col.X+8,col.Y+2,col.Width-16,23),10,Color.FromArgb(36,224,237,236));
            WorkText(g,ids[i]+(i==ranking.Metric?" ↓":""),col.X,col.Y,col.Width,col.Height,12,i==ranking.Metric?colors[i]:dim,true,false,true);
        }
        for(int i=0;i<Ranking.VisibleRows && ranking.Offset+i<ranking.Sorted.Length;i++){
            int y=104+i*29;var row=ranking.Sorted[ranking.Offset+i];
            if(i%2==0)Surface(g,new Rectangle(14,y,w-42,28),8,Color.FromArgb(15,255,255,255));
            var icon=ranking.ImageFor(row);if(icon!=null)g.DrawImage(icon,22,y+5,18,18);
            WorkText(g,row.name,48,y,w-422,28,13,fg,true);
            WorkText(g,row.pid.ToString(),w-373,y,72,28,11,dim);
            string ram=row.memory>=1073741824?(row.memory/1073741824d).ToString("0.00")+" GiB":(row.memory/1048576d).ToString("0.0")+" MiB";
            for(int j=0;j<3;j++){
                var col=ProcessColumn(w,j,y,28);
                WorkText(g,j==0?ProcessPercent(row.cpu):j==1?ram:ProcessPercent(row.gpu),col.X,y,col.Width,28,12,j==ranking.Metric?colors[j]:fg,false,false,true);
            }
        }
        if(ranking.Sorted.Length>Ranking.VisibleRows){
            var rail=ProcessRail(w);int h=Math.Max(13,rail.Height*Ranking.VisibleRows/ranking.Sorted.Length);
            int y=rail.Y+(rail.Height-h)*ranking.Offset/(ranking.Sorted.Length-Ranking.VisibleRows);
            Surface(g,new Rectangle(rail.X+5,y,3,h),1,Color.FromArgb(150,194,207,210));
        }
    }
    void PaintPlayer(Graphics g,int w,Color fg,Color dim){
        var song=desktop==null?null:desktop.Music;
        Surface(g,new Rectangle(19,110,82,82),14,Color.FromArgb(48,200,180,237));
        if(song!=null && desktop.Cover!=null){
            var state=g.Save();using(var clip=Shape(new Rectangle(19,110,82,82),14))g.SetClip(clip,CombineMode.Intersect);
            g.DrawImage(desktop.Cover,19,110,82,82);g.Restore(state);
        }else WorkText(g,"\uEC4F",19,110,82,82,31,Color.FromArgb(220,201,255),false,true,true);
        WorkText(g,song==null?"暂无播放内容":song.Title,116,115,w-291,34,17,fg,true);
        WorkText(g,song==null?(desktop==null?"正在连接":desktop.MusicStatus):song.Artist,116,151,w-291,27,12,dim);
        for(int i=0;i<3;i++){
            var r=MusicRect(w,i);bool enabled=song!=null && (i==0?song.Previous:i==1?song.Toggle:song.Next);
            Surface(g,r,16,Color.FromArgb(enabled && i==1?64:20,225,235,236));
            WorkText(g,i==0?"\uE892":i==2?"\uE893":song!=null && song.Playing?"\uE769":"\uE768",r.X,r.Y,r.Width,r.Height,16,enabled?fg:Color.Gray,false,true,true);
        }
        using(var rail=new SolidBrush(Color.FromArgb(52,255,255,255)))g.FillRectangle(rail,20,206,w-40,3);
        if(song!=null && song.Duration>0)using(var b=new SolidBrush(aiMint))g.FillRectangle(b,20,206,(float)((w-40)*song.Progress(DateTimeOffset.Now)),3);
    }
}
}
