using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace Pulse {
internal static class ShellUnread {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string cls,string title);
    static readonly Regex amount=new Regex(@"(?:[\[(（【](\d+)\+?[\])）】]|(\d+)\+?\s*(?:条(?:新|未读)?消息|条未读|个(?:新|未读)?通知|unread|new messages?))",RegexOptions.IgnoreCase);
    internal static Dictionary<string,int> Sample(){
        var result=new Dictionary<string,int>();
        foreach(string cls in new[]{"Shell_TrayWnd","TopLevelWindowForOverflowXamlIsland","NotifyIconOverflowWindow"}){
            var h=FindWindow(cls,null);if(h==IntPtr.Zero)continue;
            var root=AutomationElement.FromHandle(h);
            var items=root.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            foreach(AutomationElement item in items){
                string label=item.Current.Name??"";
                string app=label.Contains("微信输入法")?null:label.Contains("微信") || label.StartsWith("WeChat",StringComparison.OrdinalIgnoreCase)?"Weixin":
                    label.Contains("WPS协作") || label.Contains("WPS 协作") || label.Contains("金山协作")?"xiezuo":null;
                if(app==null)continue;
                var match=amount.Match(label+" "+item.Current.ItemStatus);
                int n=0;
                if(match.Success)int.TryParse(match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value,out n);
                int existing;result.TryGetValue(app,out existing);result[app]=Math.Max(existing,n);
            }
        }
        return result;
    }
}
}
