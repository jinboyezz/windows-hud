using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WindowsHUD.Native;

namespace WindowsHUD.Services;

public readonly record struct MetricsSnapshot(
    double CpuPercent,
    double MemoryPercent,
    double UploadBytesPerSec,
    double DownloadBytesPerSec,
    double DiskPercent,
    double GpuPercent);

public sealed class SystemMetricsService : IDisposable
{
    private static readonly string[] VirtualAdapterKeywords =
    {
        "virtual", "vmware", "hyper-v", "npcap", "teredo", "isatap",
        "loopback", "pseudo-interface", "bluetooth"
    };

    private static readonly Regex GpuLuidRegex = new(@"luid_0x[0-9a-fA-F]+_0x[0-9a-fA-F]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly PerformanceCounter _cpuCounter;
    private readonly Dictionary<string, PerformanceCounter> _diskCounters = new();
    private readonly Dictionary<string, (PerformanceCounter Sent, PerformanceCounter Recv)> _netCounters = new();
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();

    private int _isSampling;
    private int _ticksSinceInstanceRefresh;
    private const int InstanceRefreshEveryTicks = 10; // refresh adapter/engine instance lists roughly every ~20s

    public SystemMetricsService()
    {
        // "Processor Information\% Processor Utility" (not "Processor\% Processor Time")
        // is what Task Manager reports — it accounts for Turbo Boost/dynamic clock
        // scaling, which the older counter ignores and under-reports on modern CPUs.
        _cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");

        // First read of a fresh rate counter is meaningless; warm it up.
        _cpuCounter.NextValue();

        RefreshNetworkInstances();
        RefreshGpuInstances();
        RefreshDiskInstances();
    }

    public async Task<MetricsSnapshot?> SampleAsync()
    {
        // Guard against overlapping samples: if a previous sample is still running
        // (e.g. instance enumeration stalled), skip this tick rather than letting two
        // concurrent NextValue() calls corrupt the rate-counter interval.
        if (Interlocked.CompareExchange(ref _isSampling, 1, 0) != 0)
        {
            return null;
        }

        try
        {
            return await Task.Run(GetSnapshot);
        }
        finally
        {
            Interlocked.Exchange(ref _isSampling, 0);
        }
    }

    private MetricsSnapshot GetSnapshot()
    {
        if (_ticksSinceInstanceRefresh == 0)
        {
            RefreshNetworkInstances();
            RefreshGpuInstances();
            RefreshDiskInstances();
        }
        _ticksSinceInstanceRefresh = (_ticksSinceInstanceRefresh + 1) % InstanceRefreshEveryTicks;

        double cpu = Math.Clamp(SafeNextValue(_cpuCounter), 0, 100);
        double disk = SampleDiskPercent();

        double upload = 0, download = 0;
        foreach (var (sent, recv) in _netCounters.Values.ToList())
        {
            upload += SafeNextValue(sent);
            download += SafeNextValue(recv);
        }

        double gpu = SampleGpuPercent();

        var mem = NativeMethods.MEMORYSTATUSEX.Create();
        NativeMethods.GlobalMemoryStatusEx(ref mem);
        double memPercent = mem.dwMemoryLoad;

        return new MetricsSnapshot(cpu, memPercent, upload, download, disk, gpu);
    }

    private double SampleDiskPercent()
    {
        if (_diskCounters.Count == 0)
        {
            return 0;
        }

        // "_Total" averages % Disk Time across every physical disk, which dilutes
        // the number to near-zero when only one disk is actually busy (the common
        // case). Task Manager instead shows each disk's own activity, so report
        // whichever single disk is busiest — that's the number a user recognizes.
        double busiest = 0;
        foreach (var counter in _diskCounters.Values.ToList())
        {
            busiest = Math.Max(busiest, SafeNextValue(counter));
        }

        return Math.Clamp(busiest, 0, 100);
    }

    private double SampleGpuPercent()
    {
        if (_gpuCounters.Count == 0)
        {
            return 0;
        }

        // Multiple processes can each use the 3D engine concurrently; group by GPU
        // (luid) and sum per-GPU, then report the busiest GPU — this mirrors how
        // Task Manager derives its single "GPU" utilization number.
        var perGpuTotal = new Dictionary<string, double>();

        foreach (var (instanceName, counter) in _gpuCounters.ToList())
        {
            var match = GpuLuidRegex.Match(instanceName);
            string luid = match.Success ? match.Value : "default";

            double value = SafeNextValue(counter);
            perGpuTotal[luid] = perGpuTotal.GetValueOrDefault(luid) + value;
        }

        if (perGpuTotal.Count == 0)
        {
            return 0;
        }

        double busiest = perGpuTotal.Values.Max();
        return Math.Clamp(busiest, 0, 100);
    }

    private void RefreshNetworkInstances()
    {
        HashSet<string> currentNames;
        try
        {
            var category = new PerformanceCounterCategory("Network Interface");
            currentNames = category.GetInstanceNames()
                .Where(n => !VirtualAdapterKeywords.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToHashSet();
        }
        catch
        {
            return;
        }

        foreach (var stale in _netCounters.Keys.Except(currentNames).ToList())
        {
            _netCounters[stale].Sent.Dispose();
            _netCounters[stale].Recv.Dispose();
            _netCounters.Remove(stale);
        }

        foreach (var name in currentNames.Except(_netCounters.Keys))
        {
            try
            {
                var sent = new PerformanceCounter("Network Interface", "Bytes Sent/sec", name);
                var recv = new PerformanceCounter("Network Interface", "Bytes Received/sec", name);
                sent.NextValue();
                recv.NextValue();
                _netCounters[name] = (sent, recv);
            }
            catch
            {
                // Instance may be transient/inaccessible; skip it.
            }
        }
    }

    private void RefreshGpuInstances()
    {
        HashSet<string> currentNames;
        try
        {
            if (!PerformanceCounterCategory.Exists("GPU Engine"))
            {
                return;
            }

            var category = new PerformanceCounterCategory("GPU Engine");
            currentNames = category.GetInstanceNames()
                .Where(n => n.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                .ToHashSet();
        }
        catch
        {
            return;
        }

        foreach (var stale in _gpuCounters.Keys.Except(currentNames).ToList())
        {
            _gpuCounters[stale].Dispose();
            _gpuCounters.Remove(stale);
        }

        foreach (var name in currentNames.Except(_gpuCounters.Keys))
        {
            try
            {
                var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name);
                counter.NextValue();
                _gpuCounters[name] = counter;
            }
            catch
            {
                // Instance may belong to a process that exited between enumeration and creation; skip it.
            }
        }
    }

    private void RefreshDiskInstances()
    {
        HashSet<string> currentNames;
        try
        {
            var category = new PerformanceCounterCategory("PhysicalDisk");
            currentNames = category.GetInstanceNames()
                .Where(n => !n.Equals("_Total", StringComparison.OrdinalIgnoreCase))
                .ToHashSet();
        }
        catch
        {
            return;
        }

        foreach (var stale in _diskCounters.Keys.Except(currentNames).ToList())
        {
            _diskCounters[stale].Dispose();
            _diskCounters.Remove(stale);
        }

        foreach (var name in currentNames.Except(_diskCounters.Keys))
        {
            try
            {
                var counter = new PerformanceCounter("PhysicalDisk", "% Disk Time", name);
                counter.NextValue();
                _diskCounters[name] = counter;
            }
            catch
            {
                // Instance may be transient/inaccessible; skip it.
            }
        }
    }

    private static double SafeNextValue(PerformanceCounter counter)
    {
        try
        {
            return counter.NextValue();
        }
        catch
        {
            return 0;
        }
    }

    public void Dispose()
    {
        _cpuCounter.Dispose();
        foreach (var counter in _diskCounters.Values)
        {
            counter.Dispose();
        }
        _diskCounters.Clear();

        foreach (var (sent, recv) in _netCounters.Values)
        {
            sent.Dispose();
            recv.Dispose();
        }
        _netCounters.Clear();

        foreach (var counter in _gpuCounters.Values)
        {
            counter.Dispose();
        }
        _gpuCounters.Clear();
    }
}
