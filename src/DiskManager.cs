using System.Management;
using DiscUtils;
using DiscUtils.Ext;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using Serilog;

namespace Ext4Mounter;

/// <summary>
/// 扫描物理磁盘，检测 ext4 分区
/// </summary>
public static class DiskManager
{
    /// <summary>
    /// 扫描所有物理磁盘，返回包含 ext4 分区的信息
    /// </summary>
    public static List<Ext4PartitionInfo> ScanForExt4Partitions()
    {
        var results = new List<Ext4PartitionInfo>();

        // 通过 WMI 获取所有物理磁盘
        var diskNumbers = GetPhysicalDiskNumbers();
        foreach (var diskNumber in diskNumbers)
        {
            try
            {
                var partitions = ScanDisk(diskNumber);
                results.AddRange(partitions);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[DiskManager] 扫描磁盘 {DiskNumber} 失败", diskNumber);
            }
        }
        return results;
    }

    /// <summary>
    /// 扫描指定磁盘号的 ext4 分区
    /// </summary>
    public static List<Ext4PartitionInfo> ScanDisk(int diskNumber)
    {
        var results = new List<Ext4PartitionInfo>();
        var diskPath = $@"\\.\PhysicalDrive{diskNumber}";

        // 获取磁盘大小
        long diskSize;
        try
        {
            diskSize = GetDiskSize(diskNumber);
        }
        catch
        {
            diskSize = 0;
        }
        if (diskSize == 0)
        {
            Log.Warning("[DiskManager] 磁盘 {DiskNumber}: 无法获取磁盘大小", diskNumber);
            return results;
        }
        Log.Information("[DiskManager] 扫描磁盘 {DiskNumber} ({DiskSize})...", diskNumber, FormatSize(diskSize));
        using var diskStream = new PhysicalDiskStream(diskPath, diskSize);

        // 尝试读取分区表
        PartitionTable? partTable;

        // 先尝试 GPT
        try
        {
            diskStream.Position = 0;
            var geometry = new Geometry(diskSize / 512, 255, 63, 512);
            partTable = new GuidPartitionTable(diskStream, geometry);
            if (partTable.Count == 0)
            {
                Log.Information("[DiskManager] 磁盘 {DiskNumber}: GPT 分区表为空", diskNumber);
                partTable = null;
            }
            else
            {
                Log.Information("[DiskManager] 磁盘 {DiskNumber}: 检测到 GPT 分区表, {Count} 个分区", diskNumber, partTable.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("[DiskManager] 磁盘 {DiskNumber}: GPT 解析失败 ({Message})", diskNumber, ex.Message);
            partTable = null;
        }

        // 再尝试 MBR
        if (partTable == null)
        {
            try
            {
                diskStream.Position = 0;
                partTable = new BiosPartitionTable(diskStream);
                if (partTable.Count == 0)
                {
                    Log.Information("[DiskManager] 磁盘 {DiskNumber}: MBR 分区表为空", diskNumber);
                    partTable = null;
                }
                else
                {
                    Log.Information("[DiskManager] 磁盘 {DiskNumber}: 检测到 MBR 分区表, {Count} 个分区", diskNumber, partTable.Count);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[DiskManager] 磁盘 {DiskNumber}: MBR 解析失败 ({Message})", diskNumber, ex.Message);
                partTable = null;
            }
        }

        // 有分区表，逐个检查
        if (partTable != null)
        {
            ScanPartitionTable(partTable, diskNumber, results);
        }

        // 没有分区表或分区表中没找到 ext4 → 尝试整盘当 ext4 打开
        if (results.Count == 0)
        {
            Log.Information("[DiskManager] 磁盘 {DiskNumber}: 尝试整盘作为 ext4 打开...", diskNumber);
            try
            {
                diskStream.Position = 0;
                using var ext4 = new ExtFileSystem(diskStream);
                // 成功！整盘就是 ext4
                results.Add(new(diskNumber,
                    0,
                    diskSize,
                    $"磁盘 {diskNumber}, 整盘 ext4 ({FormatSize(diskSize)})"));
                Log.Information("[DiskManager] 磁盘 {DiskNumber}: 整盘 ext4 检测成功!", diskNumber);
            }
            catch (Exception ex)
            {
                Log.Debug("[DiskManager] 磁盘 {DiskNumber}: 整盘 ext4 检测失败 ({Message})", diskNumber, ex.Message);
            }
        }
        return results;
    }

    private static void ScanPartitionTable(PartitionTable partTable,
        int diskNumber,
        List<Ext4PartitionInfo> results)
    {
        for (var i = 0; i < partTable.Count; i++)
        {
            var partition = partTable.Partitions[i];
            try
            {
                using var partStream = partition.Open();
                // 尝试打开为 ext4，如果成功说明是 ext 文件系统
                using var ext4 = new ExtFileSystem(partStream);
                // 如果没抛异常，说明是 ext4
                results.Add(new(diskNumber,
                    partition.FirstSector * 512,
                    partition.SectorCount * 512,
                    $"磁盘 {diskNumber}, 分区 {i + 1} (ext4, {FormatSize(partition.SectorCount * 512)})"));
            }
            catch
            {
                // 不是 ext4 或无法打开，跳过
            }
        }
    }

    /// <summary>
    /// 打开 ext4 文件系统
    /// </summary>
    public static ExtFileSystem? OpenExt4(int diskNumber, long partitionOffset, long partitionLength)
    {
        var diskPath = $@"\\.\PhysicalDrive{diskNumber}";
        long diskSize;
        try
        {
            diskSize = GetDiskSize(diskNumber);
        }
        catch
        {
            diskSize = partitionOffset + partitionLength;
        }
        try
        {
            var diskStream = new PhysicalDiskStream(diskPath, diskSize);
            if (partitionOffset == 0 && partitionLength == diskSize)
            {
                // 整盘 ext4
                return new(diskStream);
            }
            // 分区子流
            var partStream = new SubStream(diskStream, partitionOffset, partitionLength);
            return new(partStream);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DiskManager] 打开 ext4 失败");
            return null;
        }
    }

    /// <summary>
    /// 获取外接磁盘（USB/移动硬盘）的磁盘号列表
    /// 通过 MediaType 和 PNPDeviceID 综合判断，兼容 USB/UASP/JMicron 等桥接芯片
    /// </summary>
    public static HashSet<int> GetUsbDiskNumbers()
    {
        var usbDisks = new HashSet<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, InterfaceType, MediaType, PNPDeviceID FROM Win32_DiskDrive");
            foreach (var disk in searcher.Get())
            {
                var deviceId = disk["DeviceID"]?.ToString();
                var interfaceType = disk["InterfaceType"]?.ToString() ?? "";
                var mediaType = disk["MediaType"]?.ToString() ?? "";
                var pnpDeviceId = disk["PNPDeviceID"]?.ToString() ?? "";

                // 判断是否为外接磁盘:
                // 1. InterfaceType 为 USB
                // 2. MediaType 包含 "External"（USB 转 SCSI 桥接芯片的情况）
                // 3. PNPDeviceID 包含 "USB" 或 "USBSTOR"
                var isExternal = interfaceType.Equals("USB", StringComparison.OrdinalIgnoreCase) || mediaType.Contains("External", StringComparison.OrdinalIgnoreCase) || pnpDeviceId.Contains("USB", StringComparison.OrdinalIgnoreCase) || pnpDeviceId.Contains("USBSTOR", StringComparison.OrdinalIgnoreCase);
                if (isExternal && deviceId != null && TryParseDiskNumber(deviceId, out var num))
                {
                    usbDisks.Add(num);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DiskManager] 获取 USB 磁盘列表失败");
        }
        return usbDisks;
    }

    private static List<int> GetPhysicalDiskNumbers()
    {
        var numbers = new List<int>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive");
            foreach (var disk in searcher.Get())
            {
                var deviceId = disk["DeviceID"]?.ToString();
                if (deviceId != null && TryParseDiskNumber(deviceId, out var num))
                {
                    numbers.Add(num);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[DiskManager] 获取磁盘列表失败");
        }
        return numbers;
    }

    private static bool TryParseDiskNumber(string deviceId, out int number)
    {
        // DeviceID 格式: \\.\PHYSICALDRIVE0
        number = 0;
        const string prefix = @"\\.\PHYSICALDRIVE";
        return deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(deviceId[prefix.Length..], out number);
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

    private static long GetDiskSize(int diskNumber)
    {
        using var searcher = new ManagementObjectSearcher($@"SELECT Size FROM Win32_DiskDrive WHERE DeviceID='\\\\.\\PHYSICALDRIVE{diskNumber}'");
        foreach (var disk in searcher.Get())
        {
            var size = disk["Size"];
            if (size != null && long.TryParse(size.ToString(), out var result))
            {
                return result;
            }
        }
        return 0;
    }

    public record Ext4PartitionInfo(
        int DiskNumber,
        long PartitionOffset,
        long PartitionLength,
        string Description
    );
}