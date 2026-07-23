# SMB Connection Viewer

一个 Windows SMB 连接可视化小工具，用来查看当前电脑作为文件共享服务器时，哪些客户端正在连接、是否打开文件、打开了哪些文件，并支持手动踢出连接。

## 功能

- 按层级区分 `已连接（活动中）` 和 `已连接（未打开文件）`
- 活动连接和空闲连接使用不同颜色显示
- 已打开文件会显示完整路径或共享相对路径
- 支持显示名称/备注，适合把 IPv6 地址手动标记成容易识别的设备名
- 支持手动踢出所选 SMB 连接
- 默认 10 秒自动刷新，可在界面调整刷新间隔
- 记住当前展开/折叠状态，刷新时不会强制展开活动连接

## 运行要求

- Windows
- 系统支持 `Get-SmbSession`、`Get-SmbOpenFile`、`Close-SmbSession`
- 建议使用管理员身份运行，踢出连接通常需要管理员权限

## 下载

如果你只想使用工具，请在 GitHub Releases 下载：

- `SmbConnectionViewer.exe`

备注会保存在 exe 同目录下的 `smb-connection-notes.json`。这个文件包含你的本机设备备注，不建议提交到仓库。

## 从源码构建

在 Windows PowerShell 中运行：

```powershell
.\build.ps1
```

构建产物会生成在：

```text
dist\SmbConnectionViewer.exe
```

## 源码结构

```text
src/
  SmbConnectionViewer.cs
  SmbConnectionViewer.exe.manifest
build.ps1
README.md
.gitignore
```

## 注意

SMB 会话里的 `ClientComputerName` 有时会显示成 IPv6 链路地址，例如 `[fe80::...]`。工具会尝试反向解析主机名；如果解析不到，可以在右侧填写显示名称/备注，下次会优先显示你填的名称。
