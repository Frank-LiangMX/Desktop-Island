using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Pulse {
internal sealed partial class Dashboard {
    sealed class ShelfEntry {
        internal string Value;
        internal bool FileItem,Link;
        internal string Label{get{return FileItem?Path.GetFileName(Value):Value.Replace("\r"," ").Replace("\n"," ");}}
    }
    readonly List<ShelfEntry> shelf=new List<ShelfEntry>();
    bool shelfPreview,shelfWasExpanded,exportingFile;
    Section shelfPrevious;
    int shelfPreviousPage;
    ShelfEntry pressedFile;
    bool ShowFileTab{get{return shelf.Count>0 || section==Section.Files;}}
    void InitializeShelf(){
        AllowDrop=true;
        DragEnter+=(s,e)=>{
            SetDropEffect(e);
            if(e.Effect==DragDropEffects.None || shelfPreview)return;
            shelfWasExpanded=expanded;shelfPrevious=section;shelfPreviousPage=page;
            shelfPreview=true;hoverOpen=false;SetExpanded(true);SelectSection(Section.Files);page=0;RefreshSurface();
        };
        DragOver+=(s,e)=>SetDropEffect(e);
        DragLeave+=(s,e)=>EndShelfPreview(false);
        DragDrop+=(s,e)=>{
            SetDropEffect(e);
            var items=e.Effect==DragDropEffects.Copy?DroppedItems(e.Data):new List<ShelfEntry>();
            if(items.Count==0){e.Effect=DragDropEffects.None;EndShelfPreview(false);return;}
            foreach(var entry in items.AsEnumerable().Reverse()){
                shelf.RemoveAll(old=>old.FileItem==entry.FileItem && string.Equals(old.Value,entry.Value,
                    entry.FileItem?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal));
                shelf.Insert(0,entry);
            }
            EndShelfPreview(true);
        };
    }
    static List<ShelfEntry> DroppedItems(IDataObject data){
        var result=new List<ShelfEntry>();
        if(data.GetDataPresent(DataFormats.FileDrop,false)){
            var paths=data.GetData(DataFormats.FileDrop,false) as string[];
            if(paths!=null)foreach(var path in paths.Where(File.Exists))result.Add(new ShelfEntry{Value=path,FileItem=true});
            return result;
        }
        string text=data.GetData(DataFormats.UnicodeText) as string;
        if(string.IsNullOrWhiteSpace(text))text=data.GetData(DataFormats.Text) as string;
        if(string.IsNullOrWhiteSpace(text)){
            var stream=data.GetData("UniformResourceLocatorW",false) as MemoryStream;
            if(stream!=null)text=System.Text.Encoding.Unicode.GetString(stream.ToArray()).TrimEnd((char)0);
        }
        if(!string.IsNullOrWhiteSpace(text)){
            Uri uri;bool link=Uri.TryCreate(text.Trim(),UriKind.Absolute,out uri) && (uri.Scheme=="https" || uri.Scheme=="http");
            result.Add(new ShelfEntry{Value=text,Link=link});
        }
        return result;
    }
    void CopyShelf(ShelfEntry entry){
        if(entry.FileItem)return;
        try{Clipboard.SetText(entry.Value);tips.Show("已复制",this,18,Height,1000);}
        catch(System.Runtime.InteropServices.ExternalException){tips.Show("剪贴板正忙，请重试",this,18,Height,1500);}
    }
    void SetDropEffect(DragEventArgs e){
        e.Effect=compact && !exportingFile && (e.AllowedEffect&DragDropEffects.Copy)!=0 &&
            e.Data!=null && (e.Data.GetDataPresent(DataFormats.FileDrop,false) || e.Data.GetDataPresent(DataFormats.UnicodeText) || e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent("UniformResourceLocatorW",false))?DragDropEffects.Copy:DragDropEffects.None;
    }
    void EndShelfPreview(bool kept){
        if(!shelfPreview)return;
        shelfPreview=false;
        if(!kept){
            if(!shelfWasExpanded)SetExpanded(false);
            if(shelfWasExpanded && shelfPrevious==Section.Processes)OpenRanking(ranking.Metric);
            else SelectSection(shelfPrevious);
            page=shelfPreviousPage;
        }
        RefreshSurface();
    }
    void ExportFile(ShelfEntry entry){
        if(entry.FileItem && !File.Exists(entry.Value)){tips.Show("找不到原文件，可从暂存区移除",this,15,Height,2200);return;}
        exportingFile=true;
        try{
            var data=new DataObject();if(entry.FileItem)data.SetData(DataFormats.FileDrop,false,new[]{entry.Value});else data.SetText(entry.Value);
            DoDragDrop(data,DragDropEffects.Copy);
        }finally{exportingFile=false;}
    }
    void PaintShelfRow(Graphics g,int w,int idx,int y,Color fg,Color dim){
        var entry=shelf[idx];bool hovered=RowAt(PointToClient(Cursor.Position))==idx;
        Surface(g,new Rectangle(14,y,w-28,37),9,Color.FromArgb(hovered?37:14,255,255,255));
        WorkText(g,entry.FileItem?"\uE8A5":entry.Link?"\uE71B":"\uE8E9",21,y,24,37,16,aiMint,false,true,true);
        WorkText(g,entry.Label,51,y,w-120,21,13,fg,true);
        WorkText(g,entry.FileItem?Path.GetDirectoryName(entry.Value):entry.Link?"链接 · 点击复制":"文字 · 点击复制",51,y+18,w-120,18,11,dim);
        if(hovered){var r=ReadAction(w,y);WorkText(g,"\uE711",r.X,r.Y,r.Width,r.Height,12,dim,false,true,true);}
    }
}
}
