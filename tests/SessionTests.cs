using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Lean81;

static partial class Tests
{
    sealed class SessionApps : IRunningApps
    {
        public readonly List<RunningApp> Current = new List<RunningApp>();
        public int Writes, Scans; public Action OnClose; public bool FailScan;
        public IList<RunningApp> Scan() { Scans++; if (FailScan) throw new Exception("fixture unavailable"); return Current.Select(TuneSessionEngine.Copy).ToList(); }
        public AppCloseResult Close(RunningApp app, bool force) { Writes++; if (OnClose != null) OnClose(); return new AppCloseResult { Pid = app.Pid, Name = app.Name, Closed = true, Status = force ? "Force closed" : "Closed" }; }
    }
    static TuneSessionRequest SessionRequest(ScanItem item, params RunningApp[] apps)
    { var plan = new DeepTunePlan { Profile = Unused() }; if (item != null) plan.Items.Add(item); return new TuneSessionRequest { Settings = plan, Apps = apps.ToList(), Measure = false }; }
    static CaptureResult Window(double cpu, double memory, double disk)
    {
        var r = new CaptureResult();
        for (int i = 0; i < 4; i++) r.Samples.Add(new CaptureSample { dt = 2, metrics = new Dictionary<string, object> { { "cpu_pct", cpu }, { "memory_available_bytes", memory * 1048576 }, { "disk_busy_pct", disk }, { "commit_pct", 50.0 }, { "power", "ac" } }, processes = new List<ProcessReading>() });
        foreach (var sample in r.Samples) sample.metrics["memory_available_pct"] = memory / 4096 * 100;
        return r;
    }
    static void SessionTests()
    {
        Test("combined tune applies settings before app closure and samples after both", () => {
            var state = Service(); var b = new Fake(state); var a = Companion(); var apps = new SessionApps(); apps.Current.Add(a); var events = new List<string>();
            b.OnWrite = () => events.Add("setting"); apps.OnClose = () => events.Add("app");
            var request = SessionRequest(Item(state), a); request.Measure = true;
            var result = new TuneSessionEngine(b, new MemoryJournals(), apps, t => { events.Add("reading"); return Window(30, 1000, 40); }, t => events.Add("settle")).Run(request, CancellationToken.None, null);
            Assert(result.Status == "Applied" && result.Settings.Applied == 1 && result.Apps.Single().Closed && events.SequenceEqual(new[] { "reading", "setting", "app", "settle", "reading" }));
        });
        Test("a failed settings pass keeps every selected app running", () => {
            var s = Service(); var b = new Fake(s) { DisableFails = true }; var a = Companion(); var apps = new SessionApps(); apps.Current.Add(a); var j = new MemoryJournals();
            var result = new TuneSessionEngine(b, j, apps, t => null, t => { }).Run(SessionRequest(Item(s), a), CancellationToken.None, null);
            Assert(result.Status == "Needs review" && apps.Writes == 0 && j.List().Single().Status == "Needs review" && result.Apps.Single().Status.StartsWith("Skipped:", StringComparison.Ordinal));
        });
        Test("stale settings do not cause unrelated app closure", () => {
            var s = Service(); var b = new Fake(s); var a = Companion(); var apps = new SessionApps(); apps.Current.Add(a); var request = SessionRequest(Item(s), a); b.States[s.Identity()].Definition = Product.Hash("independent");
            var result = new TuneSessionEngine(b, new MemoryJournals(), apps, t => null, t => { }).Run(request, CancellationToken.None, null);
            Assert(result.Status == "Needs review" && result.SettingsStarted && result.Summary().Contains("settings need review") && b.Writes == 0 && apps.Writes == 0);
        });
        Test("invalid app batch is rejected before settings or observation", () => {
            var s = Service(); var b = new Fake(s); var a = Companion(); a.Path = @"C:\Windows\explorer.exe"; var apps = new SessionApps();
            Throws(() => new TuneSessionEngine(b, new MemoryJournals(), apps, t => { throw new Exception("must not sample"); }, t => { }).Run(SessionRequest(Item(s), a), CancellationToken.None, null)); Assert(b.Writes == 0 && apps.Scans == 0);
        });
        Test("app list failure stops a combined tune before settings writes", () => {
            var s = Service(); var b = new Fake(s); var apps = new SessionApps { FailScan = true };
            var result = new TuneSessionEngine(b, new MemoryJournals(), apps, t => null, t => { }).Run(SessionRequest(Item(s), Companion()), CancellationToken.None, null); Assert(result.Status == "Needs review" && b.Writes == 0);
        });
        Test("changed process creation identity is kept rather than closed", () => {
            var app = Companion(); var apps = new SessionApps(); var now = TuneSessionEngine.Copy(app); now.Started++; apps.Current.Add(now);
            var result = new TuneSessionEngine(new Fake(), new MemoryJournals(), apps, t => null, t => { }).Run(SessionRequest(null, app), CancellationToken.None, null); Assert(apps.Writes == 0 && !result.Apps.Single().Closed);
        });
        Test("an app becoming foreground before tune is kept", () => {
            var app = Companion(); var apps = new SessionApps(); var now = TuneSessionEngine.Copy(app); now.Foreground = true; apps.Current.Add(now);
            var result = new TuneSessionEngine(new Fake(), new MemoryJournals(), apps, t => null, t => { }).Run(SessionRequest(null, app), CancellationToken.None, null); Assert(apps.Writes == 0 && !result.Apps.Single().Closed);
        });
        Test("apps-only tune needs neither settings nor administrator rights", () => {
            var app = Companion(); var apps = new SessionApps(); apps.Current.Add(app);
            var result = new TuneSessionEngine(new Fake { Admin = false }, new MemoryJournals(), apps, t => null, t => { }).Run(SessionRequest(null, app), CancellationToken.None, null); Assert(result.Status == "Applied" && result.Settings == null && result.Apps.Single().Closed);
        });
        Test("turning off observations makes no observer calls", () => {
            var app = Companion(); var apps = new SessionApps(); apps.Current.Add(app);
            var result = new TuneSessionEngine(new Fake(), new MemoryJournals(), apps, t => { throw new Exception("must not sample"); }, t => { throw new Exception("must not wait"); }).Run(SessionRequest(null, app), CancellationToken.None, null); Assert(result.Status == "Applied" && result.Before == null && result.After == null);
        });
        Test("unavailable readings do not prevent authorized tuning", () => {
            var s = Service(); var b = new Fake(s); var request = SessionRequest(Item(s)); request.Measure = true;
            var result = new TuneSessionEngine(b, new MemoryJournals(), new SessionApps(), t => { throw new Exception("counter unavailable"); }, t => { }).Run(request, CancellationToken.None, null); Assert(result.Status == "Applied" && b.Writes == 1 && result.Notes.Count == 2 && result.Before == null);
        });
        Test("cancelling before a tune produces no writes", () => {
            var s = Service(); var b = new Fake(s); using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); var result = new TuneSessionEngine(b, new MemoryJournals(), new SessionApps(), t => null, t => { }).Run(SessionRequest(Item(s)), cancel.Token, null); Assert(result.Status == "Stopped before changes" && b.Writes == 0); }
        });
        Test("cancellation after settings keeps receipt and skips app closure", () => {
            var s = Service(); var b = new Fake(s); var a = Companion(); var apps = new SessionApps(); apps.Current.Add(a); var j = new MemoryJournals();
            using (var cancel = new CancellationTokenSource()) { b.OnWrite = () => cancel.Cancel(); var result = new TuneSessionEngine(b, j, apps, t => null, t => { }).Run(SessionRequest(Item(s), a), cancel.Token, null); Assert(result.Status == "Stopped after partial work" && result.Settings.Applied == 1 && apps.Writes == 0 && j.List().Single().Status == "Applied"); }
        });
        Test("suggested app closure follows unused features and fixed catalog", () => {
            var a = Companion(); var kept = TuneSessionEngine.Copy(a); kept.Pid++; kept.Foreground = true;
            Assert(TuneSessionEngine.SuggestApps(new[] { a, kept }, Unused()).Count == 1 && TuneSessionEngine.SuggestApps(new[] { a }, new UsageProfile()).Count == 0); var copy = TuneSessionEngine.SuggestApps(new[] { a }, Unused()).Single(); copy.Path = "changed"; Assert(a.Path != copy.Path);
        });
        Test("resource differences report points and measured MiB rather than scores", () => {
            var rows = TuneReadings.Compare(Window(75, 700, 95), Window(25, 1000, 10)); Assert(rows[0].Difference == "50.0 points lower" && rows[1].Difference == "300.0 MiB higher" && rows[2].Difference == "85.0 points lower");
        });
        Test("resource comparisons require sufficient complete readings", () => {
            var before = Window(75, 700, 95); var after = Window(25, 1000, 10); after.Samples.RemoveRange(1, 3); Assert(TuneReadings.Compare(before, after).All(r => r.Difference == "Insufficient readings"));
        });
        Test("partial failed and mostly missing observations never imply zero load", () => {
            foreach (string mode in new[] { "partial", "error", "missing", "nan" }) { var after = Window(25, 1000, 10); if (mode == "partial") after.Interrupted = true; if (mode == "error") after.Error = "fixture"; if (mode == "missing") after.Samples[0].metrics["disk_busy_pct"] = null; if (mode == "nan") after.Samples[0].metrics["disk_busy_pct"] = Double.NaN; var row = TuneReadings.Compare(Window(75, 700, 95), after)[2]; Assert(row.After == "Unavailable" && row.Difference == "Insufficient readings"); }
        });
        Test("resource comparison refuses changed or unknown power context", () => {
            var b = Window(30, 700, 10); foreach (string mode in new[] { "battery", "unknown" }) { var a = Window(30, 700, 10); a.Samples[0].metrics["power"] = mode; Assert(TuneReadings.Compare(b, a).All(r => r.Difference == "Power changed / unknown")); }
        });
        Test("real and synthetic observations are never compared", () => { var a = Window(30, 700, 10); a.Synthetic = true; Assert(TuneReadings.Compare(Window(30, 700, 10), a).All(r => r.Difference == "Demo / real readings differ")); });
        Test("after-check findings identify remaining pressure inside the app", () => { var text = TuneReadings.NextSteps(Window(95, 100, 95)); Assert(text.Contains("CPU remains busy") && text.Contains("Available RAM remains low") && text.Contains("disk activity remains high") && text.Contains("network/device")); });
        Test("quiet short observations never claim measured acceleration", () => { var text = TuneReadings.NextSteps(Window(20, 1200, 10)); Assert(text.Contains("does not establish a speed increase")); });
        Test("missing after-check data remains unknown", () => { var a = Window(20, 1200, 10); a.Interrupted = true; Assert(TuneReadings.NextSteps(a).Contains("unknown") && !TuneReadings.NextSteps(a).Contains("No sustained")); });
        Test("null or invalid intervals cannot produce a resource delta", () => { var b = Window(20, 1200, 10); b.Samples[0] = null; Assert(TuneReadings.Compare(Window(20, 1200, 10), b).All(r => r.After == "Unavailable")); });
        Test("already disabled but running optional service is still actionable", () => { var s = Service(); s.StartType = 4; s.Enabled = s.Delayed = false; var b = new ServiceGraph(s); var p = DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false); Assert(p.Items.Count == 1); var j = new MemoryJournals(); var e = new DeepTuneEngine(b, j); var result = e.Apply(p, null); Assert(result.Applied == 1 && !b.Read(s).Running); Assert(e.Restore(result.GroupId, null) == 1 && b.Read(s).Running && b.Read(s).StartType == 4); });
        Test("disabled stopped optional services are not offered again", () => { var s = Service(); s.StartType = 4; s.Enabled = s.Running = s.Delayed = false; var b = new ServiceGraph(s); Assert(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false).Items.Count == 0); });
        Test("a disabled running dependent is stopped before its provider", () => { var b = Graph(); var s = b.States.Values.Single(i => i.Target == "HomeGroupProvider"); s.StartType = 4; s.Enabled = s.Delayed = false; var e = new DeepTuneEngine(b, new MemoryJournals()); var r = e.Apply(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false), null); Assert(r.Applied == 2 && b.Writes[0] == "disable:HomeGroupProvider"); });
        Test("an unselected disabled running dependent still protects its provider", () => { var parent = Service("WSearch"); var child = Service("Spooler"); child.StartType = 4; child.Enabled = child.Delayed = false; var b = new ServiceGraph(parent, child); b.Edges["WSearch"] = new[] { "Spooler" }; var profile = Unused(); profile.Printing = true; Assert(DeepTunePlan.Build(b.Scan(profile), profile, false, false, false).Items.Count == 0); });
    }
}
