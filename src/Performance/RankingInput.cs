using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pulse {
internal sealed partial class Dashboard {
    [StructLayout(LayoutKind.Sequential)]
    struct Device { public ushort page,usage; public uint flags; public IntPtr target; }
    [StructLayout(LayoutKind.Sequential)]
    struct Header { public uint type,size; public IntPtr device,param; }
    [StructLayout(LayoutKind.Explicit,Size=24)]
    struct MousePacket { [FieldOffset(4)] public ushort buttons; }
    [StructLayout(LayoutKind.Sequential)]
    struct Packet { public Header header; public MousePacket mouse; }
    [StructLayout(LayoutKind.Sequential)]
    struct ScreenPoint { public int x,y; }
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(ref Device device,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr raw,uint command,out Packet packet,ref uint size,uint headerSize);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out ScreenPoint point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
    bool listening;
    void TrackOutsideClicks(bool enable){
        if(enable==listening || !IsHandleCreated)return;
        var device=new Device{page=1,usage=2,flags=enable?0x100u:1u,target=enable?Handle:IntPtr.Zero};
        if(RegisterRawInputDevices(ref device,1,(uint)Marshal.SizeOf(typeof(Device))))listening=enable;
    }
    void RankingInput(int message,IntPtr lParam){
        if(message==0xff && listening && expanded && (section==Section.Processes || summoned)){
            Packet packet;uint size=(uint)Marshal.SizeOf(typeof(Packet));
            ScreenPoint pt;
            if(GetRawInputData(lParam,0x10000003,out packet,ref size,(uint)Marshal.SizeOf(typeof(Header)))==size &&
               packet.header.type==0 && (packet.mouse.buttons & 0x155)!=0 && GetCursorPos(out pt) &&
               GetAncestor(WindowFromPoint(pt),2)!=Handle)SetExpanded(false);
        }

    }
}
}
