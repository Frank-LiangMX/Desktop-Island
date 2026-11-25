using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Pulse {
internal sealed class CapturePicker : Form {
    readonly Bitmap screen;
    Point start;
    bool selecting;
    internal Rectangle RegionPicked{get;private set;}
    internal CapturePicker(Rectangle bounds,Bitmap snapshot){
        screen=snapshot;AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.Manual;
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;DoubleBuffered=true;
        Bounds=bounds;Cursor=Cursors.Cross;KeyPreview=true;
        KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape)DialogResult=DialogResult.Cancel;
            if(e.KeyCode==Keys.Enter){RegionPicked=ClientRectangle;DialogResult=DialogResult.OK;}};
        MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Right){DialogResult=DialogResult.Cancel;return;}
            if(e.Button==MouseButtons.Left){start=e.Location;selecting=true;Capture=true;}};
        MouseMove+=(s,e)=>{if(selecting){RegionPicked=Selection(start,e.Location,ClientRectangle);Invalidate();}};
        MouseUp+=(s,e)=>{if(!selecting || e.Button!=MouseButtons.Left)return;
            selecting=false;Capture=false;RegionPicked=Selection(start,e.Location,ClientRectangle);
            if(RegionPicked.Width>=2 && RegionPicked.Height>=2)DialogResult=DialogResult.OK;
            else Invalidate();};
    }
    internal static Rectangle Selection(Point a,Point b,Rectangle bounds){
        return Rectangle.Intersect(bounds,Rectangle.FromLTRB(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Max(a.X,b.X),Math.Max(a.Y,b.Y)));
    }
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.DrawImageUnscaled(screen,0,0);
        using(var shade=new SolidBrush(Color.FromArgb(95,0,0,0)))g.FillRectangle(shade,ClientRectangle);
        var r=RegionPicked;
        if(r.Width>0 && r.Height>0){
            g.DrawImage(screen,r,r,GraphicsUnit.Pixel);
            using(var pen=new Pen(Color.FromArgb(159,244,228),2))g.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);
        }
        const string hint="拖动选择区域   ·   Enter 整屏   ·   Esc 取消";
        var size=TextRenderer.MeasureText(hint,Font);
        var box=new Rectangle((Width-size.Width)/2-10,16,size.Width+20,size.Height+14);
        using(var b=new SolidBrush(Color.FromArgb(225,29,33,39)))g.FillRectangle(b,box);
        TextRenderer.DrawText(g,hint,Font,box,Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
}
internal sealed partial class Dashboard {
    Process recorder;
    bool pickingCapture,finishingRecording,quitAfterRecording;
    readonly Stopwatch recordClock=new Stopwatch();
    string RecordingLabel(){return recorder==null?"录屏…":finishingRecording?"正在保存录像":"停止录屏 · "+recordClock.Elapsed.ToString(@"hh\:mm\:ss");}
    void ToggleRecording(){if(recorder!=null)StopRecording();else PickCapture(true);}
    void StopRecording(){
        if(recorder==null || finishingRecording)return;
        finishingRecording=true;
        try{recorder.StandardInput.WriteLine("q");recorder.StandardInput.Flush();}
        catch(IOException){}catch(InvalidOperationException){}
    }
    async void PickCapture(bool video){
        if(pickingCapture || (video && recorder!=null))return;
        pickingCapture=true;
        var bounds=Screen.FromControl(this).Bounds;
        bool hidden=false;
        try{
            options.Close();Hide();hidden=true;await Task.Delay(180);
            if(IsDisposed || Disposing)return;
            using(var bitmap=new Bitmap(bounds.Width,bounds.Height,PixelFormat.Format32bppRgb)){
                using(var g=Graphics.FromImage(bitmap))g.CopyFromScreen(bounds.Location,Point.Empty,bounds.Size);
                Rectangle region;
                using(var picker=new CapturePicker(bounds,bitmap)){
                    if(picker.ShowDialog()!=DialogResult.OK)return;region=picker.RegionPicked;
                }
                if(video){
                    region.Offset(bounds.Location);StartRecording(region);
                }else{
                    var file=CaptureFile(false);
                    using(var crop=bitmap.Clone(region,PixelFormat.Format32bppRgb))crop.Save(file,ImageFormat.Png);
                    StoreCapture(file);
                }
            }
        }catch(Exception ex){tray.ShowBalloonTip(4000,video?"录屏未启动":"截图失败",ex.Message,ToolTipIcon.Warning);}
        finally{pickingCapture=false;if(hidden && !IsDisposed && !Disposing){Show();RefreshSurface();}}
    }
    static string CaptureFile(bool video){
        var folder=Path.Combine(Environment.GetFolderPath(video?Environment.SpecialFolder.MyVideos:Environment.SpecialFolder.MyPictures),"Desktop Island");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder,DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+(video?".mp4":".png"));
    }
    void StoreCapture(string path){
        shelf.Insert(0,new ShelfEntry{Value=path,FileItem=true});page=0;
        tray.ShowBalloonTip(2500,"已保存到暂存区",Path.GetFileName(path),ToolTipIcon.Info);
        RefreshSurface();
    }
    static string EncoderPath(){
        foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)){
            string clean=dir.Trim().Trim('"');
            if(!Path.IsPathRooted(clean))continue;
            var file=Path.Combine(clean,"ffmpeg.exe");if(File.Exists(file))return file;
        }
        throw new FileNotFoundException("录屏需要 FFmpeg，请安装后将 ffmpeg.exe 加入 PATH。");
    }
    internal static string CaptureArguments(Rectangle region,string path){
        return "-hide_banner -loglevel error -f gdigrab -framerate 24 -draw_mouse 1 -offset_x "+region.X+
            " -offset_y "+region.Y+" -video_size "+region.Width+"x"+region.Height+
            " -i desktop -an -vf pad=ceil(iw/2)*2:ceil(ih/2)*2 -c:v libx264 -preset ultrafast -crf 22 -pix_fmt yuv420p -movflags +faststart \""+path+"\"";
    }
    void StartRecording(Rectangle region){
        string encoder=EncoderPath(),file=CaptureFile(true),partial=Path.ChangeExtension(file,"partial.mp4");
        var info=new ProcessStartInfo(encoder,CaptureArguments(region,partial)){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardError=true};
        var process=Process.Start(info);recorder=process;recordClock.Restart();finishingRecording=false;
        tray.Text="Island · 正在录屏";tray.ShowBalloonTip(2200,"录屏已开始","右键 Island 或托盘图标可停止并保存",ToolTipIcon.Info);
        ObserveRecording(process,partial,file);
    }
    async void ObserveRecording(Process process,string partial,string destination){
        try{
            var errors=process.StandardError.ReadToEndAsync();
            await Task.Run(()=>process.WaitForExit());string message=(await errors).Trim();
            if(process.ExitCode!=0 || !File.Exists(partial) || new FileInfo(partial).Length==0)
                throw new IOException(string.IsNullOrEmpty(message)?"编码器没有生成有效录像":message.Substring(0,Math.Min(180,message.Length)));
            File.Move(partial,destination);
            if(!IsDisposed)StoreCapture(destination);
        }catch(Exception ex){if(!IsDisposed)tray.ShowBalloonTip(4000,"录像保存失败",ex.Message,ToolTipIcon.Warning);}
        finally{
            process.Dispose();recorder=null;recordClock.Reset();finishingRecording=false;
            if(!IsDisposed){tray.Text="性能监视器";RefreshSurface();if(quitAfterRecording){quitting=true;Close();}}
        }
    }
}
}
