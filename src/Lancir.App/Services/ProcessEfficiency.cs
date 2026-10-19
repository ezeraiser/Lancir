using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lancir.App.Services;

/// <summary>
/// Puts this process into what Task Manager calls "efficiency mode" and takes it out again: power throttling (EcoQoS, so Windows runs
/// it on the slowest, most economical cores), the lowest scheduling priority, and the lowest memory priority (its pages are the first
/// ones Windows takes back when memory is needed).
/// </summary>
internal static class ProcessEfficiency
{
    private const int ProcessMemoryPriority = 0;
    private const int ProcessPowerThrottling = 4;
    private const uint MemoryPriorityVeryLow = 1;
    private const uint MemoryPriorityNormal = 5;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryPriorityInformation
    {
        public uint MemoryPriority;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref MemoryPriorityInformation information, int size);

    public static void Enter() => Apply(throttle: true, ProcessPriorityClass.Idle, MemoryPriorityVeryLow);

    public static void Leave() => Apply(throttle: false, ProcessPriorityClass.Normal, MemoryPriorityNormal);

    private static void Apply(bool throttle, ProcessPriorityClass priority, uint memoryPriority)
    {
        using var process = Process.GetCurrentProcess();

        // ControlMask says "this setting is mine", StateMask 1 turns throttling on and 0 explicitly off (never throttled while in use)
        var power = new PowerThrottlingState { Version = 1, ControlMask = 1, StateMask = throttle ? 1u : 0u };
        SetProcessInformation(process.Handle, ProcessPowerThrottling, ref power, Marshal.SizeOf<PowerThrottlingState>());

        var memory = new MemoryPriorityInformation { MemoryPriority = memoryPriority };
        SetProcessInformation(process.Handle, ProcessMemoryPriority, ref memory, Marshal.SizeOf<MemoryPriorityInformation>());

        try
        {
            process.PriorityClass = priority;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // keeps the priority it has: the other two settings still apply
        }
    }
}
