using System.Runtime.InteropServices;

namespace Boulevard.Edge.MarketData;

/// <summary>
/// Best-effort CPU core pinning via sched_setaffinity. Linux-only (the real deployment target,
/// Docker/Containerlab) - macOS has no equivalent hard-pinning API for user threads at all, and
/// Windows would need the unrelated SetThreadAffinityMask, so this is a documented no-op
/// everywhere except Linux rather than a silent pretend-success.
/// </summary>
internal static class LinuxThreadAffinity
{
    [DllImport("libc.so.6", SetLastError = true)]
    private static extern int sched_setaffinity(int pid, IntPtr cpusetsize, ref ulong mask);

    public static void TryPinCurrentThreadTo(int coreIndex, string threadName)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine($"[EDGE] Thread affinity pinning skipped for '{threadName}' (not supported on this OS).");
            return;
        }

        ulong mask = 1UL << coreIndex;

        // pid=0 means "the calling thread" for sched_setaffinity, so this must run on the
        // thread being pinned.
        int result = sched_setaffinity(0, (IntPtr)sizeof(ulong), ref mask);
        if (result == 0)
        {
            Console.WriteLine($"[EDGE] Pinned '{threadName}' to CPU core {coreIndex}.");
        }
        else
        {
            int errno = Marshal.GetLastWin32Error();
            Console.WriteLine($"[EDGE] Failed to pin '{threadName}' to CPU core {coreIndex} (errno={errno}).");
        }
    }
}
