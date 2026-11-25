using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Pulse {
internal sealed partial class Dashboard {
    const int shortcutId=0x5055;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr window,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window,int id);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    Keys shortcut=Keys.Control|Keys.Alt|Keys.Space;
    bool shortcutActive,capturingShortcut,summoned,usedWorkbench;
    IntPtr returnWindow;
    static bool BindShortcut(IntPtr window,int id,Keys keys){
        uint mods=0x4000;
        if((keys&Keys.Alt)!=0)mods|=1;
        if((keys&Keys.Control)!=0)mods|=2;
        if((keys&Keys.Shift)!=0)mods|=4;
        return RegisterHotKey(window,id,mods,(uint)(keys&Keys.KeyCode));
    }
    string ShortcutLabel(){return new KeysConverter().ConvertToString(shortcut)+(shortcutActive?"":"（未注册）");}
    void Summon(){
        if(capturingShortcut)return;
        if(summoned){
            var previous=returnWindow;
            SetExpanded(false);
            if(previous!=IntPtr.Zero && previous!=Handle)SetForegroundWindow(previous);
            return;
        }
        returnWindow=GetForegroundWindow();
        var last=section;bool restore=usedWorkbench;
        if(!compact)SwitchMode(true);
        WindowState=FormWindowState.Normal;Show();BringToFront();Activate();SetForegroundWindow(Handle);
        hoverOpen=false;summoned=true;SetExpanded(true);
        if(restore){
            if(last==Section.Processes)OpenRanking(ranking==null?0:ranking.Metric);
            else SelectSection(last);
        }
        TrackOutsideClicks(true);RefreshSurface();
    }
    void CaptureShortcut(){
        BeginInvoke((Action)(()=>{
            if(!compact)SwitchMode(true);
            Show();BringToFront();Activate();SetForegroundWindow(Handle);capturingShortcut=true;
            tips.Show("按下新的组合键（Ctrl 或 Alt），Esc 取消",this,10,Height,10000);
        }));
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
        if(!capturingShortcut)return base.ProcessCmdKey(ref msg,keyData);
        var key=keyData&Keys.KeyCode;
        if(key==Keys.Escape){capturingShortcut=false;tips.Hide(this);return true;}
        if((keyData&(Keys.Control|Keys.Alt))==0 || key==Keys.ControlKey || key==Keys.Menu || key==Keys.ShiftKey)return true;
        if(keyData!=shortcut || !shortcutActive){
            if(shortcutActive)UnregisterHotKey(Handle,shortcutId);
            if(!BindShortcut(Handle,shortcutId,keyData)){
                shortcutActive=BindShortcut(Handle,shortcutId,shortcut);
                tips.Show("该组合键已被占用，请换一个",this,10,Height,5000);return true;
            }
            shortcutActive=true;shortcut=keyData;
            using(var k=Registry.CurrentUser.CreateSubKey(Settings))k.SetValue("SummonShortcut",(int)shortcut);
        }
        capturingShortcut=false;tips.Show("唤出快捷键："+ShortcutLabel(),this,10,Height,1800);return true;
    }
}
}
