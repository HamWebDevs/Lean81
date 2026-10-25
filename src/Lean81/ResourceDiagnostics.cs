using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace Lean81
{
    // One short-lived PDH query per capture. English paths work on localized Windows.
    // Missing/disabled counters stay unknown; they are never silently turned into zero.
    public sealed class ResourceCounters : IDisposable
    {
        IntPtr query;
        readonly Dictionary<string, IntPtr> counters = new Dictionary<string, IntPtr>();
        static readonly string[,] Paths = {
            { "disk_idle_pct", @"\PhysicalDisk(_Total)\% Idle Time" },
            { "disk_bytes_per_sec", @"\PhysicalDisk(_Total)\Disk Bytes/sec" },
            { "disk_queue", @"\PhysicalDisk(_Total)\Avg. Disk Queue Length" },
            { "pages_input_per_sec", @"\Memory\Pages Input/sec" }
        };
        [StructLayout(LayoutKind.Explicit, Size = 16)] struct CounterValue
        { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] static extern uint PdhOpenQuery(string source, UIntPtr user, out IntPtr handle);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern uint PdhAddEnglishCounterW(IntPtr handle, string path, UIntPtr user, out IntPtr counter);
        [DllImport("pdh.dll")] static extern uint PdhCollectQueryData(IntPtr handle);
        [DllImport("pdh.dll")] static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out CounterValue value);
        [DllImport("pdh.dll")] static extern uint PdhCloseQuery(IntPtr handle);
        public ResourceCounters()
        {
            try
            {
                if (PdhOpenQuery(null, UIntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return; }
                for (int i = 0; i < Paths.GetLength(0); i++)
                {
                    IntPtr counter;
                    if (PdhAddEnglishCounterW(query, Paths[i, 1], UIntPtr.Zero, out counter) == 0) counters.Add(Paths[i, 0], counter);
                }
                PdhCollectQueryData(query); // Prime interval counters; first usable value follows the wait.
            }
            catch (DllNotFoundException) { Dispose(); }
            catch (EntryPointNotFoundException) { Dispose(); }
        }
        public void Read(Dictionary<string, object> metrics)
        {
            bool collected = query != IntPtr.Zero && PdhCollectQueryData(query) == 0;
            for (int i = 0; i < Paths.GetLength(0); i++)
            {
                string name = Paths[i, 0]; IntPtr counter; uint type; CounterValue value;
                object number = null;
                // DOUBLE | NOSCALE | NOCAP100: preserve bytes/s and pages/s units.
                if (collected && counters.TryGetValue(name, out counter) && PdhGetFormattedCounterValue(counter, 0x200 | 0x1000 | 0x8000, out type, out value) == 0
                    && value.Status <= 1 && Product.Finite(value.Value) && value.Value >= 0 && (name != "disk_idle_pct" || value.Value <= 100)) number = value.Value;
                metrics[name == "disk_idle_pct" ? "disk_busy_pct" : name] = name == "disk_idle_pct" && number != null ? (object)(100 - (double)number) : number;
            }
        }
        public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; } counters.Clear(); }
    }
    public static class MemoryDiagnostics
    {
        [StructLayout(LayoutKind.Sequential)] struct PerformanceInfo
        {
            public uint Size;
            public UIntPtr CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint Handles, Processes, Threads;
        }
        [DllImport("psapi.dll", SetLastError = true)] static extern bool GetPerformanceInfo(out PerformanceInfo info, uint size);
        public static void Read(Dictionary<string, object> metrics)
        {
            foreach (string key in new[] { "commit_pct", "commit_bytes", "commit_limit_bytes", "kernel_nonpaged_bytes", "kernel_paged_bytes", "system_cache_bytes" }) metrics[key] = null;
            PerformanceInfo info;
            if (!GetPerformanceInfo(out info, (uint)Marshal.SizeOf(typeof(PerformanceInfo)))) return;
            double page = info.PageSize.ToUInt64(), limit = info.CommitLimit.ToUInt64();
            if (page <= 0 || limit <= 0) return;
            metrics["commit_pct"] = 100.0 * info.CommitTotal.ToUInt64() / limit;
            metrics["commit_bytes"] = page * info.CommitTotal.ToUInt64(); metrics["commit_limit_bytes"] = page * limit;
            metrics["kernel_nonpaged_bytes"] = page * info.KernelNonpaged.ToUInt64(); metrics["kernel_paged_bytes"] = page * info.KernelPaged.ToUInt64();
            metrics["system_cache_bytes"] = page * info.SystemCache.ToUInt64();
        }
    }
    public sealed class ProcessUsage
    {
        public string Name; public int Pid, Samples;
        public double PeakCpu, PeakWorkingSet, PeakPrivate, PeakIo;
        public bool HasIo;
    }
    public static class ResourceAnalysis
    {
        public static double? Number(IDictionary<string, object> metrics, string key, double maximum = Double.MaxValue)
        {
            object raw; if (metrics == null || !metrics.TryGetValue(key, out raw) || raw == null || raw is bool || raw is string) return null;
            try { double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); return Product.Finite(value) && value >= 0 && value <= maximum ? (double?)value : null; } catch { return null; }
        }
        public static double? Average(CaptureResult capture, string key, double maximum = Double.MaxValue)
        {
            if (capture == null) return null;
            double weighted = 0, seconds = 0;
            foreach (var sample in capture.Samples)
            {
                var value = Number(sample.metrics, key, maximum);
                if (value.HasValue && Product.Finite(sample.dt) && sample.dt > 0) { weighted += value.Value * sample.dt; seconds += sample.dt; }
            }
            return seconds > 0 ? (double?)(weighted / seconds) : null;
        }
        public static double? Peak(CaptureResult capture, string key, double maximum = Double.MaxValue)
        {
            if (capture == null) return null;
            var values = capture.Samples.Select(s => Number(s.metrics, key, maximum)).Where(v => v.HasValue).ToArray();
            return values.Length == 0 ? null : values.Max();
        }
        public static IList<ProcessUsage> Processes(CaptureResult capture)
        {
            var result = new Dictionary<string, ProcessUsage>(); if (capture == null) return result.Values.ToList();
            foreach (var sample in capture.Samples)
            {
                if (!Product.Finite(sample.dt) || sample.dt <= 0) continue;
                foreach (var p in sample.processes)
                {
                    string key = p.pid + "\n" + p.name + "\n" + p.started_ticks; ProcessUsage item;
                    if (!result.TryGetValue(key, out item)) { item = new ProcessUsage { Name = p.name, Pid = p.pid }; result.Add(key, item); }
                    item.Samples++;
                    item.PeakCpu = Math.Max(item.PeakCpu, p.cpu_pct); item.PeakWorkingSet = Math.Max(item.PeakWorkingSet, p.rss_bytes ?? 0); item.PeakPrivate = Math.Max(item.PeakPrivate, p.private_bytes ?? 0);
                    if (p.io_bytes_per_sec.HasValue) { item.HasIo = true; item.PeakIo = Math.Max(item.PeakIo, p.io_bytes_per_sec.Value); }
                }
            }
            return result.Values.ToList();
        }
        public static string Format(double? value, string suffix, double divisor = 1)
        { return value.HasValue ? (value.Value / divisor).ToString("0.0", CultureInfo.InvariantCulture) + suffix : "Unavailable"; }
        public static string Findings(CaptureResult capture)
        {
            if (capture == null || capture.Samples.Count == 0) return "Record 30–120 seconds while the slowdown is happening. Use the laptop normally.";
            var lines = new List<string>();
            if (capture.Synthetic) lines.Add("SYNTHETIC DEMO · invented resource readings.");
            if (capture.Interrupted || capture.Error != null) lines.Add("Partial capture: these findings cover only the recorded intervals.");
            var cpu = Average(capture, "cpu_pct", 100); var memory = Average(capture, "memory_available_pct", 100); var disk = Average(capture, "disk_busy_pct", 100); var commit = Peak(capture, "commit_pct", 100);
            if (cpu >= 80) lines.Add("Sustained high CPU: sort processes by CPU. Close an unneeded app normally or review its startup entry; a demanding browser workload may remain CPU limited.");
            else if (Peak(capture, "cpu_pct", 100) >= 80) lines.Add("CPU spikes were sampled. Sort processes by peak CPU and repeat a longer capture to see whether the same app keeps appearing.");
            if (memory < 15) lines.Add("Low available RAM: sort by working set/private memory and close unused apps or tabs. Windows cache is reusable; total 'used RAM' alone is not a fault.");
            if (commit >= 90) lines.Add("Commit is near its limit: save work and close unused apps. Keep Windows' paging file enabled.");
            if (disk >= 80) lines.Add("High aggregate physical-disk activity: check Resource Monitor's Disk tab. Process I/O also includes network/device operations and cannot prove which process caused disk load.");
            if (Average(capture, "pages_input_per_sec") > 0 && memory < 15) lines.Add("Disk page-ins occurred alongside low available RAM. Fewer active apps may reduce this pressure; page-ins can also read mapped program/data files.");
            if (cpu < 80 && memory >= 15 && disk < 80) lines.Add("No sustained high CPU, low available RAM or high aggregate disk activity in these samples. Capture again during a freeze; shorter spikes can be missed.");
            if (!disk.HasValue) lines.Add("Disk counters are unavailable on this Windows installation. This is unknown disk activity, not 0%. Resource Monitor can help inspect it.");
            if (!memory.HasValue) lines.Add("Available RAM could not be read; memory pressure is unknown.");
            if (!cpu.HasValue) lines.Add("CPU readings are unavailable; CPU pressure is unknown.");
            if (!commit.HasValue) lines.Add("Commit readings are unavailable; commit pressure is unknown.");
            lines.Add("Kernel pools are shown for diagnosis. Large values alone do not establish a driver leak. Process working sets can share pages and must not be added together as total RAM.");
            return String.Join(Environment.NewLine + Environment.NewLine, lines);
        }
    }
}
