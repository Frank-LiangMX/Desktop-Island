using System;
using System.Collections.Generic;
using System.Management;
using System.Runtime.InteropServices;

namespace Pulse {
internal sealed class Sensors : IDisposable {
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [StructLayout(LayoutKind.Sequential)] struct Memory {
        public uint length, load;
        public ulong total, available, page, availablePage, virtualSize, availableVirtual, extended;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref Memory m);
    [DllImport("pdh.dll", CharSet=CharSet.Unicode)] static extern uint PdhOpenQuery(string source, IntPtr data, out IntPtr query);
    [DllImport("pdh.dll", CharSet=CharSet.Unicode)] static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr data,out IntPtr counter);
    [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)] static extern uint PdhGetFormattedCounterArray(IntPtr counter,uint format, ref uint size,out uint count,IntPtr items);
    [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr query);
    [StructLayout(LayoutKind.Sequential)] struct Item { public IntPtr name; public uint status; public double value; }
    IntPtr query, counter;
    ulong lastIdle,lastTotal;
    bool primed;
    public Sensors() {
        ReadCpu();
        if(PdhOpenQuery(null,IntPtr.Zero,out query)==0 &&
           PdhAddEnglishCounter(query,@"\GPU Engine(*)\Utilization Percentage",IntPtr.Zero,out counter)==0)
            PdhCollectQueryData(query);
    }
    double ReadCpu() {
        ulong idle,kernel,user;
        if(!GetSystemTimes(out idle,out kernel,out user)) return double.NaN;
        ulong total=kernel+user;
        double value=primed && total>lastTotal ? 100.0*(1.0-(double)(idle-lastIdle)/(total-lastTotal)) : double.NaN;
        lastIdle=idle; lastTotal=total; primed=true; return value;
    }
    internal double ReadGpu(Dictionary<int,double> byProcess) {
        if(counter==IntPtr.Zero || PdhCollectQueryData(query)!=0) return double.NaN;
        uint bytes=0,count;
        if(PdhGetFormattedCounterArray(counter,0x200,ref bytes,out count,IntPtr.Zero)!=0x800007D2 || bytes==0) return double.NaN;
        IntPtr buffer=Marshal.AllocHGlobal((int)bytes);
        try {
            if(PdhGetFormattedCounterArray(counter,0x200,ref bytes,out count,buffer)!=0) return double.NaN;
            var engines=new Dictionary<string,double>();
            int stride=Marshal.SizeOf(typeof(Item));
            for(int i=0;i<count;i++) {
                var item=(Item)Marshal.PtrToStructure(IntPtr.Add(buffer,i*stride),typeof(Item));
                if(item.status>1) continue;
                string name=Marshal.PtrToStringUni(item.name);
                int keyStart=name.IndexOf("luid_",StringComparison.OrdinalIgnoreCase);
                if(keyStart<0) continue;
                string key=name.Substring(keyStart).Split('#')[0];
                double previous; engines.TryGetValue(key,out previous);
                engines[key]=previous+item.value;
                int pid;
                if(byProcess!=null && name.StartsWith("pid_") && keyStart>5 &&
                   int.TryParse(name.Substring(4,keyStart-5),out pid)){
                    double current; byProcess.TryGetValue(pid,out current);
                    byProcess[pid]=Math.Max(current,Math.Min(100,item.value));
                }
            }
            double peak=double.NaN;
            foreach(double v in engines.Values) peak=double.IsNaN(peak)?v:Math.Max(v,peak);
            return Math.Min(100,peak);
        } finally {Marshal.FreeHGlobal(buffer);}
    }
    public Sample Read() {
        var m=new Memory(); m.length=(uint)Marshal.SizeOf(m);
        bool ok=GlobalMemoryStatusEx(ref m) && m.total>0;
        return new Sample { time=DateTime.UtcNow, values=new[]{ReadCpu(),ok?100.0*(m.total-m.available)/m.total:double.NaN,ReadGpu(null)},
          total=ok?m.total/1073741824.0:0,used=ok?(m.total-m.available)/1073741824.0:0 };
    }
    public static string Hardware(string collection) {
        try {
            using(var search=new ManagementObjectSearcher("SELECT Name FROM "+collection))
            using(var results=search.Get()) {
                var names=new List<string>();
                foreach(ManagementObject row in results) using(row) { string n=Convert.ToString(row["Name"]).Trim(); if(n.Length>0) names.Add(n); }
                return string.Join("  /  ",names.ToArray());
            }
        } catch(ManagementException) {return "";} catch(UnauthorizedAccessException) {return "";}
    }
    public void Dispose(){if(query!=IntPtr.Zero) PdhCloseQuery(query);}
}
internal class Sample { public DateTime time; public double[] values; public double total,used; }
}
