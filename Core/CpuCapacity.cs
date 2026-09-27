using System.Numerics;
using System.Runtime.InteropServices;

namespace ClutterFlock.Core;

// Read live Windows limits once per comparison; Environment.ProcessorCount is cached at startup.
public static class CpuCapacity
{
    public static int ReadAvailableProcessors()
    {
        var process = new IntPtr(-1);
        var total = GetActiveProcessorCount(ushort.MaxValue);
        var available = total > 0 ? (int)total : 1;
        if (GetProcessAffinityMask(process, out var allowed, out var system) && allowed != UIntPtr.Zero)
        {
            // A restrictive process mask must take precedence. On multi-group systems this
            // conservatively uses the caller's group rather than assuming other groups are usable.
            available = Math.Min(available, BitOperations.PopCount(allowed.ToUInt64()));
        }
        else available = 1; // Keep the UI responsive if Windows cannot report process eligibility.
        if (GetProcessDefaultCpuSets(process, null, 0, out var cpuSetCount) || Marshal.GetLastWin32Error() == 122)
            if (cpuSetCount > 0) available = Math.Min(available, (int)cpuSetCount);
        // A Windows job may impose a CPU-time ceiling in addition to affinity restrictions.
        if (QueryInformationJobObject(IntPtr.Zero, 15, out var job, 8, IntPtr.Zero) && (job.Flags & 1) != 0)
        {
            var rate = (job.Flags & 4) != 0 ? job.Value : (job.Flags & 16) != 0 ? job.Value >> 16 : 0;
            if (rate > 0 && total > 0) available = Math.Min(available, Math.Max(1, (int)Math.Ceiling(total * (double)rate / 10000)));
        }
        return Math.Max(1, available);
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct CpuRate { public uint Flags, Value; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessAffinityMask(IntPtr process, out UIntPtr allowed, out UIntPtr system);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessDefaultCpuSets(IntPtr process, uint[]? ids, uint count, out uint required);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int informationClass, out CpuRate information, uint length, IntPtr returnedLength);
}
