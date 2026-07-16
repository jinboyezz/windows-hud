using System;
using System.Diagnostics;
using WindowsHUD.Native;

namespace WindowsHUD.Services;

public readonly record struct CleanupResult(int ProcessesCleaned, long FreedBytes);

public static class MemoryCleanupService
{
    public static CleanupResult RunCleanup()
    {
        long availBefore = GetAvailablePhysicalBytes();

        int cleaned = 0;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == 0)
                    {
                        continue;
                    }

                    IntPtr handle = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_QUERY_INFORMATION | NativeMethods.PROCESS_SET_QUOTA,
                        false,
                        (uint)process.Id);

                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    try
                    {
                        if (NativeMethods.EmptyWorkingSet(handle))
                        {
                            cleaned++;
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(handle);
                    }
                }
                catch
                {
                    // Ignore processes we can't access or that have already exited.
                }
            }
        }

        long availAfter = GetAvailablePhysicalBytes();
        return new CleanupResult(cleaned, availAfter - availBefore);
    }

    private static long GetAvailablePhysicalBytes()
    {
        var status = NativeMethods.MEMORYSTATUSEX.Create();
        return NativeMethods.GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : 0;
    }
}
