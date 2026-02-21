using System.Management;
using Serilog;

namespace Ext4Mounter;

/// <summary>
/// 监听 USB 设备插拔事件
/// </summary>
public sealed class UsbWatcher : IDisposable
{
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;

    public void Dispose()
    {
        _insertWatcher?.Stop();
        _insertWatcher?.Dispose();
        _removeWatcher?.Stop();
        _removeWatcher?.Dispose();
    }

    public event Func<Task>? DeviceInserted;

    public event Func<Task>? DeviceRemoved;

    public void Start()
    {
        // 监听 USB 设备插入
        var insertQuery = new WqlEventQuery("__InstanceCreationEvent",
            TimeSpan.FromSeconds(2),
            "TargetInstance ISA 'Win32_DiskDrive' AND TargetInstance.InterfaceType='USB'");
        _insertWatcher = new(insertQuery);
        _insertWatcher.EventArrived += async (_, _) =>
        {
            Log.Information("[UsbWatcher] 检测到 USB 磁盘插入");
            // 延迟一下等系统识别完成
            await Task.Delay(2000);
            if (DeviceInserted != null)
            {
                await DeviceInserted.Invoke();
            }
        };
        _insertWatcher.Start();

        // 监听 USB 设备拔出
        var removeQuery = new WqlEventQuery("__InstanceDeletionEvent",
            TimeSpan.FromSeconds(2),
            "TargetInstance ISA 'Win32_DiskDrive' AND TargetInstance.InterfaceType='USB'");
        _removeWatcher = new(removeQuery);
        _removeWatcher.EventArrived += async (_, _) =>
        {
            Log.Information("[UsbWatcher] 检测到 USB 磁盘拔出");
            if (DeviceRemoved != null)
            {
                await DeviceRemoved.Invoke();
            }
        };
        _removeWatcher.Start();
        Log.Information("[UsbWatcher] USB 监听已启动");
    }
}