using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulse {
internal sealed class ProcessIcons : IDisposable {
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
    struct FileInfo {
        public IntPtr icon; public int index; public uint attributes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string name;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string type;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path,uint attributes,out FileInfo info,uint size,uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access,bool inherit,int id);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int length);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    readonly Dictionary<string,Bitmap> cache=new Dictionary<string,Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly Bitmap fallback=ConvertIcon(SystemIcons.Application.Handle);
    static Bitmap ConvertIcon(IntPtr h){return Icon.FromHandle(h).ToBitmap();}
    public void Dispose(){foreach(var image in cache.Values)if(image!=fallback)image.Dispose();fallback.Dispose();}
    public Bitmap Read(int pid){
        var handle=OpenProcess(0x1000,false,pid);
        if(handle==IntPtr.Zero)return fallback;
        string path;
        try{
            int len=1024;var value=new StringBuilder(len);
            if(!QueryFullProcessImageName(handle,0,value,ref len))return fallback;
            path=value.ToString();
        }finally{CloseHandle(handle);}
        Bitmap icon;
        if(cache.TryGetValue(path,out icon))return icon;
        FileInfo info;
        icon=fallback;
        if(SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(FileInfo)),0x101)!=IntPtr.Zero && info.icon!=IntPtr.Zero){
            try{icon=ConvertIcon(info.icon);}finally{DestroyIcon(info.icon);}
        }
        
        cache[path]=icon;return icon;
    }
}
}
