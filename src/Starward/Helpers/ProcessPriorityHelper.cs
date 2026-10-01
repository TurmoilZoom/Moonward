using System;
using System.Runtime.InteropServices;

namespace Starward.Helpers;

/// <summary>
/// 恢复当前进程的内存优先级与 I/O 优先级。
/// <para>
/// 计划任务（免 UAC 启动游戏等）默认以优先级 7 拉起进程：除了 CPU 优先级类为「低于正常」，
/// I/O 优先级还被设为 Low、内存优先级被设为 2（正常值分别是 Normal 与 5）。
/// 只把 <see cref="System.Diagnostics.ProcessPriorityClass"/> 改回 Normal 收不回后两项，而它们会原样继承给子进程。
/// 2026-10-01 实测：经免 UAC 快捷方式启动的绝区零因此带着低 I/O、低内存优先级运行，
/// 平均帧数与官方启动器相同，但画面明显不如后者流畅。
/// </para>
/// </summary>
public static partial class ProcessPriorityHelper
{

    /// <summary>
    /// 把当前进程的内存优先级设为 <c>MEMORY_PRIORITY_NORMAL</c>、I/O 优先级设为 Normal，
    /// 之后启动的子进程（游戏、CMD、自定义启动程序等）随之继承正常值。
    /// 在 <c>Program.Main</c> 中调用，此时日志尚未初始化，失败时静默忽略。
    /// </summary>
    public static void RestoreNormalMemoryAndIoPriority()
    {
        try
        {
            IntPtr process = GetCurrentProcess();
            var memoryPriority = new MEMORY_PRIORITY_INFORMATION { MemoryPriority = MEMORY_PRIORITY_NORMAL };
            SetProcessInformation(process, ProcessMemoryPriority, in memoryPriority, (uint)Marshal.SizeOf<MEMORY_PRIORITY_INFORMATION>());
            // 只设回 Normal，不需要 SeIncreaseBasePriorityPrivilege
            int ioPriority = IoPriorityNormal;
            NtSetInformationProcess(process, ProcessIoPriority, in ioPriority, sizeof(int));
        }
        catch
        {
            // 只是兜底，任何异常都不能挡住启动
        }
    }


    #region Native

    /// <summary>PROCESS_INFORMATION_CLASS.ProcessMemoryPriority</summary>
    private const int ProcessMemoryPriority = 0;

    /// <summary>MEMORY_PRIORITY_NORMAL，进程默认的内存优先级。</summary>
    private const uint MEMORY_PRIORITY_NORMAL = 5;

    /// <summary>PROCESSINFOCLASS.ProcessIoPriority</summary>
    private const int ProcessIoPriority = 33;

    /// <summary>IO_PRIORITY_HINT.IoPriorityNormal</summary>
    private const int IoPriorityNormal = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_PRIORITY_INFORMATION
    {
        public uint MemoryPriority;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetProcessInformation(IntPtr process,
                                                     int processInformationClass,
                                                     in MEMORY_PRIORITY_INFORMATION processInformation,
                                                     uint processInformationSize);

    [LibraryImport("ntdll.dll")]
    private static partial int NtSetInformationProcess(IntPtr process,
                                                      int processInformationClass,
                                                      in int processInformation,
                                                      int processInformationLength);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetCurrentProcess();

    #endregion

}
