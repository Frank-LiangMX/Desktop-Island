using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pulse {
// Diffuse the surrounding colors across one translucent surface.
internal sealed class GlassLens : IDisposable {
    const int Margin=14;
    readonly Bitmap scene,material,mist;
    readonly GraphicsPath silhouette=new GraphicsPath();
    readonly int[] source;
    public GlassLens(Size size){
        int w=size.Width,h=size.Height;
        scene=new Bitmap(w+Margin*2,h+Margin*2,PixelFormat.Format32bppRgb);
        material=new Bitmap(w,h,PixelFormat.Format32bppPArgb);
        mist=new Bitmap((w+15)/16+1,2,PixelFormat.Format32bppPArgb);
        silhouette.AddArc(1,1,40,40,180,90);silhouette.AddArc(w-41,1,40,40,270,90);
        silhouette.AddArc(w-41,h-41,40,40,0,90);silhouette.AddArc(1,h-41,40,40,90,90);
        silhouette.CloseFigure();
        source=new int[scene.Width*scene.Height];
    }
    public void Capture(Point location){
        Point origin=new Point(location.X-Margin,location.Y-Margin);
        var visible=Rectangle.Intersect(new Rectangle(origin,scene.Size),SystemInformation.VirtualScreen);
        if(visible.IsEmpty || visible.Width<=1 || visible.Height<=1)return;
        var local=new Rectangle(visible.X-origin.X,visible.Y-origin.Y,visible.Width,visible.Height);
        try{
            using(var g=Graphics.FromImage(scene))g.CopyFromScreen(visible.Location,local.Location,visible.Size);
        }catch(Win32Exception){return;}
        var bits=scene.LockBits(new Rectangle(Point.Empty,scene.Size),ImageLockMode.ReadOnly,scene.PixelFormat);
        try{Marshal.Copy(bits.Scan0,source,0,source.Length);}finally{scene.UnlockBits(bits);}
        int stride=scene.Width;
        // Low-pass the exposed backdrop above and below the widget; never sample our own content.
        for(int x=0;x<mist.Width;x++){
            int center=Margin+x*16;
            for(int row=0;row<2;row++){
                int red=0,green=0,blue=0,count=0;
                int first=row==0?0:scene.Height-Margin+1;
                for(int y=first;y<first+Margin-1;y+=3){
                    if(y<local.Top || y>=local.Bottom)continue;
                    for(int col=Math.Max(local.Left,center-22);col<Math.Min(local.Right,center+23);col+=3){
                        int pixel=source[y*stride+col];
                        red+=(pixel>>16)&255;green+=(pixel>>8)&255;blue+=pixel&255;count++;
                    }
                }
                mist.SetPixel(x,row,count==0?Color.Transparent:Color.FromArgb(66,red/count,green/count,blue/count));
            }
        }
        using(var layer=Graphics.FromImage(material))
        using(var fog=new TextureBrush(mist,WrapMode.Clamp)){
            layer.Clear(Color.Transparent);layer.SmoothingMode=SmoothingMode.AntiAlias;
            layer.InterpolationMode=InterpolationMode.HighQualityBilinear;
            fog.ScaleTransform(material.Width/(float)(mist.Width-1),material.Height/2f);
            layer.FillPath(fog,silhouette);
        }
    }
    public int Height{get{return material.Height;}}
    public void Paint(Graphics target,int width){
        int w=material.Width,h=material.Height;
        if(w==width){target.DrawImageUnscaled(material,0,0);return;}
        // Keep the metrics-side pixels registered while extending the center of the frosted surface.
        const int cap=22;
        int fixedRight=Math.Min(w,width)-2*cap;
        DrawSlice(target,0,cap,0,cap,h);
        DrawSlice(target,cap,w-cap-fixedRight,cap,width-cap-fixedRight,h);
        DrawSlice(target,w-fixedRight,fixedRight,width-fixedRight,fixedRight,h);
    }
    void DrawSlice(Graphics g,int sx,int sw,int x,int width,int height){
        g.DrawImage(material,new Rectangle(x,0,width,height),sx,0,sw,height,GraphicsUnit.Pixel);
    }
    public void Dispose(){scene.Dispose();mist.Dispose();material.Dispose();silhouette.Dispose();}
}
}

