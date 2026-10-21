using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Lean81
{
    public static class Reports
    {
        static string H(object value) { return WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture)); }
        public static string Build(ScanReport scan, DeviceSnapshot device, CaptureSummary capture, bool includeSoftwareNames, bool synthetic)
        {
            if (scan == null) throw new InvalidOperationException("Scan the laptop before exporting a report.");
            var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Lean81 laptop report</title><style>body{font:16px/1.6 Segoe UI,Arial,sans-serif;color:#1b2b3b;background:#f4f7f9;margin:0;padding:32px}main{max-width:960px;margin:auto;background:white;padding:32px}h1{margin:0;color:#0c806f}h2{margin-top:28px}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:10px;text-align:left;border-bottom:1px solid #dfe7ec;overflow-wrap:anywhere}th{background:#edf3f5}.note{color:#566a79}strong{color:#0c806f}@media print{body{padding:0;background:white}main{padding:0}tr{break-inside:avoid}}</style><main><h1>Lean81 laptop report</h1>");
            html.Append("<p>Version ").Append(H(Product.Version)).Append(" · Scan UTC ").Append(H(scan.CapturedUtc)).Append(synthetic ? " · <strong>SYNTHETIC DEMO</strong>" : "").Append("</p>");
            html.Append("<p class=\"note\">This report omits startup commands, executable paths, task XML, receipt contents, account details and process names. Software names are ").Append(includeSoftwareNames ? "included by request" : "omitted").Append(". Scan errors are summarized as counts because their messages may contain private paths.</p>");
            if (device != null)
            {
                html.Append("<h2>Device snapshot</h2><table><tr><th>Detail</th><th>Observation</th></tr>");
                Row(html, "Windows", device.Windows); Row(html, "Architecture", device.Architecture); Row(html, "CPU", device.Cpu); Row(html, "Logical processors", device.LogicalCpus);
                Row(html, "Physical memory", DeviceSnapshot.Size(device.TotalMemory)); Row(html, "Available physical memory", DeviceSnapshot.Size(device.AvailableMemory));
                Row(html, "Power source", device.Power); Row(html, "Battery", device.BatteryPercent.HasValue ? device.BatteryPercent.Value + "%" : "Unavailable");
                html.Append("</table><h2>Fixed-drive capacity</h2><table><tr><th>Drive</th><th>Total GiB</th><th>Available GiB</th><th>Available %</th><th>Observation</th></tr>");
                foreach (var disk in device.Disks) html.Append("<tr><td>").Append(H(disk.Drive)).Append("</td><td>").Append(H((disk.TotalBytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture))).Append("</td><td>").Append(H((disk.FreeBytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture))).Append("</td><td>").Append(H(disk.FreePercent.ToString("0.0", CultureInfo.InvariantCulture))).Append("</td><td>").Append(disk.LowSpace ? "Low free space" : "Capacity observed").Append("</td></tr>");
                html.Append("</table><p class=\"note\">Capacity is not a disk-health, SSD, SMART or temperature assessment.</p>");
            }
            html.Append("<h2>Background inventory</h2><table><tr><th>Category</th><th>Found</th><th>Selectable for review</th></tr>");
            foreach (var kind in new[] { "startup", "task", "service" }) html.Append("<tr><td>").Append(H(kind)).Append("</td><td>").Append(scan.Items.Count(i => i.Kind == kind)).Append("</td><td>").Append(scan.Items.Count(i => i.Kind == kind && i.CanChange)).Append("</td></tr>");
            html.Append("</table><p>Desktop applications found: ").Append(scan.Apps.Count).Append(". Scan notes: ").Append(scan.Notes.Count).Append(". Open Scan notes in the app for local detail.</p>");
            if (capture != null)
            {
                CaptureSummary.Validate(capture); html.Append("<h2>Latest CPU result</h2><table><tr><th>Observation</th><th>Value</th></tr>");
                // Workload labels can be personal; keep them out of the default support report.
                Row(html, "Average CPU", capture.AverageCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%"); Row(html, "Peak sampled CPU", capture.PeakCpu.ToString("0.0", CultureInfo.InvariantCulture) + "%");
                Row(html, "Observed seconds", capture.ObservedSeconds.ToString("0.0", CultureInfo.InvariantCulture)); Row(html, "Time at 80%+ CPU", capture.BusySeconds.ToString("0.0", CultureInfo.InvariantCulture) + " seconds");
                Row(html, "Valid CPU samples", capture.ValidCpuSamples + " / " + capture.Samples); Row(html, "Completion", capture.Complete ? "Completed" : "Interrupted or error"); Row(html, "Source", capture.Synthetic ? "Synthetic" : "Observed Windows capture");
                html.Append("</table>");
            }
            if (includeSoftwareNames)
            {
                html.Append("<h2>Software names (opt-in)</h2><table><tr><th>Name</th><th>Category</th><th>State</th></tr>");
                foreach (var item in scan.Items) html.Append("<tr><td>").Append(H(Product.Clean(item.Name, 180))).Append("</td><td>").Append(H(item.Kind)).Append("</td><td>").Append(H(item.Status)).Append("</td></tr>");
                foreach (var app in scan.Apps) html.Append("<tr><td>").Append(H(Product.Clean(app.Name, 180))).Append("</td><td>Desktop app</td><td>").Append(H(Product.Clean(app.Version, 80))).Append("</td></tr>");
                html.Append("</table>");
            }
            return html.Append("<p class=\"note\">No settings are changed by this report. CPU observations do not prove the cause of a slowdown. Physical Windows 8.1 validation is pending; this preview is tested on newer Windows. Generated locally by Lean81 · HamWebDevs.</p></main></html>").ToString();
        }
        static void Row(StringBuilder html, string title, object value) { html.Append("<tr><td>").Append(H(title)).Append("</td><td>").Append(H(value)).Append("</td></tr>"); }
    }
    public sealed class ToolSpec
    {
        public string Id, Title, File, Args, Purpose;
        public ToolSpec(string id, string title, string file, string args, string purpose) { Id = id; Title = title; File = file; Args = args; Purpose = purpose; }
    }
    public static class WindowsTools
    {
        public static readonly ToolSpec[] All = {
            new ToolSpec("taskmgr", "Task Manager", "Taskmgr.exe", "", "Inspect live CPU and memory use, and Windows' startup approval state."),
            new ToolSpec("resources", "Resource Monitor", "resmon.exe", "", "Inspect CPU, memory, disk and network activity in more detail."),
            new ToolSpec("tasks", "Task Scheduler", "taskschd.msc", "", "Read a scheduled task's triggers and purpose before changing it."),
            new ToolSpec("services", "Services", "services.msc", "", "Inspect service properties or investigate a partial-state recovery error."),
            new ToolSpec("apps", "Programs and Features", "control.exe", "appwiz.cpl", "Uninstall recognized desktop software with its normal Windows uninstaller."),
            new ToolSpec("cleanup", "Windows Disk Cleanup", "cleanmgr.exe", "", "Open Windows' own cleanup choices; review categories before deleting files."),
            new ToolSpec("power", "Power Options", "control.exe", "powercfg.cpl", "Inspect power plans. High-performance plans may use more battery and heat."),
            new ToolSpec("devices", "Device Manager", "devmgmt.msc", "", "Inspect hardware and driver errors. Lean81 never removes drivers."),
            new ToolSpec("performance", "Performance Monitor", "perfmon.exe", "", "Inspect Windows performance counters when more detail is needed.")
        };
    }
}
