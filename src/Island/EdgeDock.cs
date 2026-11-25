using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pulse {
internal sealed partial class Dashboard {
    // -1 = left/top, +1 = right/bottom. Each axis can latch independently.
    int dockX,dockY;
    Point dockTarget;
    Rectangle dockArea;
    int spread,spreadFrom,dragInset;
    const int railWidth=compactHeight,railRow=56,railHeight=compactHeight+3*railRow+28;
    float side,sideFrom,resizeSideFrom;
    bool railOnRight;
    int SideNarrow{get{return (int)Math.Round((widgetSize.Width-railWidth)*side);}}
    int SideTall{get{return (int)Math.Round((railHeight-compactHeight)*side);}}
    float SideGoal{get{return dockX!=0 && !expanded?1:0;}}
    int RestSpread{get{return dockY!=0 && dockX==0?18:0;}}
    static float Mix(float start,float end,float t){return start+(end-start)*t;}
    readonly Stopwatch pointerClock=new Stopwatch();
    Point lastPointer;
    double impact,contactDelay;
    bool landing;
    void BeginDockDrag(Point pointer){
        lastPointer=pointer;pointerClock.Restart();
    }

    int BodyWidth{get{return Width-spread+SideNarrow;}}
    Rectangle SpreadFrame(Point body,int width,int height,Rectangle area){
        int w=width+spread-SideNarrow;
        int h=height+SideTall;
        int x=body.X-(dockX<0?0:dockX>0?spread:spread/2);
        if(railOnRight)x+=SideNarrow;
        x=Math.Max(area.Left,Math.Min(x,area.Right-w));
        int y=dockY>0?area.Bottom-h:Math.Max(area.Top,Math.Min(body.Y,area.Bottom-h));
        return new Rectangle(x,y,w,h);
    }
    Point BodyOrigin(Rectangle r){
        return new Point(r.Left+(dockX<0?0:dockX>0?spread:spread/2)-(railOnRight?SideNarrow:0),r.Top);
    }
    PointF dockFrom;
    float fromLeft,fromTop,fromRight,fromBottom,joinLeft,joinTop,joinRight,joinBottom;
    readonly Timer dockFrames=new Timer{Interval=15};
    readonly Stopwatch dockTime=new Stopwatch();

    static int SnapAxis(int raw,int min,int max,int held){
        if(held<0 && raw-min<=29)return -1;
        if(held>0 && max-raw<=29)return 1;
        if(raw-min<=6)return -1;
        if(max-raw<=6)return 1;
        return 0;
    }
    static int DockCoordinate(int raw,int low,int high,int edge){
        return edge<0?low:edge>0?high:raw;
    }
    Point CompactDockLocation(Rectangle frame,Rectangle area){
        return new Point(dockX<0?area.Left:dockX>0?area.Right-widgetSize.Width:frame.Right-spread/2-widgetSize.Width,
            dockY>0?area.Bottom-widgetSize.Height:frame.Top);
    }
    void RestoreDock(){
        var a=Screen.FromControl(this).WorkingArea;
        dockX=Left==a.Left?-1:Right==a.Right?1:0;dockY=Top==a.Top?-1:Bottom==a.Bottom?1:0;
        railOnRight=dockX>0;SetDockShape(1);side=SideGoal;spread=RestSpread;Bounds=SpreadFrame(Location,widgetSize.Width,widgetSize.Height,a);
    }
    void SetDockShape(float t){
        joinLeft=fromLeft+((dockX<0?1:0)-fromLeft)*t;
        joinRight=fromRight+((dockX>0?1:0)-fromRight)*t;
        joinTop=fromTop+((dockY<0?1:0)-fromTop)*t;
        joinBottom=fromBottom+((dockY>0?1:0)-fromBottom)*t;
    }
    RectangleF GlassBounds(Size size){
        // Extend the contacting arc beyond the visible pixels so its rim merges into the screen edge.
        return RectangleF.FromLTRB(1-23*joinLeft,1-23*joinTop,size.Width-1+23*joinRight,size.Height-1+23*joinBottom);
    }
    GraphicsPath DockOutline(Size size,RectangleF rect){
        float contact=Math.Max(joinTop,joinBottom);
        if(contact<.001f || spread==0)return Shape(rect,20);
        float w=size.Width,h=size.Height;
        // As contact widens, the opposite surface moves toward the edge.
        // Deform the glass only; the controls stay on their native pixel grid.
        float squeeze=Math.Min(2f,h*spread/(float)Math.Max(1,size.Width))*contact;
        float lip=Math.Min(spread*.20f,5)*contact;
        const float radius=20;
        float l=rect.Left+lip*(1-joinLeft),r=rect.Right-lip*(1-joinRight);
        float top=1-3*contact,bottom=h-1-squeeze;
        float shoulder=top+radius;
        float tipLeft=(l+radius)*(1-contact)-contact;
        float tipRight=(r-radius)*(1-contact)+(w+1)*contact;
        float bend=radius*.55f*(1-contact);
        var p=new GraphicsPath();
        p.StartFigure();p.AddLine(tipLeft,top,tipRight,top);
        p.AddBezier(tipRight,top,tipRight+bend,top,r,shoulder-radius*.72f,r,shoulder);
        p.AddLine(r,shoulder,r,bottom-radius);
        p.AddArc(r-2*radius,bottom-2*radius,2*radius,2*radius,0,90);
        p.AddLine(r-radius,bottom,l+radius,bottom);
        p.AddArc(l,bottom-2*radius,2*radius,2*radius,90,90);
        p.AddLine(l,bottom-radius,l,shoulder);
        p.AddBezier(l,shoulder,l,shoulder-radius*.72f,tipLeft-bend,top,tipLeft,top);p.CloseFigure();
        if(joinBottom>joinTop)using(var mirror=new Matrix(1,0,0,-1,0,h))p.Transform(mirror);
        return p;
    }
    void MoveWithDock(Point pointer,bool release){
        var a=Screen.FromPoint(pointer).WorkingArea;
        dockArea=a;int bodyWidth=BodyWidth;
        int x=pointer.X-dragOffset.X+dragInset,y=pointer.Y-dragOffset.Y;
        int horizontal=SnapAxis(x,a.Left,a.Right-bodyWidth,dockX);
        int vertical=SnapAxis(y,a.Top,a.Bottom-Height,dockY);
        var target=new Point(DockCoordinate(x,a.Left,a.Right-bodyWidth,horizontal),DockCoordinate(y,a.Top,a.Bottom-Height,vertical));
        double dt=Math.Max(.008,pointerClock.Elapsed.TotalSeconds);
        double normalSpeed=vertical!=0?(pointer.Y-lastPointer.Y)*vertical/dt:0;
        if(pointer!=lastPointer){lastPointer=pointer;pointerClock.Restart();}
        if(horizontal!=dockX || vertical!=dockY){
            landing=vertical!=0 && vertical!=dockY;
            impact=landing?Math.Min(1200,Math.Max(0,normalSpeed)):0;
            contactDelay=landing?Math.Min(65,Math.Abs(target.Y-Top)*4):0;
            dockFrom=BodyOrigin(Bounds);spreadFrom=spread;sideFrom=side;
            fromLeft=joinLeft;fromRight=joinRight;fromTop=joinTop;fromBottom=joinBottom;
            dockX=horizontal;dockY=vertical;if(horizontal!=0)railOnRight=horizontal>0;dockTime.Restart();dockFrames.Start();
        }
        dockTarget=target;
        if(dockFrames.Enabled)DockFrame();else Location=SpreadFrame(target,bodyWidth,Height-SideTall,a).Location;
        if(release){
            anchorRight=target.X+bodyWidth;anchorTop=target.Y;
            widgetLocation=new Point(dockX<0?a.Left:anchorRight-widgetSize.Width,dockY>0?a.Bottom-widgetSize.Height:target.Y);SaveWidget();
            if(expanded && dockX!=0)SetExpanded(false);
        }
    }
    void DockFrame(){
        bool animate=System.Windows.SystemParameters.ClientAreaAnimation;
        double ms=animate?dockTime.Elapsed.TotalMilliseconds:500;
        int bodyWidth=BodyWidth,bodyHeight=Height-SideTall;
        float phase=(float)Math.Min(1,ms/360);
        side=Mix(sideFrom,SideGoal,phase*phase*(3-2*phase));
        double travel=Math.Min(1,ms/(landing?Math.Max(1,contactDelay):100));
        float ease=(float)(1-Math.Pow(1-travel,3));
        double elapsed=Math.Max(0,ms-contactDelay)/1000;
        // Critical damping with resting displacement and a bounded normal impact impulse.
        double omega=20,decay=Math.Exp(-omega*elapsed);
        double displacement=(spreadFrom-RestSpread)*(1+omega*elapsed)*decay;
        double impulse=impact*.30*elapsed*decay;
        spread=Math.Max(0,(int)Math.Round(RestSpread+displacement+impulse));
        SetDockShape(landing?(float)(1-(1+omega*elapsed)*decay):ease);
        int x=dockTarget.X,y=dockTarget.Y;
        if(fromLeft!= (dockX<0?1:0) || fromRight!=(dockX>0?1:0))x=(int)Math.Round(dockFrom.X+(x-dockFrom.X)*ease);
        if(fromTop!=(dockY<0?1:0) || fromBottom!=(dockY>0?1:0))y=(int)Math.Round(dockFrom.Y+(y-dockFrom.Y)*ease);
        if(ms>=contactDelay+400){spread=RestSpread;SetDockShape(1);dockFrames.Stop();dockTime.Reset();}
        if(compact && Visible)PresentFrame(SpreadFrame(new Point(x,y),bodyWidth,bodyHeight,dockArea),true);
    }
    void FinishDock(){
        if(!dockFrames.Enabled)return;
        int bodyWidth=BodyWidth,bodyHeight=Height-SideTall;side=SideGoal;dockFrames.Stop();dockTime.Reset();SetDockShape(1);spread=RestSpread;
        Bounds=SpreadFrame(dockTarget,bodyWidth,bodyHeight,dockArea);
    }
}
}
