using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Pulse {
internal sealed partial class Dashboard {
    readonly Stopwatch clearClock=new Stopwatch();
    Bitmap[] clearRows;
    string clearCount;
    bool closingClearedPanel;
    const int clearStagger=72, rowExit=260, exitWait=280;
    // Invert the Bezier time axis to match CSS cubic-bezier easing.
    static float MotionEase(double time,double x1,double y1,double x2,double y2){
        if(time<=0)return 0;if(time>=1)return 1;
        double low=0,high=1,t=0;
        for(int i=0;i<12;i++){
            t=(low+high)/2;double u=1-t;
            if(3*u*u*t*x1+3*u*t*t*x2+t*t*t<time)low=t;else high=t;
        }
        double v=1-t;return (float)(3*v*v*t*y1+3*v*t*t*y2+t*t*t);
    }
    void BeginClearMotion(Point click){
        bool animate=System.Windows.SystemParameters.ClientAreaAnimation;
        clearCount=DetailCount+" 条 · "+desktop.UnreadCount+" 未读";
        if(animate){
            int first=page*rowsPerPage;
            clearRows=new Bitmap[Math.Max(0,Math.Min(rowsPerPage,DetailCount-first))];
            for(int i=0;i<clearRows.Length;i++){
                var image=new Bitmap(Width,rowHeight,PixelFormat.Format32bppPArgb);clearRows[i]=image;
                using(var g=Graphics.FromImage(image)){
                    g.SmoothingMode=SmoothingMode.AntiAlias;
                    g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    PaintActivityRow(g,Width,first+i,0,Color.FromArgb(242,245,246),Color.FromArgb(181,190,197));
                }
            }
        }
        if(!desktop.ClearMessages()){
            EndClearMotion();tips.Show("清空失败，请重试",this,click.X,click.Y,2000);return;
        }
        page=0;
        if(!animate){SetExpanded(false);return;}
        clearClock.Restart();lensFrames.Interval=16;
    }
    void PaintClearRows(Graphics g,int width){
        float y=rowTop;
        for(int i=0;i<clearRows.Length;i++){
            float t=MotionEase((clearClock.Elapsed.TotalMilliseconds-i*clearStagger)/rowExit,.32,.72,0,1);
            if(t>=1)continue;
            using(var attr=new ImageAttributes()){
                var matrix=new ColorMatrix();matrix.Matrix33=1-t;attr.SetColorMatrix(matrix);
                var dst=new Rectangle((int)Math.Round(width*1.08*t),(int)Math.Round(y),width,rowHeight);
                var state=g.Save();g.SetClip(new RectangleF(0,y,width,rowHeight*(1-t)),CombineMode.Intersect);
                g.DrawImage(clearRows[i],dst,0,0,width,rowHeight,GraphicsUnit.Pixel,attr);g.Restore(state);
            }
            y+=rowHeight*(1-t);
        }
    }
    void AdvanceClearMotion(){
        if(!clearClock.IsRunning || clearClock.ElapsedMilliseconds<(clearRows.Length-1)*clearStagger+exitWait)return;
        EndClearMotion();
        if(desktop.Messages.Count==0){closingClearedPanel=true;SetExpanded(false);}
    }
    void EndClearMotion(){
        clearClock.Reset();
        if(clearRows!=null){foreach(var row in clearRows)row.Dispose();clearRows=null;}
        if(!flip.IsRunning)lensFrames.Interval=120;
    }
}
}
