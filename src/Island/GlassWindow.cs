using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pulse {
internal sealed partial class Dashboard {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool RegisterShellHookWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool DeregisterShellHookWindow(IntPtr window);
    static readonly int shellMessage=RegisterWindowMessage("SHELLHOOK");
    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);RegisterShellHookWindow(Handle);shortcutActive=BindShortcut(Handle,shortcutId,shortcut);
    }
    protected override void OnHandleDestroyed(EventArgs e){
        if(shortcutActive){UnregisterHotKey(Handle,shortcutId);shortcutActive=false;}
        DeregisterShellHookWindow(Handle);base.OnHandleDestroyed(e);
    }
    protected override void WndProc(ref Message m){
        if(m.Msg==0x312 && m.WParam.ToInt32()==shortcutId){Summon();return;}
        RankingInput(m.Msg,m.LParam);
        if(shellMessage!=0 && m.Msg==shellMessage && m.WParam.ToInt64()==0x8006 && desktop!=null)
            desktop.OnFlash(m.LParam);
        base.WndProc(ref m);
    }
    protected override bool ShowWithoutActivation {get{return compact && !pinned;}}
    const int LayeredStyle = 0x80000;
    protected override CreateParams CreateParams {
        get {
            var p=base.CreateParams;
            if(compact)p.ExStyle |= LayeredStyle;
            return p;
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack=1)]
    struct Blend { public byte op, flags, opacity, alpha; }
    [DllImport("user32.dll",SetLastError=true)]
    static extern bool UpdateLayeredWindow(IntPtr window,IntPtr screen,ref Point position,ref Size size,IntPtr source,ref Point origin,uint key,ref Blend blend,uint flags);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc,IntPtr bitmap);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);

    void PresentWidget(){PresentFrame(Bounds,false);}
    // Commit the new position, dimensions and alpha surface in one compositor update.
    void PresentFrame(Rectangle frame,bool resizing){
        using(var image=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppPArgb)){
            using(var g=Graphics.FromImage(image)){
                g.SmoothingMode=SmoothingMode.AntiAlias;
                g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                PaintWidget(g,frame,resizing);
            }
            IntPtr dc=CreateCompatibleDC(IntPtr.Zero);
            IntPtr bitmap=image.GetHbitmap(Color.FromArgb(0)),old=SelectObject(dc,bitmap);
            try{
                var size=image.Size;var location=frame.Location;var origin=Point.Empty;
                var blend=new Blend{opacity=255,alpha=1};
                if(!UpdateLayeredWindow(Handle,IntPtr.Zero,ref location,ref size,dc,ref origin,0,ref blend,2))
                    throw new Win32Exception();
                if(resizing)UpdateBounds();
            }finally{SelectObject(dc,old);DeleteObject(bitmap);DeleteDC(dc);}
        }
    }
}
}


