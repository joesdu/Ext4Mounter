using Ext4Mounter;
using Serilog;

// 初始化 Serilog：同时输出到控制台和日志文件
Log.Logger = new LoggerConfiguration()
             .MinimumLevel.Debug()
             .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
             .WriteTo.File(Path.Combine(AppContext.BaseDirectory, "logs", "ext4mounter-.log"),
                 rollingInterval: RollingInterval.Day,
                 retainedFileCountLimit: 7,
                 outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
             .CreateLogger();
try
{
    Log.Information("=== Ext4Mounter - Linux ext4 分区自动挂载工具 ===");
    Log.Information("请以管理员身份运行本程序（需要读取物理磁盘）");
    Log.Information("需要 Windows 10 1809 或更高版本（ProjFS 支持）");

    // 清理上次非正常退出残留的盘符映射
    await DriveMapper.CleanupStaleSubstMappingsAsync();

    // 检查 ProjFS 功能是否可用
    if (!ProjFSProvider.IsProjFSAvailable())
    {
        ProjFSProvider.PrintEnableInstructions();
        Log.Warning("ProjFS 未启用，程序无法运行");
        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey(true);
        return;
    }
    Log.Information("[ProjFS] Windows Projected File System 已启用 ✓");
    Log.Information("[提示] ext4 分区以只读模式挂载，如需删除文件请在 Linux 下操作");
    using var mountService = new MountService();
    using var usbWatcher = new UsbWatcher();

    // 注册进程退出信号处理，确保非正常退出时也能清理盘符
    // Ctrl+C / Ctrl+Break
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true; // 阻止立即终止，让 finally 块有机会执行清理
        Log.Information("[信号] 收到终止信号，正在卸载所有分区...");
        // ReSharper disable once AccessToDisposedClosure
    };
    // 进程退出（关闭控制台窗口等）— 最后的清理机会
    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        Log.Information("[退出] 进程即将退出，清理残留映射...");
        Log.CloseAndFlush();
    };

    // 启动时先扫描一次
    Log.Information("[启动] 扫描已连接的 USB 磁盘...");
    await mountService.MountAllUsbExt4Async();

    // 监听 USB 插拔
    usbWatcher.DeviceInserted += async () =>
    {
        try
        {
            // ReSharper disable once AccessToDisposedClosure
            await mountService.MountAllUsbExt4Async();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "挂载失败");
        }
    };
    usbWatcher.DeviceRemoved += async () =>
    {
        try
        {
            // ReSharper disable once AccessToDisposedClosure
            await mountService.UnmountStalePartitionsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "卸载失败");
        }
    };
    usbWatcher.Start();
    Log.Information("[运行中] 快捷键: Q=退出 S=重新扫描 L=列出已挂载分区");
    Log.Information("挂载后可直接在资源管理器中浏览和复制文件");
    while (true)
    {
        var key = Console.ReadKey(true);
        switch (char.ToUpper(key.KeyChar))
        {
            case 'Q':
                Log.Information("[退出] 正在卸载所有分区...");
                await mountService.UnmountAllAsync();
                return;
            case 'S':
                Log.Information("[扫描] 重新扫描所有磁盘...");
                var partitions = DiskManager.ScanForExt4Partitions();
                if (partitions.Count == 0)
                {
                    Log.Information("  未发现 ext4 分区");
                }
                else
                {
                    foreach (var p in partitions)
                    {
                        Log.Information("  {Description}", p.Description);
                    }
                }
                await mountService.MountAllUsbExt4Async();
                break;
            case 'L':
                Log.Information("[列表] 当前挂载的 ext4 驱动器可在资源管理器中查看");
                break;
        }
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "程序发生致命错误");
}
finally
{
    Log.CloseAndFlush();
}