using Ext4Mounter;

Console.WriteLine("=== Ext4Mounter - Linux ext4 分区自动挂载工具 ===");
Console.WriteLine("请以管理员身份运行本程序（需要读取物理磁盘）");
Console.WriteLine("需要 Windows 10 1809 或更高版本（ProjFS 支持）");
Console.WriteLine();

// 检查 ProjFS 功能是否可用
if (!ProjFSProvider.IsProjFSAvailable())
{
    ProjFSProvider.PrintEnableInstructions();
    Console.WriteLine();
    Console.WriteLine("按任意键退出...");
    Console.ReadKey(true);
    return;
}
Console.WriteLine("[ProjFS] Windows Projected File System 已启用 ✓");
Console.WriteLine("[提示] ext4 分区以只读模式挂载，如需删除文件请在 Linux 下操作");
Console.WriteLine();
using var mountService = new MountService();
using var usbWatcher = new UsbWatcher();

// 启动时先扫描一次
Console.WriteLine("[启动] 扫描已连接的 USB 磁盘...");
mountService.MountAllUsbExt4();

// 监听 USB 插拔
usbWatcher.DeviceInserted += () =>
{
    try
    {
        // ReSharper disable once AccessToDisposedClosure
        mountService.MountAllUsbExt4();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 挂载失败: {ex.Message}");
    }
};
usbWatcher.DeviceRemoved += () =>
{
    try
    {
        // ReSharper disable once AccessToDisposedClosure
        mountService.UnmountStalePartitions();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[错误] 卸载失败: {ex.Message}");
    }
};
usbWatcher.Start();
Console.WriteLine();
Console.WriteLine("[运行中] 快捷键:");
Console.WriteLine("  Q = 退出  S = 重新扫描  L = 列出已挂载分区");
Console.WriteLine("  挂载后可直接在资源管理器中浏览和复制文件");
Console.WriteLine();
while (true)
{
    var key = Console.ReadKey(true);
    switch (char.ToUpper(key.KeyChar))
    {
        case 'Q':
            Console.WriteLine("[退出] 正在卸载所有分区...");
            mountService.UnmountAll();
            return;
        case 'S':
            Console.WriteLine("[扫描] 重新扫描所有磁盘...");
            var partitions = DiskManager.ScanForExt4Partitions();
            if (partitions.Count == 0)
            {
                Console.WriteLine("  未发现 ext4 分区");
            }
            else
            {
                foreach (var p in partitions)
                {
                    Console.WriteLine($"  {p.Description}");
                }
            }
            mountService.MountAllUsbExt4();
            break;
        case 'L':
            Console.WriteLine("[列表] 当前挂载的 ext4 驱动器可在资源管理器中查看");
            break;
    }
}