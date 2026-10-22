using System;
using System.Linq;
using Lean81;

static partial class Tests
{
    static string Report(CaptureSummary current, CaptureSummary baseline, ReportOptions options = null, CaptureSummary[] history = null)
    { return Reports.Build(new DemoBackend().Scan(Unused()), DeviceSnapshot.Demo(), current, baseline, history, Unused(), options ?? new ReportOptions()); }
    static void ReportTests()
    {
        Test("report compares compatible captures using percentage points", () => { var a = Summary(); var b = Summary(); b.AverageCpu -= 5; var html = Report(b, a); Assert(html.Contains("-5.0 pp") && html.Contains("Observed differences, not proof") && html.Contains("Matching recorded conditions")); });
        Test("report refuses mismatched comparisons without showing zero deltas", () => { var a = Summary(); var b = Summary(); b.Power = "battery"; var html = Report(b, a); Assert(html.Contains("Comparison unavailable") && html.Contains("same known power source") && !html.Contains("<td>0.0 pp")); });
        Test("report does not compare a capture with itself", () => { var a = Summary(); var html = Report(a, a); Assert(html.Contains("Choose a different result") && !html.Contains("Observed differences, not proof")); });
        Test("report distinguishes missing captures and missing device snapshots", () => { var html = Reports.Build(new DemoBackend().Scan(Unused()), null, null, false, false); Assert(html.Contains("No CPU result") && html.Contains("Device snapshot unavailable") && !html.Contains("0.0%")); });
        Test("report explains incomplete CPU coverage", () => { var a = Summary(); a.ValidCpuSamples--; a.Complete = false; var html = Report(a, Summary()); Assert(html.Contains("Partial evidence") && html.Contains("Interrupted or error") && html.Contains("Comparison unavailable")); });
        Test("report cannot leak workload labels or stored identities through history", () => { var a = Summary(); a.Workload = "private-workload-123"; var html = Report(a, null, new ReportOptions { IncludeHistory = true }, new[] { a }); Assert(html.Contains("Result 1") && !html.Contains(a.Workload) && !html.Contains(a.Id) && !html.Contains(a.MachineKey) && !html.Contains(a.HardwareKey)); });
        Test("report history is explicitly opt-in", () => { var a = Summary(); a.CreatedUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToString("o"); Assert(!Report(null, null, new ReportOptions(), new[] { a }).Contains("2020-01-01")); });
        Test("report history limit refuses excess rows", () => Throws(() => Report(null, null, new ReportOptions { IncludeHistory = true }, Enumerable.Range(0, 13).Select(i => Summary()).ToArray())));
        Test("report validates saved history before rendering", () => { var a = Summary(); a.AverageCpu = Double.NaN; Throws(() => Report(null, null, new ReportOptions { IncludeHistory = true }, new[] { a })); });
        Test("synthetic captures always label the report even without demo option", () => Assert(Report(Summary(), null).Contains("SYNTHETIC DEMO")));
        Test("report preserves unknown memory in comparison", () => { var a = Summary(); a.AverageAvailableMemory = null; Assert(Report(Summary(), a).Contains("Difference unavailable: a capture has no memory reading")); });
        Test("report presents recommendations and partial scan notes", () => { var scan = new DemoBackend().Scan(Unused()); scan.Notes.Add("private-error-content"); var html = Reports.Build(scan, DeviceSnapshot.Demo(), null, false, true); Assert(html.Contains("Your next useful steps") && html.Contains("Some scan observations need review") && !html.Contains("private-error-content")); });
        Test("report encodes device details and has no executable or remote content", () => { var d = DeviceSnapshot.Demo(); d.Cpu = "<img src=x onerror=alert(1)>"; var html = Reports.Build(new DemoBackend().Scan(Unused()), d, null, false, true); Assert(html.Contains("&lt;img") && !html.Contains("<img") && !html.Contains("<script") && !html.Contains("https://") && html.Contains("Content-Security-Policy")); });
        Test("report distinguishes unavailable fixed-drive observations", () => { var d = DeviceSnapshot.Demo(); d.Disks.Clear(); Assert(Reports.Build(new DemoBackend().Scan(Unused()), d, null, false, true).Contains("does not establish that no fixed drives exist")); });
    }
}
