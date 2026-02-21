using System.Diagnostics;
using System.Runtime.InteropServices;
using Serilog;

namespace Ext4Mounter;

/// <summary>
/// 映射/取消映射本地文件夹为驱动器盘符。
/// 由于程序以管理员身份运行，直接 subst/DefineDosDevice 创建的盘符
/// 只在提权会话中可见，资源管理器（非提权）看不到。
/// 因此优先通过 Task Scheduler 以非提权身份执行 subst，使盘符跨会话可见。
/// </summary>
public static class DriveMapper
{
    private const int DDD_REMOVE_DEFINITION = 2;
    private static readonly char[] CandidateLetters = "ZYXWVUTSRQPONMLKJIHGFED".ToCharArray();
    private static readonly HashSet<char> _mappedByUs = [];

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DefineDosDeviceW(int dwFlags, string lpDeviceName, string? lpTargetPath);

    /// <summary>
    /// 将本地文件夹映射为驱动器盘符（用于 ProjFS 虚拟化根目录）
    /// 优先以非提权身份执行 subst，使资源管理器可见
    /// </summary>
    public static char? MapLocalFolder(string folderPath)
    {
        var letter = FindAvailableDriveLetter();
        if (letter == null)
        {
            Log.Warning("[DriveMapper] 没有可用的盘符");
            return null;
        }
        var fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
        {
            Log.Warning("[DriveMapper] 文件夹不存在: {FullPath}", fullPath);
            return null;
        }

        // 方案1: 以非提权身份运行 subst（解决管理员/普通用户会话隔离问题）
        // ReSharper disable once UseRawString
        var result = RunCommandAsNonElevated("subst", $@"{letter}: ""{fullPath}""");
        if (result.ExitCode == 0)
        {
            _mappedByUs.Add(letter.Value);
            Log.Information("[DriveMapper] 已映射 {FullPath} -> {Letter}: (非提权 subst)", fullPath, letter);

            // 同时在提权会话中也创建映射，让本进程也能访问
            DefineDosDeviceW(0, $"{letter}:", fullPath);
            return letter;
        }
        Log.Debug("[DriveMapper] 非提权 subst 失败: {Output}", result.Output.Trim());

        // 方案2: 直接 subst（仅提权会话可见）
        // ReSharper disable once UseRawString
        result = RunCommand("subst", $@"{letter}: ""{fullPath}""");
        if (result.ExitCode == 0)
        {
            _mappedByUs.Add(letter.Value);
            Log.Information("[DriveMapper] 已映射 {FullPath} -> {Letter}: (subst, 仅提权会话可见)", fullPath, letter);
            return letter;
        }

        // 方案3: DefineDosDevice API（仅提权会话可见）
        if (DefineDosDeviceW(0, $"{letter}:", fullPath))
        {
            _mappedByUs.Add(letter.Value);
            Log.Information("[DriveMapper] 已映射 {FullPath} -> {Letter}: (DefineDosDevice, 仅提权会话可见)", fullPath, letter);
            return letter;
        }
        Log.Error("[DriveMapper] 所有映射方式均失败");
        return null;
    }

    /// <summary>
    /// 取消本地文件夹的驱动器映射
    /// </summary>
    public static void UnmapLocalDrive(char driveLetter)
    {
        // 两个会话都尝试移除
        RunCommandAsNonElevated("subst", $"{driveLetter}: /d");
        RunCommand("subst", $"{driveLetter}: /d");
        DefineDosDeviceW(DDD_REMOVE_DEFINITION, $"{driveLetter}:", null);
        _mappedByUs.Remove(driveLetter);
        Log.Information("[DriveMapper] 已取消映射 {DriveLetter}:", driveLetter);
    }

    private static char? FindAvailableDriveLetter()
    {
        var usedLetters = DriveInfo.GetDrives()
                                   .Select(d => d.Name[0])
                                   .ToHashSet();
        foreach (var m in _mappedByUs)
            usedLetters.Add(m);
        foreach (var letter in CandidateLetters)
        {
            if (!usedLetters.Contains(letter))
            {
                return letter;
            }
        }
        return null;
    }

    private static (int ExitCode, string Output) RunCommand(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            return (process.ExitCode, string.IsNullOrEmpty(output) ? error : output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }

    /// <summary>
    /// 以非提权（普通用户）身份运行命令，解决管理员会话隔离问题。
    /// 通过 Task Scheduler 以当前登录用户身份（LIMITED 权限）执行。
    /// </summary>
    private static (int ExitCode, string Output) RunCommandAsNonElevated(string fileName, string arguments)
    {
        try
        {
            var tempOut = Path.Combine(Path.GetTempPath(), $"ext4mount_{Guid.NewGuid():N}.out");
            var taskName = $"Ext4Mount_{Guid.NewGuid():N}";

            // 创建一个计划任务，以当前用户身份（非提权）运行
            // /RL LIMITED = 以最低权限运行, /IT = 仅在用户登录时运行, /F = 强制创建
            var createResult = RunCommand("schtasks",
                $@"/Create /TN ""{taskName}"" /TR ""cmd /c {fileName} {arguments} > \""{tempOut}\"" 2>&1"" /SC ONCE /ST 00:00 /F /RL LIMITED /IT");
            if (createResult.ExitCode != 0)
            {
                return (-1, $"创建任务失败: {createResult.Output}");
            }

            // 立即运行
            // ReSharper disable once UseRawString
            var runResult = RunCommand("schtasks", $@"/Run /TN ""{taskName}""");
            if (runResult.ExitCode != 0)
            {
                // ReSharper disable once UseRawString
                RunCommand("schtasks", $@"/Delete /TN ""{taskName}"" /F");
                return (-1, $"运行任务失败: {runResult.Output}");
            }

            // 等待完成
            for (var i = 0; i < 20; i++)
            {
                Thread.Sleep(500);
                if (!File.Exists(tempOut))
                {
                    continue;
                }
                // 再等一下确保写完
                Thread.Sleep(300);
                break;
            }

            // 清理任务
            // ReSharper disable once UseRawString
            RunCommand("schtasks", $@"/Delete /TN ""{taskName}"" /F");
            var output = "";
            if (File.Exists(tempOut))
            {
                output = File.ReadAllText(tempOut);
                try
                {
                    File.Delete(tempOut);
                }
                catch
                {
                    // 无论如何删除临时文件，避免垃圾文件残留
                }
            }

            // subst 成功时无输出，失败时有错误信息
            var success = string.IsNullOrWhiteSpace(output) || output.Contains("successfully", StringComparison.OrdinalIgnoreCase) || output.Contains("成功", StringComparison.Ordinal);
            return (success ? 0 : 1, output);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}