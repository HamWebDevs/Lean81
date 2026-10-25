using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace Lean81
{
    public sealed class ReportOptions
    {
        public bool IncludeSoftwareNames, IncludeHistory, Synthetic;
    }
    public static class Reports
    {
        static string H(object value) { return WebUtility.HtmlEncode(Convert.ToString(value, CultureInfo.InvariantCulture)); }
        static string N(double value) { return value.ToString("0.0", CultureInfo.InvariantCulture); }
        static string Utc(string value)
        {
            DateTime date;
            return DateTime.TryParseExact(value, "o", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)
                ? date.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC" : Product.Clean(value, 80);
        }
        static string Signed(double value) { return value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture); }
        public static string Build(ScanReport scan, DeviceSnapshot device, CaptureSummary capture, bool includeSoftwareNames, bool synthetic)
        {
            return Build(scan, device, capture, null, null, new UsageProfile(), new ReportOptions { IncludeSoftwareNames = includeSoftwareNames, Synthetic = synthetic });
        }
        public static string Build(ScanReport scan, DeviceSnapshot device, CaptureSummary capture, CaptureSummary baseline, IEnumerable<CaptureSummary> history, UsageProfile profile, ReportOptions options)
        {
            if (scan == null) throw new InvalidOperationException("Scan the laptop before exporting a report.");
            if (options == null || profile == null) throw new ArgumentNullException("options");
            if (capture != null) CaptureSummary.Validate(capture);
            if (baseline != null) CaptureSummary.Validate(baseline);
            var saved = options.IncludeHistory && history != null ? history.Take(13).ToList() : new List<CaptureSummary>();
            if (saved.Count > 12) throw new InvalidOperationException("Report history exceeds the saved-result limit.");
            foreach (var item in saved) CaptureSummary.Validate(item);
            bool synthetic = options.Synthetic || capture != null && capture.Synthetic || baseline != null && baseline.Synthetic || saved.Any(s => s.Synthetic);
            var html = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'\"><title>Lean81 laptop report</title><style>");
            html.Append(Css).Append("</style></head><body><main><header><p class=\"eyebrow\">LEAN81 / LAPTOP OBSERVATIONS</p><h1>A clearer picture of your laptop.</h1><p class=\"lead\">Background choices, CPU evidence and the next useful step.</p><p>Version ").Append(H(Product.Version)).Append(" · Scan ").Append(H(Utc(scan.CapturedUtc))).Append("</p>");
            if (synthetic) html.Append("<p class=\"banner\"><strong>SYNTHETIC DEMO</strong> · This report includes invented example observations.</p>");
            html.Append("<nav aria-label=\"Report sections\"><a href=\"#next\">Next steps</a><a href=\"#performance\">CPU evidence</a><a href=\"#device\">Device</a><a href=\"#inventory\">Inventory</a><a href=\"#privacy\">Privacy &amp; limits</a></nav></header><p class=\"mobile-hint\">Tables with several columns scroll sideways on small screens.</p><div class=\"cards\">");
            Card(html, "Average CPU", capture == null ? "Not captured" : N(capture.AverageCpu) + "%", "Time-weighted observed intervals");
            Card(html, "Available memory", capture == null || !capture.AverageAvailableMemory.HasValue ? "Unavailable" : N(capture.AverageAvailableMemory.Value) + "%", "Average during the CPU capture");
            Card(html, "Startup entries to review", scan.Items.Count(i => i.Kind == "startup" && i.CanChange).ToString(CultureInfo.InvariantCulture), "Eligibility does not measure CPU cost");
            Card(html, "Scan notes", scan.Notes.Count.ToString(CultureInfo.InvariantCulture), "Read local scan notes for full detail");
            html.Append("</div><section id=\"next\"><h2>Your next useful steps</h2><p>Based on this inventory, your usage answers and the reported CPU result. Each step needs your review.</p><ol class=\"steps\">");
            foreach (var rec in Recommendations.Build(scan, device, capture, profile)) html.Append("<li><h3>").Append(H(rec.Title)).Append("</h3><p>").Append(H(rec.Reason)).Append("</p><p class=\"note\">In Lean81: ").Append(H(rec.Action)).Append("</p></li>");
            html.Append("</ol></section><section id=\"performance\"><h2>CPU evidence</h2>");
            if (capture == null) html.Append("<p class=\"empty\">No CPU result is included. In Resource capture, record the same activity before and after a change, using the same power source and duration.</p>");
            else
            {
                html.Append("<p>Result recorded ").Append(H(Utc(capture.CreatedUtc))).Append(". This capture and the inventory are separate snapshots; the report does not link a setting change to a result.</p>");
                html.Append("<div class=\"table-wrap\"><table><caption>Current capture and measurement coverage</caption><thead><tr><th scope=\"col\">Observation</th><th scope=\"col\">Value</th></tr></thead><tbody>");
                Row(html, "Average CPU", N(capture.AverageCpu) + "%"); Row(html, "Peak sampled CPU", N(capture.PeakCpu) + "%");
                Row(html, "Time at 80%+ CPU", N(capture.BusySeconds) + " seconds");
                Row(html, "Average available physical memory", capture.AverageAvailableMemory.HasValue ? N(capture.AverageAvailableMemory.Value) + "%" : "Unavailable");
                Row(html, "Average aggregate physical-disk busy", capture.AverageDiskBusy.HasValue ? N(capture.AverageDiskBusy.Value) + "% (" + capture.ValidDiskSamples + "/" + capture.Samples + " samples)" : "Unavailable (not recorded by older versions or counters unavailable)");
                Row(html, "Peak committed memory / commit limit", capture.PeakCommit.HasValue ? N(capture.PeakCommit.Value) + "% (" + capture.ValidCommitSamples + "/" + capture.Samples + " samples)" : "Unavailable");
                Row(html, "Peak nonpaged kernel pool", capture.PeakNonpagedBytes.HasValue ? N(capture.PeakNonpagedBytes.Value / 1048576) + " MiB" : "Unavailable");
                Row(html, "Elapsed / observed CPU time", N(capture.Duration) + " / " + N(capture.ObservedSeconds) + " seconds");
                Row(html, "Valid CPU samples", capture.ValidCpuSamples + " / " + capture.Samples); Row(html, "Power source", capture.Power);
                Row(html, "Completion", capture.Complete ? "Completed" : "Interrupted or error"); Row(html, "Observer CPU cost", N(capture.ObserverCpu) + " CPU seconds");
                Row(html, "Source", capture.Synthetic ? "Synthetic" : "Observed Windows capture"); html.Append("</tbody></table></div>");
                if (!capture.Complete || capture.ValidCpuSamples != capture.Samples) html.Append("<p class=\"banner\">Partial evidence: missing CPU samples are excluded from the time-weighted average, and interrupted results cannot pass the baseline comparison checks.</p>");
            }
            Comparison(html, baseline, capture);
            if (options.IncludeHistory) History(html, saved, baseline, capture);
            html.Append("<p class=\"note\">Two-second sampling can miss short spikes and inaccessible processes. There is no input-latency or wake-delay measurement. Matching recorded conditions do not prove matching activity or causality.</p></section>");
            Device(html, device);
            html.Append("<section id=\"inventory\"><h2>Background inventory</h2><div class=\"table-wrap\"><table class=\"wide\"><caption>Items observed in the scan</caption><thead><tr><th scope=\"col\">Category</th><th scope=\"col\">Found</th><th scope=\"col\">Selectable for review</th><th scope=\"col\">Kept / inspection only</th></tr></thead><tbody>");
            foreach (var kind in new[] { "startup", "task", "service", "setting" })
            {
                int total = scan.Items.Count(i => i.Kind == kind), selectable = scan.Items.Count(i => i.Kind == kind && i.CanChange);
                html.Append("<tr><th scope=\"row\">").Append(H(kind)).Append("</th><td>").Append(total).Append("</td><td>").Append(selectable).Append("</td><td>").Append(total - selectable).Append("</td></tr>");
            }
            html.Append("</tbody></table></div><p>Desktop applications found: ").Append(scan.Apps.Count).Append(". Scan notes: ").Append(scan.Notes.Count).Append(". Counts describe observed entries, not CPU usage or a complete list of every background component.</p>");
            if (scan.Notes.Count > 0) html.Append("<p class=\"banner\">Some scan observations need review. Open Scan notes in Lean81 before treating the inventory as complete. Private error messages are excluded here.</p>");
            if (options.IncludeSoftwareNames)
            {
                html.Append("<div class=\"table-wrap\"><table><caption>Software names (explicit opt-in)</caption><thead><tr><th scope=\"col\">Name</th><th scope=\"col\">Category</th></tr></thead><tbody>");
                foreach (var item in scan.Items) html.Append("<tr><td>").Append(H(Product.Clean(item.Name, 180))).Append("</td><td>").Append(H(item.Kind)).Append("</td></tr>");
                foreach (var app in scan.Apps) html.Append("<tr><td>").Append(H(Product.Clean(app.Name, 180))).Append("</td><td>Desktop app</td></tr>");
                html.Append("</tbody></table></div>");
            }
            html.Append("</section><section id=\"privacy\"><h2>Privacy &amp; limits</h2><p>Startup commands, executable paths, task XML, recovery receipts, account details, machine identifiers, workload labels and process names are excluded. Software names are ").Append(options.IncludeSoftwareNames ? "included by request" : "omitted").Append(". Saved-result history is ").Append(options.IncludeHistory ? "included as aggregate observations" : "omitted").Append(".</p><p>This self-contained report has no scripts, remote resources, telemetry or uploads. No settings are changed by export. Review the report before sharing; hardware details and capture dates can still identify a device or activity.</p><p>Capacity is not a disk-health, SSD, SMART or temperature assessment. Physical Windows 8.1 validation is pending; automated testing uses newer Windows. No speed improvement is promised.</p></section><footer>Generated locally by Lean81 · HamWebDevs · ").Append(H(Product.Version)).Append("</footer></main></body></html>");
            return html.ToString();
        }
        static void Card(StringBuilder html, string title, string value, string note)
        { html.Append("<article class=\"card\"><h2>").Append(H(title)).Append("</h2><p class=\"value\">").Append(H(value)).Append("</p><p class=\"note\">").Append(H(note)).Append("</p></article>"); }
        static void Row(StringBuilder html, string title, object value)
        { html.Append("<tr><th scope=\"row\">").Append(H(title)).Append("</th><td>").Append(H(value)).Append("</td></tr>"); }
        static void Comparison(StringBuilder html, CaptureSummary baseline, CaptureSummary current)
        {
            html.Append("<h3>Baseline comparison</h3>");
            if (baseline == null || current == null) { html.Append("<p class=\"empty\">A baseline and a separate current result are needed. In Saved results, choose Use as baseline, repeat the activity and export again with comparison enabled.</p>"); return; }
            var result = CaptureComparison.Compare(baseline, current);
            if (!result.Comparable)
            {
                html.Append("<div class=\"banner\"><strong>Comparison unavailable</strong><ul>");
                foreach (var reason in result.Explanation.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)) html.Append("<li>").Append(H(reason)).Append("</li>");
                html.Append("</ul><p>No difference is calculated from these mismatched observations.</p></div>"); return;
            }
            html.Append("<p>Matching recorded conditions · baseline ").Append(H(Utc(baseline.CreatedUtc))).Append(". Values below are current minus baseline; CPU changes are percentage points.</p><div class=\"table-wrap\"><table class=\"wide\"><caption>Observed differences, not proof of improvement</caption><thead><tr><th scope=\"col\">Measurement</th><th scope=\"col\">Baseline</th><th scope=\"col\">Current</th><th scope=\"col\">Difference</th></tr></thead><tbody>");
            Delta(html, "Average CPU", baseline.AverageCpu, current.AverageCpu, result.CpuDifference, "%", " pp");
            Delta(html, "Peak sampled CPU", baseline.PeakCpu, current.PeakCpu, result.PeakDifference, "%", " pp");
            Delta(html, "Time at 80%+ CPU", baseline.BusySeconds, current.BusySeconds, result.BusyDifference, " s", " s");
            if (result.MemoryDifference.HasValue) Delta(html, "Available physical memory", baseline.AverageAvailableMemory.Value, current.AverageAvailableMemory.Value, result.MemoryDifference.Value, "%", " pp");
            else html.Append("<tr><th scope=\"row\">Available physical memory</th><td colspan=\"3\">Difference unavailable: a capture has no memory reading.</td></tr>");
            if (result.DiskDifference.HasValue) Delta(html, "Aggregate physical-disk busy", baseline.AverageDiskBusy.Value, current.AverageDiskBusy.Value, result.DiskDifference.Value, "%", " pp");
            else html.Append("<tr><th scope=\"row\">Aggregate physical-disk busy</th><td colspan=\"3\">Difference unavailable: disk coverage is missing or incomplete.</td></tr>");
            html.Append("</tbody></table></div><p class=\"note\">").Append(H(result.Explanation)).Append("</p>");
        }
        static void Delta(StringBuilder html, string title, double before, double after, double difference, string unit, string differenceUnit)
        { html.Append("<tr><th scope=\"row\">").Append(H(title)).Append("</th><td>").Append(N(before)).Append(unit).Append("</td><td>").Append(N(after)).Append(unit).Append("</td><td>").Append(Signed(difference)).Append(differenceUnit).Append("</td></tr>"); }
        static void History(StringBuilder html, IList<CaptureSummary> saved, CaptureSummary baseline, CaptureSummary current)
        {
            html.Append("<h3>Saved observations</h3><p>Workload labels and stored identities are withheld. These rows are a history of observations with potentially different activities and conditions.</p>");
            if (saved.Count == 0) { html.Append("<p class=\"empty\">No saved observations are available.</p>"); return; }
            html.Append("<div class=\"table-wrap\"><table class=\"wide\"><caption>Aggregate history, up to 12 results</caption><thead><tr><th scope=\"col\">Result / UTC</th><th scope=\"col\">Average CPU</th><th scope=\"col\">Observed</th><th scope=\"col\">Power / coverage</th></tr></thead><tbody>");
            for (int i = 0; i < saved.Count; i++)
            {
                var row = saved[i]; string role = (baseline != null && row.Id == baseline.Id ? " · baseline" : "") + (current != null && row.Id == current.Id ? " · current" : "");
                html.Append("<tr><th scope=\"row\">Result ").Append(i + 1).Append(H(role)).Append("<br>").Append(H(Utc(row.CreatedUtc))).Append("</th><td>").Append(N(row.AverageCpu)).Append("%</td><td>").Append(N(row.ObservedSeconds)).Append(" s</td><td>").Append(H(row.Power)).Append(" · ").Append(row.ValidCpuSamples).Append("/").Append(row.Samples).Append(row.Complete ? " · complete" : " · partial").Append(row.Synthetic ? " · synthetic" : "").Append("</td></tr>");
            }
            html.Append("</tbody></table></div>");
        }
        static void Device(StringBuilder html, DeviceSnapshot device)
        {
            html.Append("<section id=\"device\"><h2>Device &amp; storage</h2>");
            if (device == null) { html.Append("<p class=\"empty\">Device snapshot unavailable. Refresh Device &amp; storage in Lean81 and export again.</p></section>"); return; }
            html.Append("<p>Snapshot ").Append(H(Utc(device.CapturedUtc))).Append(" · ").Append(device.Notes.Count).Append(" unavailable-reading notes. The device snapshot may have been taken separately from the scan and capture.</p><div class=\"table-wrap\"><table><caption>Hardware and power snapshot</caption><tbody>");
            Row(html, "Windows", device.Windows); Row(html, "Architecture", device.Architecture); Row(html, "CPU", device.Cpu); Row(html, "Logical processors", device.LogicalCpus);
            Row(html, "Physical memory", DeviceSnapshot.Size(device.TotalMemory)); Row(html, "Available physical memory", DeviceSnapshot.Size(device.AvailableMemory)); Row(html, "Power source", device.Power);
            Row(html, "Battery", device.BatteryPercent.HasValue ? device.BatteryPercent.Value + "%" : "Unavailable"); html.Append("</tbody></table></div>");
            if (device.Disks.Count == 0) html.Append("<p class=\"empty\">No fixed-drive capacity readings were available; this does not establish that no fixed drives exist.</p>");
            else
            {
                html.Append("<div class=\"table-wrap\"><table class=\"wide\"><caption>Fixed-drive capacity available to your account</caption><thead><tr><th scope=\"col\">Drive</th><th scope=\"col\">Total GiB</th><th scope=\"col\">Available GiB</th><th scope=\"col\">Available %</th><th scope=\"col\">Observation</th></tr></thead><tbody>");
                foreach (var disk in device.Disks) html.Append("<tr><th scope=\"row\">").Append(H(disk.Drive)).Append("</th><td>").Append(N(disk.TotalBytes / 1073741824.0)).Append("</td><td>").Append(N(disk.FreeBytes / 1073741824.0)).Append("</td><td>").Append(N(disk.FreePercent)).Append("%</td><td>").Append(disk.LowSpace ? "Low free space" : "Capacity observed").Append("</td></tr>");
                html.Append("</tbody></table></div>");
            }
            html.Append("</section>");
        }
        const string Css = @"*{box-sizing:border-box}html{scroll-behavior:smooth}body{font:15px/1.65 'Segoe UI',Arial,sans-serif;color:#213547;background:#edf2f5;margin:0}main{max-width:1120px;margin:auto;padding:36px 28px}header{background:#192f40;color:#e4edf2;padding:32px;border-radius:14px}h1{font-size:36px;line-height:1.2;letter-spacing:-1px;color:#fff;margin:12px 0}.eyebrow{font-size:12px;letter-spacing:2px;color:#7de5cc;margin:0}.lead{font-size:18px;color:#c4d6e1}nav{display:flex;flex-wrap:wrap;gap:10px 24px;margin-top:24px}nav a{color:#a2f2df;text-underline-offset:4px}a:focus{outline:3px solid #f6ba52;outline-offset:4px}.cards{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:16px;margin:22px 0}.card,section{background:#fff;border:1px solid #d9e3ea;border-radius:12px}.card{padding:20px}.card h2{font-size:13px;margin:0;color:#526779}.value{font-size:28px;font-weight:650;line-height:1.3;margin:10px 0;color:#086d5d}.card .note{font-size:12px;margin:0}section{padding:28px;margin:20px 0;scroll-margin-top:12px}h2{font-size:23px;line-height:1.3;margin:0 0 12px}h3{font-size:17px;margin:24px 0 8px}p{margin:10px 0}.note,footer{color:#526779}.steps{padding-left:25px}.steps li{padding:2px 0 10px 8px}.steps h3{margin:10px 0 4px}.steps p{margin:4px 0}.banner,.empty{border-left:4px solid #dba448;background:#fff5df;color:#59400d;padding:14px 18px;border-radius:5px}.empty{border-color:#92a9ba;background:#f1f6f9;color:#455d6e}header .banner{color:#523a0b}.wide{min-width:580px}.mobile-hint{display:none;font-size:12px;color:#526779}.table-wrap{overflow-x:auto;margin:18px 0;max-width:100%}table{border-collapse:collapse;width:100%;font-size:14px}caption{text-align:left;font-size:12px;font-weight:bold;color:#526779;padding:0 10px 8px}th,td{padding:12px 10px;text-align:left;border-bottom:1px solid #e1e9ee;vertical-align:top;overflow-wrap:anywhere}thead th{background:#f0f5f7;color:#39556a}tbody th{font-weight:500}footer{font-size:12px;padding:12px 0 24px}header,section,.card,p,li{overflow-wrap:anywhere}@media(max-width:760px){.mobile-hint{display:block}.cards{grid-template-columns:repeat(2,minmax(0,1fr))}main{padding:18px 12px}header,section{padding:22px}h1{font-size:29px}}@media(max-width:420px){.card{padding:14px}.value{font-size:24px}section{padding:18px}th,td{padding:9px 7px}}@page{margin:14mm}@media print{.wide{min-width:0}.mobile-hint{display:none}body{background:white;font-size:10pt}main{max-width:none;padding:0}header{background:white;color:#213547;padding:0;border-radius:0}h1{color:#192f40;font-size:25pt}.eyebrow,.lead{color:#39556a}nav{display:none}.cards{grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}.card{padding:10px}.value{font-size:19pt}section{padding:16px 0;border:0;border-top:1px solid #d9e3ea;border-radius:0}tr,.card,.steps li{break-inside:avoid}h2,h3,caption{break-after:avoid}.table-wrap{overflow:visible}th,td{padding:7px}footer{padding-bottom:0}}";
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
