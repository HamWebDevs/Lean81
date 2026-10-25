using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Lean81
{
    public static class Profiles
    {
        public static readonly string[] Names = { "Keep every feature", "No home or media sharing", "No sharing, Bluetooth or touch", "Browser / cloud gaming laptop", "Maximum reduction: browser / cloud games" };
        public static UsageProfile Preset(int index)
        {
            if (index < 0 || index >= Names.Length) throw new ArgumentOutOfRangeException("index");
            var p = new UsageProfile();
            if (index > 0) p.HomeGroup = p.MediaSharing = p.OfflineFiles = false;
            if (index == 2) p.Bluetooth = p.TouchKeyboard = false;
            if (index == 3) p.HomeGroup = p.MediaSharing = p.OfflineFiles = p.TouchKeyboard = p.Scanning = p.PortableDevices = p.LinkTracking = false;
            if (index == 4)
            {
                p.Printing = p.Bluetooth = p.IndexedSearch = p.TouchKeyboard = p.HomeGroup = p.MediaSharing = p.OfflineFiles = p.Prefetch = false;
                p.Scanning = p.PortableDevices = p.ErrorReporting = p.LinkTracking = p.Telemetry = p.MediaCenter = p.DiagnosticHelpers = false;
                p.AppUpdaters = p.GameLaunchers = p.CloudSync = p.ChatApps = p.MusicApps = false;
                // Compatibility and the theme stay available unless the user opts out.
            }
            return p;
        }
        public static void Copy(UsageProfile source, UsageProfile target)
        {
            target.Printing = source.Printing; target.Bluetooth = source.Bluetooth; target.IndexedSearch = source.IndexedSearch;
            target.TouchKeyboard = source.TouchKeyboard; target.HomeGroup = source.HomeGroup; target.MediaSharing = source.MediaSharing;
            target.OfflineFiles = source.OfflineFiles; target.Prefetch = source.Prefetch;
            target.Scanning = source.Scanning; target.PortableDevices = source.PortableDevices; target.ErrorReporting = source.ErrorReporting; target.LinkTracking = source.LinkTracking;
            target.Telemetry = source.Telemetry; target.MediaCenter = source.MediaCenter; target.Compatibility = source.Compatibility; target.DiagnosticHelpers = source.DiagnosticHelpers; target.Themes = source.Themes;
            target.AppUpdaters = source.AppUpdaters; target.GameLaunchers = source.GameLaunchers; target.CloudSync = source.CloudSync; target.ChatApps = source.ChatApps; target.MusicApps = source.MusicApps;
        }
        public static UsageProfile AllUnused()
        {
            var p = new UsageProfile();
            foreach (var property in typeof(UsageProfile).GetProperties().Where(x => x.PropertyType == typeof(bool))) property.SetValue(p, false, null);
            return p;
        }
        public static string FeatureReason(ScanItem item, UsageProfile profile)
        {
            var s = item.Before;
            if (s == null) return "Inventory only";
            if (!Policy.NeedsReduction(s)) return "Already reduced or absent";
            if (s.Kind == "setting") return item.CanChange ? "Optional desktop effect; choose individually" : "Unsupported value or missing settings location";
            if (s.Kind == "service")
            {
                var option = Policy.Services.FirstOrDefault(o => o.Name.Equals(s.Target, StringComparison.OrdinalIgnoreCase));
                if (option == null) return "Outside the optional-service allowlist";
                if ((s.Dependents ?? new string[0]).Length > 0) return "Needed by another service";
                if (profile.Uses(option.Feature)) return "Kept by your usage answers";
                return item.CanChange ? "You marked this feature unused" : "Kept by the change policy";
            }
            return item.CanChange ? "Review the purpose before choosing" : "Protected or unsupported; inspection only";
        }
    }
    public sealed class DiskSnapshot
    {
        public string Drive { get; set; }
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public double FreePercent { get { return TotalBytes <= 0 ? 0 : 100.0 * FreeBytes / TotalBytes; } }
        public bool LowSpace { get { return TotalBytes > 0 && (FreePercent < 10 || FreeBytes < 5L * 1024 * 1024 * 1024); } }
    }
    public sealed class DeviceSnapshot
    {
        public string Cpu, Windows, Architecture, Power;
        public int LogicalCpus; public ulong TotalMemory, AvailableMemory, UptimeMilliseconds;
        public int? BatteryPercent;
        public readonly List<DiskSnapshot> Disks = new List<DiskSnapshot>();
        public readonly List<string> Notes = new List<string>();
        public string CapturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        [StructLayout(LayoutKind.Sequential)] struct Memory { public uint Length, Load; public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GlobalMemoryStatusEx(ref Memory memory);
        public static DeviceSnapshot Read()
        {
            var d = new DeviceSnapshot { Cpu = "Unknown CPU", Windows = Environment.OSVersion.VersionString, Architecture = Environment.Is64BitOperatingSystem ? "64-bit Windows" : "32-bit Windows", LogicalCpus = Environment.ProcessorCount, UptimeMilliseconds = GetTickCount64(), Power = "unknown" };
            try { using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) if (key != null) d.Cpu = Product.Clean(key.GetValue("ProcessorNameString") as string, 200); }
            catch { d.Notes.Add("CPU model could not be read."); }
            var m = new Memory { Length = (uint)Marshal.SizeOf(typeof(Memory)) };
            if (GlobalMemoryStatusEx(ref m)) { d.TotalMemory = m.Total; d.AvailableMemory = m.Available; } else d.Notes.Add("Memory information is unavailable.");
            try
            {
                var power = SystemInformation.PowerStatus; d.Power = power.PowerLineStatus == PowerLineStatus.Online ? "ac" : power.PowerLineStatus == PowerLineStatus.Offline ? "battery" : "unknown";
                float fraction = power.BatteryLifePercent;
                if ((power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && power.BatteryChargeStatus != BatteryChargeStatus.Unknown && fraction >= 0 && fraction <= 1) d.BatteryPercent = (int)Math.Round(fraction * 100);
            }
            catch { d.Notes.Add("Power information is unavailable."); }
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { d.Notes.Add("Fixed-drive enumeration was unavailable; capacity scan is incomplete."); return d; }
            foreach (var drive in drives.Take(26))
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    long total = drive.TotalSize, free = drive.AvailableFreeSpace;
                    if (total > 0 && free >= 0 && free <= total) d.Disks.Add(new DiskSnapshot { Drive = drive.Name.TrimEnd('\\'), TotalBytes = total, FreeBytes = free });
                }
                catch { d.Notes.Add("A fixed drive was unavailable; capacity scan is incomplete."); }
            }
            return d;
        }
        public static DeviceSnapshot Demo()
        {
            var d = new DeviceSnapshot { Cpu = "Synthetic low-power CPU", LogicalCpus = 2, Windows = "Windows 8.1 (synthetic)", Architecture = "64-bit Windows", TotalMemory = 4UL << 30, AvailableMemory = 17UL * 1024 * 1024 * 100, UptimeMilliseconds = 137000000, Power = "ac", BatteryPercent = 74 };
            d.Disks.Add(new DiskSnapshot { Drive = "C:", TotalBytes = 120L << 30, FreeBytes = 9L << 30 }); return d;
        }
        public static string Size(ulong bytes) { return bytes == 0 ? "Unavailable" : (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GiB"; }
    }
    public sealed class Recommendation
    {
        public string Title, Reason, Action; public int Page;
    }
    public static class Recommendations
    {
        public static IList<Recommendation> Build(ScanReport report, DeviceSnapshot device, CaptureSummary capture, UsageProfile profile)
        {
            var result = new List<Recommendation>();
            if (report == null) { result.Add(new Recommendation { Title = "Start with a scan", Reason = "Build an inventory before making any choices.", Action = "Scan", Page = 0 }); return result; }
            int startup = report.Items.Count(i => i.Kind == "startup" && i.CanChange), services = report.Items.Count(i => i.Kind == "service" && i.CanChange && i.Before != null && Policy.Services.Any(s => String.Equals(s.Name, i.Before.Target, StringComparison.OrdinalIgnoreCase) && !profile.Uses(s.Feature)));
            if (startup > 0) result.Add(new Recommendation { Title = "Review " + startup + (startup == 1 ? " startup entry" : " startup entries"), Reason = "Choose apps you recognize and don't need at every sign-in. Idle entries may have little performance cost.", Action = "Review startup", Page = 2 });
            if (services > 0) result.Add(new Recommendation { Title = services + " optional services match your answers", Reason = "You marked their features unused. Check each tradeoff before selecting anything.", Action = "Review services", Page = 4 });
            else result.Add(new Recommendation { Title = "Tell Lean81 what you use", Reason = "Uncertain features stay enabled. Usage answers unlock specific optional-service choices.", Action = "Usage profile", Page = 1 });
            if (device != null && device.Disks.Any(d => d.LowSpace)) result.Add(new Recommendation { Title = "A fixed drive is low on free space", Reason = "Review storage and open Windows Disk Cleanup if useful. Lean81 does not delete your files.", Action = "Storage", Page = 8 });
            if (capture != null && capture.AverageAvailableMemory.HasValue && capture.AverageAvailableMemory < 15) result.Add(new Recommendation { Title = "Low available memory was observed", Reason = "Try fewer browser tabs and background apps, then repeat the same workload. This is an observation, not a memory-cleaner score.", Action = "CPU results", Page = 6 });
            if (capture != null && capture.PeakCpu >= 80) result.Add(new Recommendation { Title = "High CPU samples were observed", Reason = "Inspect the captured busy processes. A two-second sample cannot identify every short spike.", Action = "Resource capture", Page = 6 });
            if (capture != null && capture.AverageDiskBusy >= 80) result.Add(new Recommendation { Title = "High aggregate disk activity was observed", Reason = "Inspect the resource details and Resource Monitor. Page-ins, app activity and maintenance can all contribute. Process I/O also includes network and devices.", Action = "Resource capture", Page = 6 });
            if (capture != null && capture.PeakCommit >= 90) result.Add(new Recommendation { Title = "Committed memory approached its limit", Reason = "Save work, close unused apps and keep the paging file enabled. Inspect resource details during the slowdown.", Action = "Resource capture", Page = 6 });
            result.Add(new Recommendation { Title = capture == null ? "Record a CPU baseline" : "Compare another run with your baseline", Reason = "Use the same workload, duration and power state. Results are observations and do not establish what caused a change.", Action = "Resource capture", Page = 6 });
            return result;
        }
    }
    public sealed class UserWorkspace
    {
        public int Schema { get; set; }
        public UsageProfile Profile { get; set; }
        public string Workload { get; set; }
        public string BaselineId { get; set; }
        public List<CaptureSummary> History { get; set; }
        public UserWorkspace() { Schema = 1; Profile = new UsageProfile(); Workload = "Normal laptop use"; History = new List<CaptureSummary>(); }
        public void Add(CaptureSummary result)
        {
            CaptureSummary.Validate(result); History.RemoveAll(s => s.Id == result.Id); History.Insert(0, result);
            if (History.Count > 12)
            {
                var remove = History.LastOrDefault(s => s.Id != BaselineId); if (remove != null) History.Remove(remove);
            }
        }
        public static void Validate(UserWorkspace value)
        {
            if (value == null || value.Schema != 1 || value.Profile == null || value.History == null || value.History.Count > 12 || value.Workload == null || value.Workload.Length > 80 || value.Workload.Any(Char.IsControl)) throw new InvalidOperationException("Unsupported workspace settings.");
            foreach (var summary in value.History) CaptureSummary.Validate(summary);
            if (value.History.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != value.History.Count || value.BaselineId != null && !value.History.Any(s => s.Id == value.BaselineId)) throw new InvalidOperationException("Invalid saved result identities.");
        }
    }
    // Preferences contain usage answers and aggregate numbers, never commands or an apply plan.
    public sealed class WorkspaceStore
    {
        readonly string directory; public string Warning { get; private set; }
        public WorkspaceStore(string directory) { this.directory = directory; }
        public static WorkspaceStore Local() { return new WorkspaceStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lean81")); }
        void CheckLocation()
        {
            for (var d = new DirectoryInfo(directory); d != null; d = d.Parent) if (d.Exists && (d.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Settings path is redirected; preferences will stay in memory.");
            string file = Path.Combine(directory, "workspace.json");
            if (File.Exists(file) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("Settings file is redirected; preferences will stay in memory.");
        }
        public UserWorkspace Load()
        {
            try
            {
                CheckLocation(); string file = Path.Combine(directory, "workspace.json"); if (!File.Exists(file)) return new UserWorkspace();
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (stream.Length > 128 * 1024) throw new IOException("Saved settings exceed the size limit.");
                    using (var reader = new StreamReader(stream, Encoding.UTF8)) { var data = Product.Json().Deserialize<UserWorkspace>(reader.ReadToEnd()); UserWorkspace.Validate(data); return data; }
                }
            }
            catch { Warning = "Saved preferences were unavailable or invalid; safe defaults are in use."; return new UserWorkspace(); }
        }
        public void Save(UserWorkspace value)
        {
            UserWorkspace.Validate(value); CheckLocation(); Directory.CreateDirectory(directory); CheckLocation();
            byte[] bytes = new UTF8Encoding(false).GetBytes(Product.Json().Serialize(value)); if (bytes.Length > 128 * 1024) throw new IOException("Settings exceed the size limit.");
            string file = Path.Combine(directory, "workspace.json"), temp = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                CheckLocation(); if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
                Warning = null;
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
