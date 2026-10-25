using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Lean81
{
    public sealed class ProcessReading
    {
        public int pid { get; set; }
        public string name { get; set; }
        public double cpu_pct { get; set; }
        public long? rss_bytes { get; set; }
        public long? private_bytes { get; set; }
        public double? io_bytes_per_sec { get; set; }
        public long started_ticks { get; set; }
    }
    public sealed class CaptureSample
    {
        public string type { get { return "sample"; } }
        public double t { get; set; }
        public double dt { get; set; }
        public double collector_ms { get; set; }
        public double[] wake_lag_ms { get { return new double[0]; } }
        public Dictionary<string, object> metrics { get; set; }
        public List<ProcessReading> processes { get; set; }
    }
    public sealed class CaptureResult
    {
        public List<CaptureSample> Samples = new List<CaptureSample>();
        public bool Synthetic, Interrupted;
        public double Duration, ObserverCpu;
        public int LogicalCpus;
        public ulong MemoryBytes;
        public string CpuModel, Error;
        public void ExportCsv(string path, bool includeNames)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine("elapsed_s,interval_s,cpu_pct,memory_available_pct,power,top_process_cpu_pct,top_process_name,disk_busy_pct,disk_bytes_per_sec,disk_queue,pages_input_per_sec,commit_pct,kernel_nonpaged_bytes");
                foreach (var sample in Samples)
                {
                    var top = sample.processes.OrderByDescending(p => p.cpu_pct).FirstOrDefault();
                    var row = new List<string> { sample.t.ToString("0.000", CultureInfo.InvariantCulture), sample.dt.ToString("0.000", CultureInfo.InvariantCulture), Convert.ToString(sample.metrics["cpu_pct"], CultureInfo.InvariantCulture), Convert.ToString(sample.metrics["memory_available_pct"], CultureInfo.InvariantCulture), Csv(Convert.ToString(sample.metrics["power"], CultureInfo.InvariantCulture)), top == null ? "" : top.cpu_pct.ToString("0.000", CultureInfo.InvariantCulture), Csv(includeNames && top != null ? top.name : "redacted") };
                    foreach (string key in new[] { "disk_busy_pct", "disk_bytes_per_sec", "disk_queue", "pages_input_per_sec", "commit_pct", "kernel_nonpaged_bytes" }) row.Add(Convert.ToString(ResourceAnalysis.Number(sample.metrics, key), CultureInfo.InvariantCulture));
                    writer.WriteLine(String.Join(",", row));
                }
            }
        }
        static string Csv(string value)
        {
            value = Product.Clean(value, 128);
            string trimmed = value.TrimStart();
            if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        public void Export(string path, bool includeNames)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(false)))
            {
                var json = Product.Json();
                writer.WriteLine(json.Serialize(new { type = "header", schema = 1, version = Product.Version, label = "Lean81 resource capture", synthetic = Synthetic,
                    platform = "Windows", backend = "lean81-win32", interval_s = 2, heartbeat_s = 0.05, workload = "On-demand laptop observation; no workload injected",
                    process_names_enabled = includeNames,
                    hardware = new { machine = Environment.Is64BitOperatingSystem ? "x64" : "x86", logical_cpus = LogicalCpus, memory_bytes = MemoryBytes == 0 ? (object)null : MemoryBytes,
                        cpu_model = CpuModel, scope_warning = "No wake-delay probe. Physical-disk counters are aggregate across disks. Process I/O includes file, network and device operations, not just disk. Up to 20 CPU/memory/I-O leaders per sample; inaccessible and short-lived processes may be missed." } }));
                foreach (var sample in Samples)
                    writer.WriteLine(json.Serialize(new { type = "sample", sample.t, sample.dt, sample.collector_ms, sample.wake_lag_ms, sample.metrics,
                        processes = sample.processes.Select(p => new { p.pid, name = includeNames ? p.name : "redacted", p.cpu_pct, p.rss_bytes, p.private_bytes, p.io_bytes_per_sec }).ToArray() }));
                writer.WriteLine(json.Serialize(new { type = "footer", duration_s = Duration, interrupted = Interrupted, observer_cpu_s = ObserverCpu,
                    dropped_wake_samples = 0, samples = Samples.Count, error = Error }));
            }
        }
        public static CaptureResult Demo()
        {
            var r = new CaptureResult { Synthetic = true, Duration = 30, LogicalCpus = 2, MemoryBytes = 4UL * 1024 * 1024 * 1024, CpuModel = "Synthetic low-power CPU" };
            for (int i = 1; i <= 15; i++)
            {
                double cpu = i == 8 ? 87 : 18 + i % 5 * 4;
                r.Samples.Add(new CaptureSample { t = i * 2, dt = 2, collector_ms = 4,
                    metrics = new Dictionary<string, object> { { "cpu_pct", cpu }, { "memory_available_pct", 43.0 }, { "power", "ac" }, { "disk_busy_pct", i == 8 ? 96.0 : 24.0 }, { "disk_bytes_per_sec", 4.0 * 1024 * 1024 }, { "disk_queue", 0.4 }, { "pages_input_per_sec", 2.0 }, { "commit_pct", 56.0 }, { "commit_bytes", 3.36 * 1073741824 }, { "commit_limit_bytes", 6.0 * 1073741824 }, { "kernel_nonpaged_bytes", 180.0 * 1048576 }, { "kernel_paged_bytes", 240.0 * 1048576 }, { "system_cache_bytes", 700.0 * 1048576 }, { "kernel_cpu_pct", 8.0 } },
                    processes = new List<ProcessReading> { new ProcessReading { pid = 120, name = "ExampleApp", cpu_pct = cpu * 0.7, rss_bytes = 80 * 1024 * 1024, private_bytes = 65 * 1024 * 1024, io_bytes_per_sec = 3.0 * 1048576, started_ticks = 1 }, new ProcessReading { pid = 121, name = "IdleMemoryApp", cpu_pct = 0, rss_bytes = 850 * 1024 * 1024, private_bytes = 720 * 1024 * 1024, io_bytes_per_sec = 0, started_ticks = 1 } } });
            }
            foreach (var sample in r.Samples) sample.metrics["memory_available_bytes"] = 0.43 * r.MemoryBytes;
            return r;
        }
    }
    public static class CpuCapture
    {
        [StructLayout(LayoutKind.Sequential)] struct TimeValue { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [StructLayout(LayoutKind.Sequential)] struct Memory { public uint Length, Load; public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out TimeValue idle, out TimeValue kernel, out TimeValue user);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref Memory memory);
        [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        sealed class ProcessTime { public string Name; public long Start, Rss, Private; public double Cpu; public ulong? Io; }
        static Dictionary<int, ProcessTime> Processes()
        {
            var result = new Dictionary<int, ProcessTime>();
            foreach (var p in Process.GetProcesses())
            {
                using (p) try
                {
                    var reading = new ProcessTime { Name = Product.Clean(p.ProcessName, 128), Start = p.StartTime.ToUniversalTime().Ticks, Cpu = p.TotalProcessorTime.TotalSeconds, Rss = p.WorkingSet64, Private = p.PrivateMemorySize64 };
                    IntPtr handle = OpenProcess(0x1000, false, p.Id);
                    if (handle != IntPtr.Zero) try { IoCounters io; if (GetProcessIoCounters(handle, out io)) reading.Io = checked(io.ReadBytes + io.WriteBytes); } finally { CloseHandle(handle); }
                    result[p.Id] = reading;
                }
                catch { }
            }
            return result;
        }
        static ulong[] Times()
        {
            TimeValue idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new[] { idle.Value, kernel.Value + user.Value, kernel.Value };
        }
        public static double Percent(ulong idleDelta, ulong totalDelta)
        { return totalDelta == 0 || idleDelta > totalDelta ? 0 : Math.Max(0, Math.Min(100, 100.0 * (totalDelta - idleDelta) / totalDelta)); }
        public static CaptureResult Run(int seconds, CancellationToken token, Action<CaptureSample> progress)
        {
            if (seconds < 4 || seconds > 120) throw new ArgumentOutOfRangeException("seconds");
            var result = new CaptureResult { LogicalCpus = Environment.ProcessorCount, CpuModel = "Unknown CPU" };
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                if (key != null) result.CpuModel = Product.Clean(key.GetValue("ProcessorNameString") as string, 1000);
            using (var self = Process.GetCurrentProcess())
            using (var resources = new ResourceCounters())
            {
            double observer = self.TotalProcessorTime.TotalSeconds;
            var watch = Stopwatch.StartNew(); double last = 0; var previous = Processes(); var times = Times();
            try
            {
                while (watch.Elapsed.TotalSeconds < seconds)
                {
                    if (token.WaitHandle.WaitOne(2000)) { result.Interrupted = true; break; }
                    double at = watch.Elapsed.TotalSeconds, dt = at - last; var collection = Stopwatch.StartNew();
                    var next = Times(); var processes = Processes();
                    var sample = new CaptureSample { t = at, dt = dt, processes = new List<ProcessReading>(), metrics = new Dictionary<string, object>() };
                    sample.metrics["cpu_pct"] = next[0] < times[0] || next[1] < times[1] ? (object)null : Percent(next[0] - times[0], next[1] - times[1]);
                    sample.metrics["kernel_cpu_pct"] = next[2] < times[2] || next[0] < times[0] || next[1] <= times[1] || next[2] - times[2] < next[0] - times[0] ? (object)null : 100.0 * ((next[2] - times[2]) - (next[0] - times[0])) / (next[1] - times[1]);
                    resources.Read(sample.metrics); MemoryDiagnostics.Read(sample.metrics);
                    var memory = new Memory { Length = (uint)Marshal.SizeOf(typeof(Memory)) };
                    if (GlobalMemoryStatusEx(ref memory)) { result.MemoryBytes = memory.Total; sample.metrics["memory_available_pct"] = memory.Total == 0 ? (object)null : 100.0 * memory.Available / memory.Total; sample.metrics["memory_available_bytes"] = memory.Available; }
                    else { sample.metrics["memory_available_pct"] = null; sample.metrics["memory_available_bytes"] = null; }
                    var power = SystemInformation.PowerStatus.PowerLineStatus;
                    sample.metrics["power"] = power == PowerLineStatus.Online ? "ac" : power == PowerLineStatus.Offline ? "battery" : "unknown";
                    foreach (var pair in processes)
                    {
                        ProcessTime before; var now = pair.Value;
                        if (!previous.TryGetValue(pair.Key, out before) || now.Start != before.Start || now.Cpu < before.Cpu) continue;
                        double cpu = Math.Min(100, Math.Max(0, (now.Cpu - before.Cpu) / dt / result.LogicalCpus * 100));
                        double? io = now.Io.HasValue && before.Io.HasValue && now.Io >= before.Io ? (double?)((now.Io.Value - before.Io.Value) / dt) : null;
                        sample.processes.Add(new ProcessReading { pid = pair.Key, name = now.Name, cpu_pct = cpu, rss_bytes = Math.Max(0, now.Rss), private_bytes = Math.Max(0, now.Private), io_bytes_per_sec = io, started_ticks = now.Start });
                    }
                    sample.metrics["accessible_processes"] = processes.Count;
                    sample.processes = SelectLeaders(sample.processes);
                    sample.collector_ms = collection.Elapsed.TotalMilliseconds;
                    result.Samples.Add(sample); last = at; previous = processes; times = next;
                    if (progress != null) progress(sample);
                }
            }
            catch (Exception ex) { result.Error = Product.Clean(ex.Message, 400); result.Interrupted = true; }
            finally { result.Duration = watch.Elapsed.TotalSeconds; self.Refresh(); result.ObserverCpu = Math.Max(0, self.TotalProcessorTime.TotalSeconds - observer); }
            return result;
            }
        }
        public static List<ProcessReading> SelectLeaders(IEnumerable<ProcessReading> readings)
        {
            var all = readings.ToArray();
            return all.OrderByDescending(p => p.cpu_pct).Take(5).Concat(all.OrderByDescending(p => p.rss_bytes).Take(5))
                .Concat(all.OrderByDescending(p => p.private_bytes).Take(5)).Concat(all.Where(p => p.io_bytes_per_sec.HasValue).OrderByDescending(p => p.io_bytes_per_sec).Take(5))
                .GroupBy(p => p.pid).Select(g => g.First()).OrderByDescending(p => p.cpu_pct).Take(20).ToList();
        }
    }
}
