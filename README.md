# Ext4Mounter

在 Windows 下自动挂载 USB 磁盘上的 Linux ext4 分区，映射为本地驱动器盘符，可直接在资源管理器中浏览和复制文件。

## 系统要求

- Windows 10 1809 (October 2018 Update) 或更高版本
- 以管理员身份运行（需要读取物理磁盘）
- 启用 Windows Projected File System (ProjFS) 可选功能

## 启用 ProjFS

首次使用前需要启用 ProjFS 功能，以管理员身份运行以下任一命令：

**PowerShell：**
```powershell
Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart
```

**DISM：**
```cmd
dism /online /enable-feature /featurename:Client-ProjFS
```

启用后需要重启电脑。

## 使用方式

1. 将包含 ext4 文件系统的 USB 磁盘连接到电脑
2. 以管理员身份运行 `Ext4Mounter.exe`
3. 程序自动扫描并挂载 ext4 分区，分配盘符（如 Z:）
4. 在资源管理器中打开对应盘符即可浏览文件
5. 可直接复制文件到本地磁盘

**快捷键：**

| 按键 | 功能 |
|------|------|
| Q | 退出程序，卸载所有分区 |
| S | 重新扫描所有 USB 磁盘 |
| L | 列出已挂载的分区 |

## 工作原理

```
USB 磁盘 (ext4)
    │
    ▼
PhysicalDiskStream ── 扇区对齐的物理磁盘读取
    │
    ▼
DiscUtils.Ext ── 解析 ext4 文件系统元数据和文件内容
    │
    ▼
ProjFSProvider ── Windows Projected File System 虚拟化提供程序
    │                 响应目录枚举、文件信息查询、文件数据读取回调
    │
    ▼
虚拟化根目录 (%TEMP%\ext4mount_*)
    │
    ▼
DriveMapper ── 通过 subst / DefineDosDevice 映射为驱动器盘符
    │
    ▼
资源管理器 / 应用程序 ── 像访问本地磁盘一样访问 ext4 文件
```

**核心组件：**

- **DiskManager** — 扫描 USB 磁盘，识别 GPT/MBR 分区表中的 ext4 分区，支持整盘 ext4 检测
- **PhysicalDiskStream** — 封装物理磁盘的 FileStream，处理 Windows 物理磁盘的 512 字节扇区对齐要求
- **ProjFSProvider** — ProjFS 虚拟化提供程序，通过 P/Invoke 调用 `ProjectedFSLib.dll`，实现目录枚举、Placeholder 创建、文件数据按需提供
- **DriveMapper** — 将虚拟化根目录映射为驱动器盘符，通过 Task Scheduler 以非提权身份执行 subst 解决 UAC 会话隔离问题
- **MountService** — 协调挂载/卸载流程
- **UsbWatcher** — 通过 WMI 监听 USB 设备插拔事件，自动触发挂载/卸载

## 注意事项

### 只读挂载

ext4 分区以**只读模式**挂载（底层库 DiscUtils 不支持 ext4 写入）。如需修改或删除文件，请在 Linux 下操作。在资源管理器中删除文件只会删除本地 ProjFS 缓存，不会影响 ext4 源数据，重新挂载后文件会重新出现。

### 磁盘大小显示不准确

资源管理器中显示的磁盘总容量和可用空间是本地缓存盘（通常是 C 盘）的信息，**不是 ext4 分区的真实大小**。这是 ProjFS 的平台限制。程序启动时会在控制台输出 ext4 分区的真实空间信息。

### 大文件首次打开较慢

ProjFS 不支持部分文件读取（partial hydration），打开文件时会将整个文件内容从 USB 磁盘读取到本地缓存。对于大文件（如视频），首次打开需要等待整个文件传输完成。传输速度取决于 USB 磁盘的读取速度（通常 20-30 MB/s）。文件缓存完成后，后续访问为本地磁盘速度。

### 盘符可见性

程序以管理员身份运行，为解决 UAC 会话隔离导致的盘符不可见问题，程序通过 Task Scheduler 以非提权身份创建盘符映射。如果资源管理器中仍看不到盘符，可尝试按 F5 刷新或重新打开资源管理器窗口。

### 支持的分区格式

- GPT 分区表中的 ext4 分区（Linux filesystem 类型）
- MBR 分区表中的 ext4 分区（类型 0x83）
- 整盘 ext4（无分区表，整个磁盘为一个 ext4 文件系统）

## 构建

```bash
dotnet build
```

**依赖：**

- .NET 10 (或更高版本)
- LTRData.DiscUtils.Ext — ext4 文件系统解析
- System.Management — WMI USB 设备监听
- ProjectedFSLib.dll — Windows 系统自带（需启用 ProjFS 功能）

## 许可

本项目仅供学习和个人使用。
