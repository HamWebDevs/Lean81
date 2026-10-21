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
                writer.WriteLine("elapsed_s,interval_s,cpu_pct,memory_available_pct,power,top_process_cpu_pct,top_process_name");
                foreach (var sample in Samples)
                {
                    var top = sample.processes.OrderByDescending(p => p.cpu_pct).FirstOrDefault();
                    writer.WriteLine(String.Join(",", new[] { sample.t.ToString("0.000", CultureInfo.InvariantCulture), sample.dt.ToString("0.000", CultureInfo.InvariantCulture), Convert.ToString(sample.metrics["cpu_pct"], CultureInfo.InvariantCulture), Convert.ToString(sample.metrics["memory_available_pct"], CultureInfo.InvariantCulture), Csv(Convert.ToString(sample.metrics["power"], CultureInfo.InvariantCulture)), top == null ? "" : top.cpu_pct.ToString("0.000", CultureInfo.InvariantCulture), Csv(includeNames && top != null ? top.name : "redacted") }));
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
                writer.WriteLine(json.Serialize(new { type = "header", schema = 1, version = Product.Version, label = "Lean81 CPU capture", synthetic = Synthetic,
                    platform = "Windows", backend = "lean81-win32", interval_s = 2, heartbeat_s = 0.05, workload = "On-demand laptop observation; no workload injected",
                    process_names_enabled = includeNames,
                    hardware = new { machine = Environment.Is64BitOperatingSystem ? "x64" : "x86", logical_cpus = LogicalCpus, memory_bytes = MemoryBytes == 0 ? (object)null : MemoryBytes,
                        cpu_model = CpuModel, scope_warning = "No wake-delay probe; CPU/resource observations only. Processes may be inaccessible; short-lived processes may be missed." } }));
                foreach (var sample in Samples)
                    writer.WriteLine(json.Serialize(new { type = "sample", sample.t, sample.dt, sample.collector_ms, sample.wake_lag_ms, sample.metrics,
                        processes = sample.processes.Select(p => new { p.pid, name = includeNames ? p.name : "redacted", p.cpu_pct, p.rss_bytes }).ToArray() }));
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
                    metrics = new Dictionary<string, object> { { "cpu_pct", cpu }, { "memory_available_pct", 43.0 }, { "power", "ac" } },
                    processes = new List<ProcessReading> { new ProcessReading { pid = 120, name = "ExampleApp", cpu_pct = cpu * 0.7, rss_bytes = 80 * 1024 * 1024 } } });
            }
            return r;
        }
    }
    public static class CpuCapture
    {
        [StructLayout(LayoutKind.Sequential)] struct TimeValue { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [StructLayout(LayoutKind.Sequential)] struct Memory { public uint Length, Load; public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out TimeValue idle, out TimeValue kernel, out TimeValue user);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref Memory memory);
        sealed class ProcessTime { public string Name; public long Start, Rss; public double Cpu; }
        static Dictionary<int, ProcessTime> Processes()
        {
            var result = new Dictionary<int, ProcessTime>();
            foreach (var p in Process.GetProcesses())
            {
                using (p) try { result[p.Id] = new ProcessTime { Name = Product.Clean(p.ProcessName, 128), Start = p.StartTime.ToUniversalTime().Ticks, Cpu = p.TotalProcessorTime.TotalSeconds, Rss = p.WorkingSet64 }; } catch { }
            }
            return result;
        }
        static ulong[] Times()
        {
            TimeValue idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return new[] { idle.Value, kernel.Value + user.Value };
        }
        public static double Percent(ulong idleDelta, ulong totalDelta)
        { return totalDelta == 0 || idleDelta > totalDelta ? 0 : Math.Max(0, Math.Min(100, 100.0 * (totalDelta - idleDelta) / totalDelta)); }
        public static CaptureResult Run(int seconds, CancellationToken token, Action<CaptureSample> progress)
        {
            if (seconds < 4 || seconds > 120) throw new ArgumentOutOfRangeException("seconds");
            var result = new CaptureResult { LogicalCpus = Environment.ProcessorCount, CpuModel = "Unknown CPU" };
            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                if (key != null) result.CpuModel = Product.Clean(key.GetValue("ProcessorNameString") as string, 1000);
            var self = Process.GetCurrentProcess(); double observer = self.TotalProcessorTime.TotalSeconds;
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
                    var memory = new Memory { Length = (uint)Marshal.SizeOf(typeof(Memory)) };
                    if (GlobalMemoryStatusEx(ref memory)) { result.MemoryBytes = memory.Total; sample.metrics["memory_available_pct"] = memory.Total == 0 ? (object)null : 100.0 * memory.Available / memory.Total; }
                    else sample.metrics["memory_available_pct"] = null;
                    var power = SystemInformation.PowerStatus.PowerLineStatus;
                    sample.metrics["power"] = power == PowerLineStatus.Online ? "ac" : power == PowerLineStatus.Offline ? "battery" : "unknown";
                    foreach (var pair in processes)
                    {
                        ProcessTime before; var now = pair.Value;
                        if (!previous.TryGetValue(pair.Key, out before) || now.Start != before.Start || now.Cpu < before.Cpu) continue;
                        double cpu = Math.Min(100, Math.Max(0, (now.Cpu - before.Cpu) / dt / result.LogicalCpus * 100));
                        if (cpu > 0) sample.processes.Add(new ProcessReading { pid = pair.Key, name = now.Name, cpu_pct = cpu, rss_bytes = Math.Max(0, now.Rss) });
                    }
                    sample.processes = sample.processes.OrderByDescending(p => p.cpu_pct).Take(5).ToList();
                    sample.collector_ms = collection.Elapsed.TotalMilliseconds;
                    result.Samples.Add(sample); last = at; previous = processes; times = next;
                    if (progress != null) progress(sample);
                }
            }
            catch (Exception ex) { result.Error = Product.Clean(ex.Message, 400); result.Interrupted = true; }
            finally { result.Duration = watch.Elapsed.TotalSeconds; self.Refresh(); result.ObserverCpu = Math.Max(0, self.TotalProcessorTime.TotalSeconds - observer); self.Dispose(); }
            return result;
        }
    }
}
