using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;

namespace Pulse {
internal sealed class GlassMenu {
    readonly ContextMenu menu;
    readonly MenuItem mode,pin,start,shortcut,focus;
    internal GlassMenu(Action toggle,Action togglePin,Action toggleStart,Action agents,Action configureShortcut,Action toggleFocus,Action exit) {
        menu=(ContextMenu)XamlReader.Parse(@"
<ContextMenu xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' HasDropShadow='False'
 FontFamily='Microsoft YaHei UI' FontSize='12' Foreground='#F7FAFF' Placement='AbsolutePoint'>
 <ContextMenu.Template><ControlTemplate TargetType='ContextMenu'>
  <Border Padding='6' CornerRadius='14' Background='#E6232D3A' BorderBrush='#709BAFC4' BorderThickness='1'>
   <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
  </Border>
 </ControlTemplate></ContextMenu.Template>
 <ContextMenu.Resources>
  <Style TargetType='MenuItem'><Setter Property='Template'><Setter.Value>
   <ControlTemplate TargetType='MenuItem'>
    <Border x:Name='row' Background='Transparent' CornerRadius='8' Padding='10,8' MinWidth='186'>
     <Grid><Grid.ColumnDefinitions><ColumnDefinition Width='26'/><ColumnDefinition/><ColumnDefinition Width='23'/></Grid.ColumnDefinitions>
      <TextBlock FontFamily='Segoe MDL2 Assets' Text='{TemplateBinding Tag}' Foreground='#C5D9EA' VerticalAlignment='Center'/>
      <ContentPresenter Grid.Column='1' ContentSource='Header' VerticalAlignment='Center'/>
      <TextBlock x:Name='check' Grid.Column='2' Text='✓' Foreground='#A2EBDD' Visibility='Hidden' HorizontalAlignment='Right'/>
     </Grid>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='row' Property='Background' Value='#447F9BAF'/></Trigger>
     <Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate>
  </Setter.Value></Setter></Style>
  <Style TargetType='Separator'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Separator'>
   <Border Height='1' Background='#486C8093' Margin='9,4'/>
  </ControlTemplate></Setter.Value></Setter></Style>
 </ContextMenu.Resources>
</ContextMenu>");
        Add("打开活动中心","\uE8BD",agents);
        mode=Add("打开详细趋势","\uE9D9",toggle);pin=Add("小组件置顶","\uE718",togglePin);
        focus=Add("开始专注 · 25 分钟","\uE916",toggleFocus);
        shortcut=Add("唤出快捷键","\uE765",configureShortcut);
        start=Add("登录时启动","\uE7E8",toggleStart);menu.Items.Add(new Separator());Add("退出","\uE7E8",exit);
    }
    MenuItem Add(string label,string glyph,Action action){
        var item=new MenuItem{Header=label,Tag=glyph};item.Click+=(s,e)=>action();menu.Items.Add(item);return item;
    }
    public void Show(System.Drawing.Point screen,float dpi,bool compact,bool pinned,bool startup,string hotkey,bool focusing){
        focus.Header=focusing?"结束专注":"开始专注 · 25 分钟";
        shortcut.Header="唤出快捷键 · "+hotkey;
        mode.Header=compact?"打开详细趋势":"返回桌面小组件";pin.IsChecked=pinned;start.IsChecked=startup;
        menu.HorizontalOffset=screen.X*96.0/dpi;menu.VerticalOffset=screen.Y*96.0/dpi;
        menu.IsOpen=true;
    }
    public void Close(){menu.IsOpen=false;}
}
}



