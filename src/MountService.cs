using System.Diagnostics;
using DiscUtils.Ext;
using Serilog;

// ReSharper disable UseRawString

namespace Ext4Mounter;

/// <summary>
/// 协调 ext4 分区的挂载和卸载（ProjFS 虚拟化方案）
/// </summary>
public sealed class MountService : IDisposable
{
    private readonly Lock _lock = new();

    private readonly List<MountedPartition> _mounted = [];

    public void Dispose()
    {
        UnmountAll();
    }

    /// <summary>
    /// 扫描并挂载所有 USB 磁盘上的 ext4 分区
    /// </summary>
    public void MountAllUsbExt4()
    {
        var usbDisks = DiskManager.GetUsbDiskNumbers();
        if (usbDisks.Count == 0)
        {
            Log.Information("[MountService] 未发现 USB 磁盘");
            return;
        }
        foreach (var diskNumber in usbDisks)
        {
            try
            {
                var partitions = DiskManager.ScanDisk(diskNumber);
                foreach (var part in partitions)
                {
                    MountPartition(part);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MountService] 扫描磁盘 {DiskNumber} 失败", diskNumber);
            }
        }
    }

    /// <summary>
    /// 卸载所有不再存在的 USB 磁盘上的分区
    /// </summary>
    public void UnmountStalePartitions()
    {
        lock (_lock)
        {
            var currentUsbDisks = DiskManager.GetUsbDiskNumbers();
            var toRemove = _mounted.Where(m => !currentUsbDisks.Contains(m.DiskNumber)).ToList();
            foreach (var mount in toRemove)
            {
                try
                {
                    Log.Information("[MountService] 卸载 {DriveLetter}:", mount.DriveLetter);
                    DriveMapper.UnmapLocalDrive(mount.DriveLetter);
                    mount.Provider.Dispose();
                    CleanupVirtualizationRoot(mount.Provider.VirtualizationRoot);
                    mount.FileSystem.Dispose();
                    _mounted.Remove(mount);
                    Log.Information("[MountService] {DriveLetter}: 已卸载", mount.DriveLetter);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MountService] 卸载 {DriveLetter}: 失败", mount.DriveLetter);
                    _mounted.Remove(mount);
                }
            }
        }
    }

    /// <summary>
    /// 卸载所有已挂载的分区
    /// </summary>
    public void UnmountAll()
    {
        lock (_lock)
        {
            foreach (var mount in _mounted.ToList())
            {
                try
                {
                    Log.Information("[MountService] 卸载 {DriveLetter}:", mount.DriveLetter);
                    DriveMapper.UnmapLocalDrive(mount.DriveLetter);
                    mount.Provider.Dispose();
                    CleanupVirtualizationRoot(mount.Provider.VirtualizationRoot);
                    mount.FileSystem.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[MountService] 卸载失败");
                }
            }
            _mounted.Clear();
        }
    }

    private void MountPartition(DiskManager.Ext4PartitionInfo partInfo)
    {
        lock (_lock)
        {
            // 检查是否已挂载
            if (_mounted.Any(m => m.DiskNumber == partInfo.DiskNumber &&
                                  m.PartitionOffset == partInfo.PartitionOffset))
            {
                return;
            }
            var ext4 = DiskManager.OpenExt4(partInfo.DiskNumber, partInfo.PartitionOffset, partInfo.PartitionLength);
            if (ext4 == null)
            {
                Log.Warning("[MountService] 无法打开 ext4: {Description}", partInfo.Description);
                return;
            }
            try
            {
                // 创建虚拟化根目录（临时文件夹）
                var virtRoot = Path.Combine(Path.GetTempPath(), $"ext4mount_{partInfo.DiskNumber}_{partInfo.PartitionOffset}");
                CleanupVirtualizationRoot(virtRoot);
                Directory.CreateDirectory(virtRoot);
                var provider = new ProjFSProvider(ext4, virtRoot);
                provider.Start();
                var driveLetter = DriveMapper.MapLocalFolder(virtRoot);
                if (driveLetter == null)
                {
                    provider.Dispose();
                    CleanupVirtualizationRoot(virtRoot);
                    ext4.Dispose();
                    Log.Warning("[MountService] 映射盘符失败: {Description}", partInfo.Description);
                    return;
                }
                _mounted.Add(new(partInfo.DiskNumber,
                    partInfo.PartitionOffset,
                    driveLetter.Value,
                    provider,
                    ext4));
                Log.Information("[MountService] {Description} -> {DriveLetter}:", partInfo.Description, driveLetter);

                // 输出 ext4 分区的真实空间信息
                try
                {
                    var totalSize = ext4.Size;
                    var usedSpace = ext4.UsedSpace;
                    var availSpace = ext4.AvailableSpace;
                    Log.Information("[MountService]   分区大小: {TotalSize}, 已用: {UsedSpace}, 可用: {AvailSpace}",
                        FormatSize(totalSize), FormatSize(usedSpace), FormatSize(availSpace));
                    Log.Warning("[MountService]   注意: 资源管理器中显示的磁盘大小为本地缓存盘大小，非 ext4 分区真实大小");
                }
                catch
                {
                    // 获取空间信息失败不影响挂载，继续显示挂载成功即可
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MountService] 挂载失败");
                ext4.Dispose();
            }
        }
    }

    private static void CleanupVirtualizationRoot(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }
        try
        {
            // ProjFS 虚拟化根目录带有 reparse point，普通 Directory.Delete 可能失败
            // 使用 rmdir /s /q 更可靠地清理
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $@"/c rmdir /s /q ""{path}""",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);

            // 等待文件系统释放
            Thread.Sleep(200);
        }
        catch
        {
            // ignore any errors, we'll try .NET API as a fallback
        }

        // 如果 rmdir 失败，回退到 .NET API
        if (!Directory.Exists(path))
        {
            return;
        }
        try
        {
            Directory.Delete(path, true);
        }
        catch
        {
            // 最后尝试一次，忽略失败
        }
        Thread.Sleep(200);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return $"{size:F1} {units[unit]}";
    }

    private record MountedPartition(
        int DiskNumber,
        long PartitionOffset,
        char DriveLetter,
        ProjFSProvider Provider,
        ExtFileSystem FileSystem
    );
}