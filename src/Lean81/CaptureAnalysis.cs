using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Lean81
{
    public sealed class CaptureSummary
    {
        public string Id { get; set; }
        public string CreatedUtc { get; set; }
        public string Workload { get; set; }
        public string MachineKey { get; set; }
        public string HardwareKey { get; set; }
        public string Power { get; set; }
        public bool Synthetic { get; set; }
        public bool Complete { get; set; }
        public int Samples { get; set; }
        public int ValidCpuSamples { get; set; }
        public double Duration { get; set; }
        public double ObservedSeconds { get; set; }
        public double AverageCpu { get; set; }
        public double PeakCpu { get; set; }
        public double BusySeconds { get; set; }
        public double ObserverCpu { get; set; }
        public double? AverageAvailableMemory { get; set; }
        public static CaptureSummary From(CaptureResult result, string workload, string machineKey)
        {
            if (result == null || result.Samples == null || result.Samples.Count == 0) throw new InvalidOperationException("No capture samples are available.");
            var summary = new CaptureSummary { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), Workload = Product.Clean(workload, 80).Trim(), MachineKey = machineKey,
                HardwareKey = Product.Hash(result.LogicalCpus + "\n" + result.MemoryBytes + "\n" + result.CpuModel), Synthetic = result.Synthetic, Complete = !result.Interrupted && result.Error == null, Samples = result.Samples.Count, Duration = result.Duration, ObserverCpu = result.ObserverCpu };
            double weighted = 0, memory = 0, memorySeconds = 0; var powers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var sample in result.Samples)
            {
                if (!Product.Finite(sample.dt) || sample.dt <= 0) throw new InvalidOperationException("Invalid capture interval.");
                object raw; double value;
                if (sample.metrics.TryGetValue("cpu_pct", out raw) && Number(raw, out value))
                {
                    summary.ValidCpuSamples++; summary.ObservedSeconds += sample.dt; weighted += value * sample.dt;
                    summary.PeakCpu = Math.Max(summary.PeakCpu, value); if (value >= 80) summary.BusySeconds += sample.dt;
                }
                if (sample.metrics.TryGetValue("memory_available_pct", out raw) && Number(raw, out value)) { memory += value * sample.dt; memorySeconds += sample.dt; }
                string power = sample.metrics.TryGetValue("power", out raw) ? raw as string : null;
                powers.Add(power == "ac" || power == "battery" ? power : "unknown");
            }
            if (summary.ObservedSeconds <= 0) throw new InvalidOperationException("No valid CPU samples were collected.");
            summary.AverageCpu = Math.Min(summary.PeakCpu, weighted / summary.ObservedSeconds); summary.AverageAvailableMemory = memorySeconds > 0 ? (double?)(memory / memorySeconds) : null;
            summary.Power = powers.Count == 1 ? powers.Single() : "mixed"; if (summary.Workload.Length == 0) summary.Workload = "Normal laptop use";
            Validate(summary); return summary;
        }
        static bool Number(object raw, out double value)
        {
            value = 0; if (raw == null || raw is bool || raw is string) return false;
            try { value = Convert.ToDouble(raw, CultureInfo.InvariantCulture); return Product.Finite(value) && value >= 0 && value <= 100; } catch { return false; }
        }
        static bool Range(double value, double minimum, double maximum) { return Product.Finite(value) && value >= minimum && value <= maximum; }
        static bool Hash(string value) { return value != null && value.Length == 64 && value.All(c => "0123456789abcdef".Contains(c)); }
        public static void Validate(CaptureSummary value)
        {
            Guid id; DateTime date;
            if (value == null || !Guid.TryParseExact(value.Id, "N", out id) || !DateTime.TryParseExact(value.CreatedUtc, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)
                || String.IsNullOrWhiteSpace(value.Workload) || value.Workload.Length > 80 || value.Workload.Any(Char.IsControl) || !Hash(value.MachineKey) || !Hash(value.HardwareKey)
                || !new[] { "ac", "battery", "mixed", "unknown" }.Contains(value.Power) || value.Samples < 1 || value.Samples > 1000 || value.ValidCpuSamples < 1 || value.ValidCpuSamples > value.Samples
                || !Range(value.Duration, 0.01, 3600) || !Range(value.ObservedSeconds, 0.01, value.Duration + 1) || !Range(value.AverageCpu, 0, 100) || !Range(value.PeakCpu, value.AverageCpu, 100)
                || !Range(value.BusySeconds, 0, value.ObservedSeconds) || !Range(value.ObserverCpu, 0, 3600 * 65536) || value.AverageAvailableMemory.HasValue && !Range(value.AverageAvailableMemory.Value, 0, 100))
                throw new InvalidOperationException("Invalid saved capture summary.");
        }
    }
    public sealed class CaptureComparison
    {
        public bool Comparable; public string Explanation;
        public double CpuDifference, PeakDifference, BusyDifference; public double? MemoryDifference;
        public static CaptureComparison Compare(CaptureSummary baseline, CaptureSummary current)
        {
            CaptureSummary.Validate(baseline); CaptureSummary.Validate(current);
            var problems = new List<string>();
            if (baseline.Id == current.Id) problems.Add("Choose a different result from the baseline.");
            if (baseline.MachineKey != current.MachineKey || baseline.HardwareKey != current.HardwareKey) problems.Add("These results have different machine or hardware identities.");
            if (baseline.Synthetic != current.Synthetic) problems.Add("Synthetic results cannot be compared with real captures.");
            if (!baseline.Complete || !current.Complete) problems.Add("A capture was stopped early or reported an error.");
            if (baseline.ValidCpuSamples != baseline.Samples || current.ValidCpuSamples != current.Samples) problems.Add("CPU observations are missing from a capture.");
            if (Math.Min(baseline.ObservedSeconds, current.ObservedSeconds) < 15 || Math.Max(baseline.ObservedSeconds, current.ObservedSeconds) / Math.Min(baseline.ObservedSeconds, current.ObservedSeconds) > 1.25) problems.Add("Use captures of at least 15 seconds with similar durations.");
            if (baseline.Power != current.Power || baseline.Power == "mixed" || baseline.Power == "unknown") problems.Add("Use the same known power source for both captures.");
            if (!String.Equals(baseline.Workload.Trim(), current.Workload.Trim(), StringComparison.OrdinalIgnoreCase)) problems.Add("Use matching workload labels and repeat the same activity.");
            if (problems.Count > 0) return new CaptureComparison { Comparable = false, Explanation = String.Join("\r\n", problems) };
            return new CaptureComparison { Comparable = true, CpuDifference = current.AverageCpu - baseline.AverageCpu, PeakDifference = current.PeakCpu - baseline.PeakCpu,
                BusyDifference = current.BusySeconds - baseline.BusySeconds,
                MemoryDifference = current.AverageAvailableMemory.HasValue && baseline.AverageAvailableMemory.HasValue ? current.AverageAvailableMemory - baseline.AverageAvailableMemory : null,
                Explanation = "Matching recorded conditions. This comparison describes observations; it does not prove a change caused an improvement. Repeat the same activity and let background startup settle." };
        }
        public string Text()
        {
            if (!Comparable) return "Comparison needs matching conditions\r\n\r\n" + Explanation;
            return "Current result minus baseline\r\n\r\nAverage CPU: " + Signed(CpuDifference) + " percentage points\r\nPeak CPU: " + Signed(PeakDifference) + " percentage points\r\nTime at 80%+ CPU: " + Signed(BusyDifference) + " seconds\r\nAvailable memory: " + (MemoryDifference.HasValue ? Signed(MemoryDifference.Value) + " percentage points" : "unavailable") + "\r\n\r\n" + Explanation;
        }
        static string Signed(double value) { return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture); }
    }
}
