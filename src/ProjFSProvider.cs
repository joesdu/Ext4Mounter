using System.Diagnostics;
using System.Runtime.InteropServices;
using DiscUtils.Ext;
using Serilog;

// ReSharper disable UnusedMember.Global

namespace Ext4Mounter;

internal static class ProjFSNative
{
    private const string ProjFsDll = "ProjectedFSLib.dll";

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern int PrjStartVirtualizing(
        string virtualizationRootPath,
        ref PrjCallbacks callbacks,
        IntPtr instanceContext,
        IntPtr options,
        ref IntPtr namespaceVirtualizationContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern void PrjStopVirtualizing(IntPtr namespaceVirtualizationContext);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern int PrjMarkDirectoryAsPlaceholder(
        string rootPathName,
        string? targetPathName,
        IntPtr versionInfo,
        ref Guid virtualizationInstanceID);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern int PrjWritePlaceholderInfo(
        IntPtr namespaceVirtualizationContext,
        string destinationFileName,
        ref PrjPlaceholderInfo placeholderInfo,
        uint placeholderInfoSize);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern int PrjWriteFileData(
        IntPtr namespaceVirtualizationContext,
        ref Guid dataStreamId,
        IntPtr buffer,
        ulong byteOffset,
        uint length);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern IntPtr PrjAllocateAlignedBuffer(IntPtr namespaceVirtualizationContext, uint size);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern void PrjFreeAlignedBuffer(IntPtr buffer);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern int PrjFillDirEntryBuffer(
        string fileName,
        ref PrjFileBasicInfo fileBasicInfo,
        IntPtr dirEntryBufferHandle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrjFileNameMatch(string fileNameToCheck, string pattern);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    internal static extern int PrjFileNameCompare(string fileName1, string fileName2);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport(ProjFsDll, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PrjDoesNameContainWildCards(string fileName);
}

internal enum PrjCallbackDataFlags : uint
{
    None = 0,
    RestartScan = 1
}

internal enum PrjNotification : uint
{
    FileOpened = 0x00000002,
    NewFileCreated = 0x00000004,
    FileOverwritten = 0x00000008,
    PreDelete = 0x00000010,
    PreRename = 0x00000020,
    PreSetHardlink = 0x00000040,
    FileRenamed = 0x00000080,
    HardlinkCreated = 0x00000100,
    FileHandleClosedNoModification = 0x00000200,
    FileHandleClosedFileModified = 0x00000400,
    FileHandleClosedFileDeleted = 0x00000800,
    FilePreConvertToFull = 0x00001000
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrjCallbacks
{
    internal PrjStartDirectoryEnumerationCb StartDirectoryEnumerationCallback;
    internal PrjEndDirectoryEnumerationCb EndDirectoryEnumerationCallback;
    internal PrjGetDirectoryEnumerationCb GetDirectoryEnumerationCallback;
    internal PrjGetPlaceholderInfoCb GetPlaceholderInfoCallback;
    internal PrjGetFileDataCb GetFileDataCallback;
    internal PrjQueryFileNameCb? QueryFileNameCallback;
    internal PrjNotificationCb? NotificationCallback;
    internal PrjCancelCommandCb? CancelCommandCallback;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct PrjCallbackData
{
    internal uint Size;
    internal uint Flags;
    internal IntPtr NamespaceVirtualizationContext;
    internal int CommandId;
    internal Guid FileId;
    internal Guid DataStreamId;

    [MarshalAs(UnmanagedType.LPWStr)]
    internal string FilePathName;

    internal IntPtr VersionInfo;
    internal uint TriggeringProcessId;

    [MarshalAs(UnmanagedType.LPWStr)]
    internal string TriggeringProcessImageFileName;

    internal IntPtr InstanceContext;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrjFileBasicInfo
{
    [MarshalAs(UnmanagedType.U1)]
    internal bool IsDirectory;

    internal long FileSize;
    internal long CreationTime;
    internal long LastAccessTime;
    internal long LastWriteTime;
    internal long ChangeTime;
    internal FileAttributes FileAttributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrjPlaceholderInfo
{
    internal PrjFileBasicInfo FileBasicInfo;
    internal uint EaBufferSize;
    internal uint OffsetToFirstEa;
    internal uint SecurityBufferSize;
    internal uint OffsetToSecurityDescriptor;
    internal uint StreamsInfoBufferSize;
    internal uint OffsetToFirstStreamInfo;
    internal PrjPlaceholderVersionInfo VersionInfo;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
    internal byte[] VariableData;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PrjPlaceholderVersionInfo
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    internal byte[] ProviderID;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
    internal byte[] ContentID;
}

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjStartDirectoryEnumerationCb(PrjCallbackData callbackData, ref Guid enumerationId);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjEndDirectoryEnumerationCb(PrjCallbackData callbackData, ref Guid enumerationId);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjGetDirectoryEnumerationCb(
    PrjCallbackData callbackData,
    ref Guid enumerationId,
    string searchExpression,
    IntPtr dirEntryBufferHandle);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjGetPlaceholderInfoCb(PrjCallbackData callbackData);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjGetFileDataCb(PrjCallbackData callbackData, ulong byteOffset, uint length);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjQueryFileNameCb(PrjCallbackData callbackData);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjNotificationCb(
    PrjCallbackData callbackData,
    [MarshalAs(UnmanagedType.U1)]
    bool isDirectory,
    PrjNotification notification,
    string? destinationFileName,
    IntPtr operationParameters);

[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
internal delegate int PrjCancelCommandCb(PrjCallbackData callbackData);

public sealed class ProjFSProvider : IDisposable
{
    private const int S_OK = 0;
    private const int E_FILENOTFOUND = unchecked((int)0x80070002);
    private const int E_OUTOFMEMORY = unchecked((int)0x8007000E);
    private const int ChunkSize = 4 * 1024 * 1024; // 4MB — 减少 ProjFS 回调次数
    private const int MaxCachedStreams = 64;
    private readonly PrjEndDirectoryEnumerationCb _endDirectoryEnumerationCb;
    private readonly Dictionary<Guid, EnumState> _enumerations = new();

    private readonly ExtFileSystem _ext4;
    private readonly ReaderWriterLockSlim _fsLock = new();
    private readonly PrjGetDirectoryEnumerationCb _getDirectoryEnumerationCb;
    private readonly PrjGetFileDataCb _getFileDataCb;
    private readonly PrjGetPlaceholderInfoCb _getPlaceholderInfoCb;

    private readonly PrjStartDirectoryEnumerationCb _startDirectoryEnumerationCb;

    // 文件流缓存：避免每次 GetFileData 回调都重新打开文件（由 _fsLock 保护）
    private readonly Dictionary<string, CachedStream> _streamCache = new();

    private IntPtr _namespaceContext;
    private bool _started;

    public ProjFSProvider(ExtFileSystem ext4, string virtualizationRoot)
    {
        _ext4 = ext4 ?? throw new ArgumentNullException(nameof(ext4));
        if (string.IsNullOrWhiteSpace(virtualizationRoot))
        {
            throw new ArgumentException("虚拟化根路径不能为空", nameof(virtualizationRoot));
        }
        VirtualizationRoot = Path.GetFullPath(virtualizationRoot);
        Directory.CreateDirectory(VirtualizationRoot);
        var instanceId = Guid.NewGuid();
        _startDirectoryEnumerationCb = StartDirectoryEnumeration;
        _endDirectoryEnumerationCb = EndDirectoryEnumeration;
        _getDirectoryEnumerationCb = GetDirectoryEnumeration;
        _getPlaceholderInfoCb = GetPlaceholderInfo;
        _getFileDataCb = GetFileData;
        var hr = ProjFSNative.PrjMarkDirectoryAsPlaceholder(VirtualizationRoot, null, IntPtr.Zero, ref instanceId);
        if (hr != S_OK)
        {
            throw new InvalidOperationException($"PrjMarkDirectoryAsPlaceholder 失败: 0x{hr:X8}");
        }
    }

    public string VirtualizationRoot { get; }

    public void Dispose()
    {
        Stop();
    }

    /// <summary>
    /// 检测 Windows Projected File System 可选功能是否已启用。
    /// 通过尝试加载 ProjectedFSLib.dll 来判断。
    /// </summary>
    public static bool IsProjFSAvailable()
    {
        try
        {
            var handle = NativeLibrary.Load("ProjectedFSLib.dll", typeof(ProjFSProvider).Assembly, DllImportSearchPath.System32);
            NativeLibrary.Free(handle);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 打印 ProjFS 功能未启用的提示信息，指导用户如何启用。
    /// </summary>
    public static void PrintEnableInstructions()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("""
                          ╔═════════════════════════════════════════════════════════════════════════════════════
                          ║  Windows Projected File System (ProjFS) 功能未启用!
                          ║
                          ║  请以管理员身份运行以下命令启用 ProjFS:
                          ║
                          ║  PowerShell:
                          ║    Enable-WindowsOptionalFeature -Online -FeatureName Client-ProjFS -NoRestart
                          ║
                          ║  或通过 DISM:
                          ║    dism /online /enable-feature /featurename:Client-ProjFS
                          ║
                          ║  启用后可能需要重启电脑,然后重新运行本程序.
                          ╚═════════════════════════════════════════════════════════════════════════════════════
                          """);
        Console.ResetColor();
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }
        var callbacks = new PrjCallbacks
        {
            StartDirectoryEnumerationCallback = _startDirectoryEnumerationCb,
            EndDirectoryEnumerationCallback = _endDirectoryEnumerationCb,
            GetDirectoryEnumerationCallback = _getDirectoryEnumerationCb,
            GetPlaceholderInfoCallback = _getPlaceholderInfoCb,
            GetFileDataCallback = _getFileDataCb,
            QueryFileNameCallback = null,
            NotificationCallback = null,
            CancelCommandCallback = null
        };
        var context = IntPtr.Zero;
        var hr = ProjFSNative.PrjStartVirtualizing(VirtualizationRoot, ref callbacks, IntPtr.Zero, IntPtr.Zero, ref context);
        if (hr != S_OK)
        {
            throw new InvalidOperationException($"PrjStartVirtualizing 失败: 0x{hr:X8}");
        }
        _namespaceContext = context;
        _started = true;
        Log.Information("[ProjFS] 虚拟化已启动: {VirtualizationRoot}", VirtualizationRoot);
    }

    private void Stop()
    {
        if (!_started)
        {
            return;
        }
        ProjFSNative.PrjStopVirtualizing(_namespaceContext);
        _namespaceContext = IntPtr.Zero;
        _started = false;
        _fsLock.EnterWriteLock();
        try
        {
            ClearStreamCache();
        }
        finally
        {
            _fsLock.ExitWriteLock();
        }
        lock (_enumerations)
        {
            _enumerations.Clear();
        }
        Log.Information("[ProjFS] 虚拟化已停止");
    }

    private int StartDirectoryEnumeration(PrjCallbackData callbackData, ref Guid enumerationId)
    {
        lock (_enumerations)
        {
            _enumerations[enumerationId] = new()
            {
                CurrentIndex = 0,
                SearchExpression = null,
                Entries = null
            };
        }
        return S_OK;
    }

    private int EndDirectoryEnumeration(PrjCallbackData callbackData, ref Guid enumerationId)
    {
        lock (_enumerations)
        {
            _enumerations.Remove(enumerationId);
        }
        return S_OK;
    }

    private int GetDirectoryEnumeration(
        PrjCallbackData callbackData,
        ref Guid enumerationId,
        string searchExpression,
        IntPtr dirEntryBufferHandle)
    {
        EnumState state;
        lock (_enumerations)
        {
            if (!_enumerations.TryGetValue(enumerationId, out state!))
            {
                state = new();
                _enumerations[enumerationId] = state;
            }
        }
        var restartScan = (callbackData.Flags & (uint)PrjCallbackDataFlags.RestartScan) != 0;
        var normalizedSearch = string.IsNullOrEmpty(searchExpression) ? null : searchExpression;
        if (restartScan)
        {
            state.CurrentIndex = 0;
        }
        if (state.Entries == null || restartScan || state.SearchExpression != normalizedSearch)
        {
            var unixPath = NormalizeToUnixPath(callbackData.FilePathName);
            var builtEntries = new List<(string Name, bool IsDirectory, long Size, DateTime LastWrite)>();
            _fsLock.EnterReadLock();
            try
            {
                if (_ext4.DirectoryExists(unixPath))
                {
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach (var dirPath in _ext4.GetDirectories(unixPath))
                    {
                        var name = Path.GetFileName(dirPath.TrimEnd('/'));
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }
                        var info = _ext4.GetDirectoryInfo(dirPath);
                        builtEntries.Add((name, true, 0, info.LastWriteTimeUtc));
                    }
                    // ReSharper disable once LoopCanBeConvertedToQuery
                    foreach (var filePath in _ext4.GetFiles(unixPath))
                    {
                        var name = Path.GetFileName(filePath);
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }
                        var info = _ext4.GetFileInfo(filePath);
                        var size = _ext4.GetFileLength(filePath);
                        builtEntries.Add((name, false, size, info.LastWriteTimeUtc));
                    }
                }
            }
            finally
            {
                _fsLock.ExitReadLock();
            }
            builtEntries.Sort((a, b) => ProjFSNative.PrjFileNameCompare(a.Name, b.Name));
            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (!string.IsNullOrEmpty(normalizedSearch) && ProjFSNative.PrjDoesNameContainWildCards(normalizedSearch))
            {
                builtEntries = builtEntries
                               .Where(e => ProjFSNative.PrjFileNameMatch(e.Name, normalizedSearch))
                               .ToList();
            }
            else if (!string.IsNullOrEmpty(normalizedSearch))
            {
                builtEntries = builtEntries
                               .Where(e => ProjFSNative.PrjFileNameCompare(e.Name, normalizedSearch) == 0)
                               .ToList();
            }
            state.Entries = builtEntries;
            state.SearchExpression = normalizedSearch;
            state.CurrentIndex = 0;
        }
        var entries = state.Entries;
        if (entries == null)
        {
            return S_OK;
        }
        while (state.CurrentIndex < entries.Count)
        {
            var entry = entries[state.CurrentIndex];
            var fileTime = entry.LastWrite == DateTime.MinValue
                               ? DateTime.UtcNow.ToFileTimeUtc()
                               : entry.LastWrite.ToFileTimeUtc();
            var basicInfo = new PrjFileBasicInfo
            {
                IsDirectory = entry.IsDirectory,
                FileSize = entry.IsDirectory ? 0 : entry.Size,
                CreationTime = fileTime,
                LastAccessTime = fileTime,
                LastWriteTime = fileTime,
                ChangeTime = fileTime,
                FileAttributes = entry.IsDirectory
                                     ? FileAttributes.Directory | FileAttributes.ReadOnly
                                     : FileAttributes.ReadOnly
            };
            var fillHr = ProjFSNative.PrjFillDirEntryBuffer(entry.Name, ref basicInfo, dirEntryBufferHandle);
            if (fillHr != S_OK)
            {
                return S_OK;
            }
            state.CurrentIndex++;
        }
        return S_OK;
    }

    private int GetPlaceholderInfo(PrjCallbackData callbackData)
    {
        var sw = Stopwatch.StartNew();
        var unixPath = NormalizeToUnixPath(callbackData.FilePathName);
        bool isDirectory;
        long size;
        DateTime lastWriteUtc;
        _fsLock.EnterReadLock();
        try
        {
            if (_ext4.DirectoryExists(unixPath))
            {
                isDirectory = true;
                size = 0;
                lastWriteUtc = _ext4.GetDirectoryInfo(unixPath).LastWriteTimeUtc;
            }
            else if (_ext4.FileExists(unixPath))
            {
                isDirectory = false;
                size = _ext4.GetFileLength(unixPath);
                lastWriteUtc = _ext4.GetFileInfo(unixPath).LastWriteTimeUtc;
            }
            else
            {
                return E_FILENOTFOUND;
            }
        }
        finally
        {
            _fsLock.ExitReadLock();
        }
        var fileTime = lastWriteUtc == DateTime.MinValue
                           ? DateTime.UtcNow.ToFileTimeUtc()
                           : lastWriteUtc.ToFileTimeUtc();
        var placeholderInfo = new PrjPlaceholderInfo
        {
            FileBasicInfo = new()
            {
                IsDirectory = isDirectory,
                FileSize = isDirectory ? 0 : size,
                CreationTime = fileTime,
                LastAccessTime = fileTime,
                LastWriteTime = fileTime,
                ChangeTime = fileTime,
                FileAttributes = isDirectory
                                     ? FileAttributes.Directory | FileAttributes.ReadOnly
                                     : FileAttributes.ReadOnly
            },
            EaBufferSize = 0,
            OffsetToFirstEa = 0,
            SecurityBufferSize = 0,
            OffsetToSecurityDescriptor = 0,
            StreamsInfoBufferSize = 0,
            OffsetToFirstStreamInfo = 0,
            VersionInfo = new()
            {
                ProviderID = new byte[128],
                ContentID = new byte[128]
            },
            VariableData = new byte[1]
        };
        var infoSize = (uint)Marshal.SizeOf<PrjPlaceholderInfo>();
        var hr2 = ProjFSNative.PrjWritePlaceholderInfo(callbackData.NamespaceVirtualizationContext,
            callbackData.FilePathName,
            ref placeholderInfo,
            infoSize);
        sw.Stop();
        Log.Debug("[ProjFS] PlaceholderInfo: {UnixPath} ({Type}) {ElapsedMs}ms", unixPath, isDirectory ? "DIR" : FormatSize(size), sw.ElapsedMilliseconds);
        return hr2;
    }

    private int GetFileData(PrjCallbackData callbackData, ulong byteOffset, uint length)
    {
        if (length == 0)
        {
            return S_OK;
        }
        var sw = Stopwatch.StartNew();
        var unixPath = NormalizeToUnixPath(callbackData.FilePathName);
        var managedBuffer = new byte[ChunkSize];
        ulong remaining = length;
        var writeOffset = byteOffset;
        long totalRead = 0;
        long lockWaitMs = 0;
        while (remaining > 0)
        {
            var chunkLength = (int)Math.Min(ChunkSize, remaining);
            int read;

            // 在写锁内读取 ext4 数据到 managedBuffer（需要写锁：可能写入流缓存 + 修改流 Position）
            var lockSw = Stopwatch.StartNew();
            _fsLock.EnterWriteLock();
            try
            {
                lockWaitMs += lockSw.ElapsedMilliseconds;
                var stream = GetOrOpenCachedStream(unixPath);
                if (stream == null)
                {
                    return E_FILENOTFOUND;
                }
                if ((long)writeOffset >= stream.Length)
                {
                    break;
                }
                stream.Position = (long)writeOffset;
                read = stream.Read(managedBuffer, 0, chunkLength);
            }
            finally
            {
                _fsLock.ExitWriteLock();
            }
            if (read <= 0)
            {
                break;
            }

            // 锁外：分配对齐缓冲区、拷贝数据、写入 ProjFS
            var alignedBuffer = ProjFSNative.PrjAllocateAlignedBuffer(callbackData.NamespaceVirtualizationContext, (uint)read);
            if (alignedBuffer == IntPtr.Zero)
            {
                return E_OUTOFMEMORY;
            }
            try
            {
                Marshal.Copy(managedBuffer, 0, alignedBuffer, read);
                var hr = ProjFSNative.PrjWriteFileData(callbackData.NamespaceVirtualizationContext,
                    ref callbackData.DataStreamId,
                    alignedBuffer,
                    writeOffset,
                    (uint)read);
                if (hr != S_OK)
                {
                    return hr;
                }
            }
            finally
            {
                ProjFSNative.PrjFreeAlignedBuffer(alignedBuffer);
            }
            writeOffset += (ulong)read;
            remaining -= (ulong)read;
            totalRead += read;
        }
        sw.Stop();
        var speed = sw.ElapsedMilliseconds > 0 ? (totalRead * 1000) / sw.ElapsedMilliseconds : 0;
        Log.Debug("[ProjFS] FileData: {UnixPath} offset={ByteOffset} req={ReqSize} read={ReadSize} {ElapsedMs}ms ({Speed}/s) lockWait={LockWaitMs}ms", unixPath, byteOffset, FormatSize(length), FormatSize(totalRead), sw.ElapsedMilliseconds, FormatSize(speed), lockWaitMs);
        return S_OK;
    }

    /// <summary>
    /// 获取或打开缓存的文件流。必须在 _fsLock 写锁内调用。
    /// </summary>
    private Stream? GetOrOpenCachedStream(string unixPath)
    {
        if (_streamCache.TryGetValue(unixPath, out var cached))
        {
            cached.LastAccess = Environment.TickCount64;
            return cached.Stream;
        }

        // 不在缓存中，打开新流
        if (!_ext4.FileExists(unixPath))
        {
            return null;
        }
        var stream = _ext4.OpenFile(unixPath, FileMode.Open, FileAccess.Read);

        // 驱逐最旧缓存项
        while (_streamCache.Count >= MaxCachedStreams)
        {
            string? oldestKey = null;
            var oldestTime = long.MaxValue;
            foreach (var kvp in _streamCache.Where(kvp => kvp.Value.LastAccess < oldestTime))
            {
                oldestTime = kvp.Value.LastAccess;
                oldestKey = kvp.Key;
            }
            if (oldestKey == null)
            {
                continue;
            }
            _streamCache[oldestKey].Stream.Dispose();
            _streamCache.Remove(oldestKey);
        }
        _streamCache[unixPath] = new()
        {
            Stream = stream,
            LastAccess = Environment.TickCount64
        };
        return stream;
    }

    /// <summary>
    /// 清理所有缓存的文件流。必须在 _fsLock 写锁内调用。
    /// </summary>
    private void ClearStreamCache()
    {
        foreach (var cached in _streamCache.Values)
        {
            try
            {
                cached.Stream.Dispose();
            }
            catch
            {
                // 忽略关闭流时的异常
            }
        }
        _streamCache.Clear();
    }

    private static string NormalizeToUnixPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }
        var normalized = path.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized == "/")
        {
            return "/";
        }
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }
        return normalized.TrimEnd('/');
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

    private class EnumState
    {
        public int CurrentIndex;
        public List<(string Name, bool IsDirectory, long Size, DateTime LastWrite)>? Entries;
        public string? SearchExpression;
    }

    private class CachedStream
    {
        public long LastAccess;
        public required Stream Stream;
    }
}