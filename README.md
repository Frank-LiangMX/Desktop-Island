# 桌面 Island

Windows 常驻桌面工作台。收起时显示 CPU、内存和 GPU 使用率，点击指标即可在同一窗口查看进程排行。工作台支持 AI 会话关注、通知回看、音乐控制、专注计时，以及通过拖放暂存文件、文字和链接。

## 构建和运行

在 64 位 Windows 上安装 .NET Framework 4.8 Developer Pack 和 Windows 10 SDK。打开 PowerShell，进入项目目录，运行：

```powershell
.\Build.ps1
& ".\性能监视器.exe"
```

## 常用操作

- 按 **Ctrl + Alt + 空格** 唤出或收起工作台。右键菜单可更改快捷键、切换置顶或开始专注。
- 单击 CPU、RAM 或 GPU 查看排行。单击会话旁的星标可关注该任务。
- 将文件、文字或链接拖到浮窗即可暂存。点击文字可复制，文件可拖到其他应用。
- 微信和 WPS 协作提醒依赖客户端或 Windows 提供的通知信号。通知历史使用当前 Windows 用户的凭据在本机加密保存。
