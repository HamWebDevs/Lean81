using System;
using System.IO;
using System.Linq;
using Lean81;

static partial class Tests
{
    static CaptureSummary Summary()
    { return CaptureSummary.From(CaptureResult.Demo(), "Browsing", Product.Hash("fixture-machine")); }
    static CaptureSummary CloneSummary(CaptureSummary value)
    { return Product.Json().Deserialize<CaptureSummary>(Product.Json().Serialize(value)); }
    static void WorkspaceFixture(Action<WorkspaceStore, string> test)
    {
        var path = Path.Combine(Path.GetTempPath(), "Lean81SettingsFixture" + Guid.NewGuid().ToString("N"));
        try { Directory.CreateDirectory(path); test(new WorkspaceStore(path), path); } finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
    static void FeatureTests()
    {
        Test("keep-all preset retains optional features", () => Assert(FeatureFlags(Profiles.Preset(0)).All(x => x)));
        Test("sharing preset retains printing, search and preloading", () => { var p = Profiles.Preset(1); Assert(p.Printing && p.IndexedSearch && p.Prefetch && p.Bluetooth && p.TouchKeyboard && !p.HomeGroup && !p.MediaSharing && !p.OfflineFiles); });
        Test("wireless-extras preset requires explicit Bluetooth and touch review", () => { var p = Profiles.Preset(2); Assert(!p.Bluetooth && !p.TouchKeyboard && p.Printing && p.IndexedSearch && p.Prefetch); });
        Test("unknown preset is refused", () => Throws(() => Profiles.Preset(99)));
        Test("profile copying does not alias source", () => { var a = Profiles.Preset(2); var b = new UsageProfile(); Profiles.Copy(a, b); b.Printing = false; Assert(a.Printing && !b.Printing && !b.Bluetooth); });
        Test("service reason explains kept feature", () => Assert(Profiles.FeatureReason(Item(Service()), new UsageProfile()).Contains("usage answers")));
        Test("service reason explains dependencies", () => { var s = Service(); s.Dependents = new[] { "fixture" }; Assert(Profiles.FeatureReason(Item(s), Unused()).Contains("another enabled service")); });
        Test("service reason explains explicit unused choice", () => Assert(Profiles.FeatureReason(Item(Service()), Unused()).Contains("marked this feature unused")));
        Test("storage low-space threshold handles small disks", () => Assert(new DiskSnapshot { TotalBytes = 40L << 30, FreeBytes = 4L << 30 }.LowSpace));
        Test("storage percentage threshold handles large disks", () => Assert(new DiskSnapshot { TotalBytes = 500L << 30, FreeBytes = 40L << 30 }.LowSpace));
        Test("healthy capacity is not marked low", () => Assert(!new DiskSnapshot { TotalBytes = 120L << 30, FreeBytes = 30L << 30 }.LowSpace));
        Test("missing capacity is not given a false score", () => Assert(!new DiskSnapshot().LowSpace && DeviceSnapshot.Size(0) == "Unavailable"));
        Test("recommendations route missing inventory to scan", () => Assert(Recommendations.Build(null, null, null, new UsageProfile()).Single().Page == 0));
        Test("recommendations only count services matching unused answers", () => { var d = new DemoBackend(); var kept = Recommendations.Build(d.Scan(new UsageProfile()), DeviceSnapshot.Demo(), null, new UsageProfile()); Assert(!kept.Any(r => r.Title.Contains("optional services match"))); var unused = Recommendations.Build(d.Scan(Unused()), DeviceSnapshot.Demo(), null, Unused()); Assert(unused.Any(r => r.Page == 4) && unused.Any(r => r.Page == 8)); });
        Test("CPU summaries weight variable sample intervals", () =>
        {
            var c = CaptureResult.Demo(); c.Samples = c.Samples.Take(2).ToList(); c.Samples[0].dt = 2; c.Samples[0].metrics["cpu_pct"] = 20.0; c.Samples[1].dt = 8; c.Samples[1].metrics["cpu_pct"] = 80.0; c.Duration = 10;
            var r = CaptureSummary.From(c, "Browsing", Product.Hash("machine")); Assert(Math.Abs(r.AverageCpu - 68) < 0.0001 && r.BusySeconds == 8 && r.ObservedSeconds == 10);
        });
        Test("missing CPU values remain visible in summary coverage", () => { var c = CaptureResult.Demo(); c.Samples[0].metrics["cpu_pct"] = null; var s = CaptureSummary.From(c, "Browsing", Product.Hash("machine")); Assert(s.ValidCpuSamples == 14 && s.ObservedSeconds == 28); });
        Test("no valid CPU observations cannot create a result", () => { var c = CaptureResult.Demo(); foreach (var sample in c.Samples) sample.metrics["cpu_pct"] = null; Throws(() => CaptureSummary.From(c, "Browsing", Product.Hash("machine"))); });
        Test("invalid sample intervals are refused", () => { var c = CaptureResult.Demo(); c.Samples[0].dt = Double.NaN; Throws(() => CaptureSummary.From(c, "Browsing", Product.Hash("machine"))); });
        Test("power transitions are marked mixed", () => { var c = CaptureResult.Demo(); c.Samples[0].metrics["power"] = "battery"; Assert(CaptureSummary.From(c, "Browsing", Product.Hash("machine")).Power == "mixed"); });
        Test("blank workload gets a stable default", () => Assert(CaptureSummary.From(CaptureResult.Demo(), "   ", Product.Hash("machine")).Workload == "Normal laptop use"));
        Test("summary rejects nonfinite CPU", () => { var s = Summary(); s.AverageCpu = Double.PositiveInfinity; Throws(() => CaptureSummary.Validate(s)); });
        Test("summary rejects impossible peak", () => { var s = Summary(); s.PeakCpu = s.AverageCpu - 1; Throws(() => CaptureSummary.Validate(s)); });
        Test("summary rejects short ambiguous date", () => { var s = Summary(); s.CreatedUtc = "2026-01-01"; Throws(() => CaptureSummary.Validate(s)); });
        Test("summary rejects invalid hash identities", () => { var s = Summary(); s.MachineKey = "../private"; Throws(() => CaptureSummary.Validate(s)); });
        Test("comparison reports absolute percentage points", () => { var a = Summary(); var b = CloneSummary(a); b.Id = Guid.NewGuid().ToString("N"); b.AverageCpu -= 5; b.PeakCpu -= 10; var r = CaptureComparison.Compare(a, b); Assert(r.Comparable && Math.Abs(r.CpuDifference + 5) < 0.0001 && r.PeakDifference == -10 && r.Text().Contains("percentage points")); });
        Test("comparison refuses same result", () => { var s = Summary(); Assert(!CaptureComparison.Compare(s, s).Comparable); });
        Test("comparison refuses different machines", () => { var a = Summary(); var b = Summary(); b.MachineKey = Product.Hash("elsewhere"); Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses different hardware", () => { var a = Summary(); var b = Summary(); b.HardwareKey = Product.Hash("changed cpu"); Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses synthetic versus real", () => { var a = Summary(); var b = Summary(); b.Synthetic = false; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses different workloads", () => { var a = Summary(); var b = Summary(); b.Workload = "Gaming"; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison permits case and whitespace label differences", () => { var a = Summary(); var b = Summary(); b.Workload = " browsing "; Assert(CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses changed power source", () => { var a = Summary(); var b = Summary(); b.Power = "battery"; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses mixed power source", () => { var a = Summary(); var b = Summary(); a.Power = b.Power = "mixed"; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses unknown power source", () => { var a = Summary(); var b = Summary(); a.Power = b.Power = "unknown"; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses partial captures", () => { var a = Summary(); var b = Summary(); b.Complete = false; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses missing CPU samples", () => { var a = Summary(); var b = Summary(); b.ValidCpuSamples--; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses dissimilar durations", () => { var a = Summary(); var b = Summary(); b.Duration = b.ObservedSeconds = 60; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("comparison refuses tiny captures", () => { var a = Summary(); var b = Summary(); a.Duration = a.ObservedSeconds = b.Duration = b.ObservedSeconds = 10; Assert(!CaptureComparison.Compare(a, b).Comparable); });
        Test("saved history is capped while retaining baseline", () => { var w = new UserWorkspace(); var baseline = Summary(); w.Add(baseline); w.BaselineId = baseline.Id; for (int i = 0; i < 20; i++) w.Add(Summary()); Assert(w.History.Count == 12 && w.History.Any(s => s.Id == baseline.Id)); UserWorkspace.Validate(w); });
        Test("duplicate saved identities are refused", () => { var w = new UserWorkspace(); var s = Summary(); w.History.Add(s); w.History.Add(s); Throws(() => UserWorkspace.Validate(w)); });
        Test("missing baseline identity is refused", () => { var w = new UserWorkspace { BaselineId = Guid.NewGuid().ToString("N") }; Throws(() => UserWorkspace.Validate(w)); });
        Test("missing preferences keep every feature", () => WorkspaceFixture((store, path) => Assert(FeatureFlags(store.Load().Profile).All(x => x))));
        Test("preferences round-trip usage, baseline and aggregate history", () => WorkspaceFixture((store, path) => { var w = new UserWorkspace { Profile = Profiles.Preset(1) }; w.Add(Summary()); w.BaselineId = w.History[0].Id; store.Save(w); var read = store.Load(); Assert(!read.Profile.HomeGroup && read.Profile.Printing && read.BaselineId == w.BaselineId && read.History.Count == 1); var json = File.ReadAllText(Path.Combine(path, "workspace.json")); Assert(!json.Contains("ExampleApp") && !json.Contains("rss_bytes") && !json.Contains("pid")); }));
        Test("atomic preferences update replaces previous content", () => WorkspaceFixture((store, path) => { store.Save(new UserWorkspace()); var updated = new UserWorkspace { Workload = "Second run" }; store.Save(updated); Assert(store.Load().Workload == "Second run" && Directory.GetFiles(path).Length == 1); }));
        Test("malformed preferences return safe defaults with warning", () => WorkspaceFixture((store, path) => { File.WriteAllText(Path.Combine(path, "workspace.json"), "{bad json"); var read = store.Load(); Assert(read.Profile.Prefetch && read.Profile.Printing && store.Warning != null); }));
        Test("oversized settings are bounded", () => WorkspaceFixture((store, path) => { File.WriteAllText(Path.Combine(path, "workspace.json"), new string(' ', 128 * 1024 + 1)); Assert(store.Load().History.Count == 0 && store.Warning != null); }));
        Test("invalid preference save preserves existing file", () => WorkspaceFixture((store, path) => { store.Save(new UserWorkspace()); var original = File.ReadAllText(Path.Combine(path, "workspace.json")); var w = new UserWorkspace(); var s = Summary(); s.AverageCpu = Double.NaN; w.History.Add(s); Throws(() => store.Save(w)); Assert(File.ReadAllText(Path.Combine(path, "workspace.json")) == original); }));
        Test("workspace schema mismatch does not import preferences", () => WorkspaceFixture((store, path) => { File.WriteAllText(Path.Combine(path, "workspace.json"), "{\"Schema\":999,\"Profile\":{\"Printing\":false}}"); Assert(store.Load().Profile.Printing && store.Warning != null); }));
        Test("CSV export omits process names by default", () => { var path = Path.GetTempFileName(); try { CaptureResult.Demo().ExportCsv(path, false); var text = File.ReadAllText(path); Assert(!text.Contains("ExampleApp") && text.Contains("redacted") && File.ReadAllLines(path).Length == 16); } finally { File.Delete(path); } });
        Test("CSV opt-in escapes spreadsheet formulas and quotes", () => { var path = Path.GetTempFileName(); try { var c = CaptureResult.Demo(); c.Samples[0].processes[0].name = "=BAD(\"x\")"; c.ExportCsv(path, true); Assert(File.ReadAllText(path).Contains("\"'=BAD(\"\"x\"\")\"")); } finally { File.Delete(path); } });
        Test("HTML report excludes private fields even when names are requested", () => { var scan = new DemoBackend().Scan(Unused()); scan.Items[0].Before.Value = @"C:\Users\PrivateName\secret.exe token=secret"; scan.Notes.Add(@"Access denied C:\Users\PrivateName\"); var html = Reports.Build(scan, DeviceSnapshot.Demo(), Summary(), true, true); Assert(!html.Contains("PrivateName") && !html.Contains("token=secret") && !html.Contains("Definition") && html.Contains("SYNTHETIC DEMO")); });
        Test("HTML report hides software names without opt-in", () => { var scan = new DemoBackend().Scan(Unused()); scan.Items[0].Name = "PrivateAppName"; var html = Reports.Build(scan, null, null, false, false); Assert(!html.Contains("PrivateAppName") && !html.Contains("Cloud sync companion")); });
        Test("HTML software names are encoded rather than executable markup", () => { var scan = new DemoBackend().Scan(Unused()); scan.Items[0].Name = "<script>alert(1)</script>"; var html = Reports.Build(scan, null, null, true, false); Assert(!html.Contains("<script>") && html.Contains("&lt;script&gt;")); });
        Test("report cannot claim a scan exists when absent", () => Throws(() => Reports.Build(null, null, null, false, false)));
        Test("recovery state assessment performs no system writes", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var receipt = e.Apply(new[] { Item(s) }, Unused(), null); int before = b.Writes; var check = e.Inspect(receipt.Id).Single(); Assert(check.CanRestore && check.Status.Contains("disabled") && b.Writes == before); });
        Test("recovery assessment distinguishes independent edits", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var receipt = e.Apply(new[] { Item(s) }, Unused(), null); b.States[s.Identity()] = s.Copy(); b.States[s.Identity()].Value = "externally changed"; Assert(!e.Inspect(receipt.Id).Single().CanRestore && b.Writes == 1); });
        Test("nonadmin recovery assessment is refused", () => { var b = new Fake(Startup()) { Admin = false }; Throws(() => new ChangeEngine(b, new MemoryJournals()).Inspect(Guid.NewGuid().ToString("N"))); Assert(b.Writes == 0); });
        Test("tool catalog only contains fixed Windows system commands", () => Assert(WindowsTools.All.Select(t => t.Id).Distinct().Count() == WindowsTools.All.Length && WindowsTools.All.All(t => !t.File.Contains("\\") && !t.File.Contains("/") && !t.Args.Contains("--") && (t.Args.Length == 0 || t.Args.EndsWith(".cpl", StringComparison.Ordinal)))));
    }
    static bool[] FeatureFlags(UsageProfile p) { return new[] { p.Printing, p.Bluetooth, p.IndexedSearch, p.TouchKeyboard, p.HomeGroup, p.MediaSharing, p.OfflineFiles, p.Prefetch }; }
}
