using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Lean81;

static partial class Tests
{
    sealed class ServiceGraph : IBackend
    {
        public readonly Dictionary<string, ItemState> States = new Dictionary<string, ItemState>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string[]> Edges = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Writes = new List<string>();
        public bool Admin = true; public string MachineScope { get { return "graph-test"; } } public bool IsAdministrator { get { return Admin; } }
        public ServiceGraph(params ItemState[] states) { foreach (var state in states) States[state.Identity()] = state.Copy(); }
        public ItemState Read(ItemState identity)
        {
            var copy = States[identity.Identity()].Copy(); string[] edges;
            if (copy.Kind == "service" && Edges.TryGetValue(copy.Target, out edges)) copy.Dependents = edges.Where(n => States.Values.Any(s => s.Kind == "service" && s.Target == n && s.StartType != 4)).ToArray();
            return copy;
        }
        public ScanReport Scan(UsageProfile profile)
        {
            var report = new ScanReport(); foreach (var state in States.Values)
            {
                var now = Read(state); var item = Item(now); item.Kind = now.Kind; item.Scope = now.Scope; item.Feature = now.Kind == "startup" ? StartupRules.Feature(now.Value) : null;
                try { Policy.Validate(now, profile, false); } catch { item.CanChange = false; } report.Items.Add(item);
            }
            return report;
        }
        public void Preflight(ItemState state, UsageProfile profile, bool restore) { Policy.Validate(Read(state), profile, restore); }
        public void Disable(ItemState state) { Preflight(state, Unused(), false); Writes.Add("disable:" + state.Target); States[state.Identity()] = state.Disabled(); }
        public void Restore(ItemState state) { Writes.Add("restore:" + state.Target); States[state.Identity()] = state.Copy(); }
    }
    static ServiceGraph Graph()
    {
        var graph = new ServiceGraph(Service("HomeGroupListener"), Service("HomeGroupProvider")); graph.Edges["HomeGroupListener"] = new[] { "HomeGroupProvider" }; return graph;
    }
    static ItemState ShortcutState(string name = "Example.lnk")
    {
        string bytes = Convert.ToBase64String(Encoding.UTF8.GetBytes("Synthetic shortcut, never executed"));
        return new ItemState { Kind = "startupfile", Target = name, Scope = "user", Exists = true, Enabled = true, Value = bytes, ValueKind = 3, Definition = Product.Hash(bytes),
            FileCreatedUtc = DateTime.UtcNow.ToString("o"), FileModifiedUtc = DateTime.UtcNow.ToString("o"), FileSecurity = "O:BAG:BAD:P(A;;FA;;;BA)(A;;FA;;;SY)" };
    }
    sealed class FakeApps : IRunningApps
    {
        public int Writes; public IList<RunningApp> Scan() { return new RunningApp[0]; }
        public AppCloseResult Close(RunningApp app, bool force) { Writes++; return new AppCloseResult { Pid = app.Pid, Name = app.Name, Closed = true, Status = "Closed" }; }
    }
    static RunningApp Companion()
    { return new RunningApp { Pid = 500, Started = 123, Path = @"C:\Users\Example\AppData\Local\Discord\app\Discord.exe", Owner = "user", Name = "Discord", Family = "discord", Feature = "chat" }; }
    static void DeepTests()
    {
        Test("maximum reduction disables indexing preloading and unused companions in its answers", () => { var p = Profiles.Preset(4); Assert(!p.IndexedSearch && !p.Prefetch && !p.Printing && !p.Bluetooth && !p.GameLaunchers && !p.CloudSync && !p.ChatApps && !p.AppUpdaters && !p.Telemetry && p.Compatibility && p.Themes); });
        Test("new answers remain kept in older workspaces", () => { var p = Product.Json().Deserialize<UsageProfile>("{\"Printing\":false}"); Assert(p.AppUpdaters && p.ChatApps && p.CloudSync && p.Telemetry && p.Compatibility && p.Themes); });
        Test("all-unused helper covers every current bool answer", () => Assert(typeof(UsageProfile).GetProperties().Where(p => p.PropertyType == typeof(bool)).All(p => !(bool)p.GetValue(Unused(), null))));
        Test("expanded service catalog is unique and excludes protected OS services", () => Assert(Policy.Services.Length == 26 && Policy.Services.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 26 && !Policy.Services.Any(s => new[] { "RpcSs", "WinDefend", "BITS", "wuauserv", "Dhcp", "Dnscache", "WlanSvc", "EventLog", "DcomLaunch" }.Contains(s.Name, StringComparer.OrdinalIgnoreCase))));
        Test("deep tune includes a parent whose dependent is also unused", () => { var b = Graph(); var p = DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false); Assert(p.Items.Count == 2 && p.Items.Any(i => i.Before.Dependents.Length == 1)); });
        Test("deep tune stops dependents before parents in separate recorded passes", () => { var b = Graph(); var j = new MemoryJournals(); var p = DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false); var r = new DeepTuneEngine(b, j).Apply(p, null); Assert(r.Status == "Applied" && r.Applied == 2 && r.Receipts.Count == 2 && b.Writes.SequenceEqual(new[] { "disable:HomeGroupProvider", "disable:HomeGroupListener" }) && j.List().All(x => x.GroupId == r.GroupId)); });
        Test("deep tune group restores providers before dependents", () => { var b = Graph(); var j = new MemoryJournals(); var e = new DeepTuneEngine(b, j); var r = e.Apply(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false), null); b.Writes.Clear(); Assert(e.Restore(r.GroupId, null) == 2 && b.Writes.SequenceEqual(new[] { "restore:HomeGroupListener", "restore:HomeGroupProvider" })); Assert(j.List().All(x => x.Status == "Restored")); });
        Test("deep tune keeps a service needed by an unselected dependent", () => { var b = new ServiceGraph(Service("WSearch"), Service("Spooler")); b.Edges["WSearch"] = new[] { "Spooler" }; var p = Unused(); p.Printing = true; var plan = DeepTunePlan.Build(b.Scan(p), p, false, false, false); Assert(plan.Items.Count == 0 && plan.Kept.Count == 1); });
        Test("deep tune keeps service dependency cycles", () => { var b = Graph(); b.Edges["HomeGroupProvider"] = new[] { "HomeGroupListener" }; Assert(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false).Items.Count == 0); });
        Test("deep tune preflight blocks a newly enabled outside dependent before writes", () => { var b = Graph(); var p = DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false); var extra = Service("Spooler"); b.States[extra.Identity()] = extra; b.Edges["HomeGroupListener"] = new[] { "HomeGroupProvider", "Spooler" }; Throws(() => new DeepTuneEngine(b, new MemoryJournals()).Apply(p, null)); Assert(b.Writes.Count == 0); });
        Test("group recovery conflicts stop the whole group before writes", () => { var b = Graph(); var j = new MemoryJournals(); var e = new DeepTuneEngine(b, j); var r = e.Apply(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false), null); var item = b.States.Values.First(s => s.Target == "HomeGroupProvider"); item.Definition = Product.Hash("independent edit"); b.Writes.Clear(); Throws(() => e.Restore(r.GroupId, null)); Assert(b.Writes.Count == 0); });
        Test("service settings fingerprints do not confuse live dependents with configuration", () => { var a = Service(); var b = a.Copy(); b.Dependents = new[] { "NewDependent" }; Assert(a.Fingerprint() == b.Fingerprint()); Throws(() => Policy.Validate(b, Unused(), false)); });
        Test("deep tune never auto-selects unknown startup programs", () => { var s = Startup("Unknown app"); s.Value = @"C:\Unknown\app.exe"; var b = new ServiceGraph(s); Assert(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, true).Items.Count == 0); });
        Test("deep tune startup selection follows recognized unused categories", () => { var s = Startup("OneDrive"); s.Value = @"C:\Users\Example\Microsoft\OneDrive\OneDrive.exe"; var b = new ServiceGraph(s); Assert(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, true).Items.Count == 1 && DeepTunePlan.Build(b.Scan(new UsageProfile()), new UsageProfile(), false, false, true).Items.Count == 0); });
        Test("deep tune requires a separate power-plan choice", () => { var s = new ItemState { Kind = "power", Scope = "user", Target = "active-power-plan", Exists = true, Enabled = true, Value = PowerPlans.Balanced }; var b = new ServiceGraph(s); Assert(DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, false, false).Items.Count == 0 && DeepTunePlan.Build(b.Scan(Unused()), Unused(), false, true, false).Items.Count == 1); });
        Test("power-plan journal round trips an existing custom GUID", () => { var s = new ItemState { Kind = "power", Scope = "user", Target = "active-power-plan", Exists = true, Enabled = true, Value = Guid.NewGuid().ToString("D") }; var b = new Fake(s); var e = new ChangeEngine(b, new MemoryJournals()); var r = e.Apply(new[] { Item(s) }, Unused(), null); Assert(b.Read(s).Value == PowerPlans.HighPerformance && e.Restore(r.Id, null).Status == "Restored" && b.Read(s).Value == s.Value); });
        Test("power-plan operations reject invalid identifiers and scope", () => { var s = new ItemState { Kind = "power", Scope = "user", Target = "active-power-plan", Exists = true, Enabled = true, Value = "bad" }; Throws(() => Policy.Validate(s, Unused(), false)); s.Value = PowerPlans.Balanced; s.Scope = "machine"; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("startup shortcut state is journaled and recovered exactly", () => { var s = ShortcutState(); var b = new Fake(s); var e = new ChangeEngine(b, new MemoryJournals()); var r = e.Apply(new[] { Item(s) }, Unused(), null); Assert(!b.Read(s).Exists && b.Read(s).Value == null); Assert(e.Restore(r.Id, null).Status == "Restored" && b.Read(s).Fingerprint() == s.Fingerprint()); });
        Test("startup shortcuts reject traversal executable files and invalid backups", () => { foreach (string name in new[] { @"..\Example.lnk", "Example.exe", @"C:\Example.lnk" }) Throws(() => Policy.Validate(ShortcutState(name), Unused(), false)); var s = ShortcutState(); s.Value = "not-base64"; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("startup shortcut security/input targets are kept", () => { var s = ShortcutState(); s.Value = Convert.ToBase64String(Encoding.Unicode.GetBytes(@"C:\Program Files\Windows Defender\MSASCui.exe")); s.Definition = Product.Hash(s.Value); Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("startup shortcut definition mismatches are refused", () => { var s = ShortcutState(); s.Definition = Product.Hash("changed"); Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("startup shortcuts retain timestamps and security in receipt validation", () => { var s = ShortcutState(); var r = new ChangeEngine(new Fake(s), new MemoryJournals()).Apply(new[] { Item(s) }, Unused(), null); ChangeEngine.ValidateReceipt(r, "test-scope"); Assert(r.Entries[0].Original.FileSecurity == s.FileSecurity && r.Entries[0].Expected.FileSecurity == null); });
        Test("ordinary receipts from older versions remain ungrouped", () => { var s = Startup(); var r = new ChangeEngine(new Fake(s), new MemoryJournals()).Apply(new[] { Item(s) }, Unused(), null); Assert(r.GroupId == null && r.GroupSequence == 0); ChangeEngine.ValidateReceipt(r, "test-scope"); });
        Test("invalid group metadata is rejected before any write", () => { var s = Startup(); var b = new Fake(s); Throws(() => new ChangeEngine(b, new MemoryJournals()).Apply(new[] { Item(s) }, Unused(), null, "bad", 1)); Assert(b.Writes == 0); });
        Test("app catalog rejects browsers Windows executables and arbitrary names", () => { foreach (string path in new[] { @"C:\Program Files\Mozilla Firefox\firefox.exe", @"C:\Windows\System32\Discord\Discord.exe", @"C:\Steam\not-steam.exe", @"\\server\Steam\steam.exe", @"C:\Other\Discord.exe" }) Assert(AppCatalog.Match(path) == null); });
        Test("app catalog requires both recognized filename and vendor location", () => { Assert(AppCatalog.Match(@"D:\Games\Steam\steam.exe").Id == "steam" && AppCatalog.Match(@"C:\Users\Example\AppData\Local\Discord\app\Discord.exe").Id == "discord"); });
        Test("app closing refuses foreground system-account and mismatched identities", () => { var a = Companion(); a.Foreground = true; Throws(() => AppCatalog.Validate(a)); a.Foreground = false; a.Owner = "S-1-5-18"; Throws(() => AppCatalog.Validate(a)); a.Owner = "user"; a.Family = "steam"; Throws(() => AppCatalog.Validate(a)); });
        Test("app-closing batch validates every selection before any close", () => { var a = Companion(); var bad = Companion(); bad.Pid = 501; bad.Path = @"C:\Windows\explorer.exe"; var b = new FakeApps(); Throws(() => AppCatalog.CloseSelected(b, new[] { a, bad }, true)); Assert(b.Writes == 0); });
        Test("app closure rejects duplicate PIDs", () => { var a = Companion(); var b = new FakeApps(); Throws(() => AppCatalog.CloseSelected(b, new[] { a, a }, true)); Assert(b.Writes == 0); });
        Test("demo app closure leaves foreground apps untouched", () => { var b = new DemoRunningApps(); var apps = b.Scan(); var target = apps.First(a => !a.Foreground); Assert(AppCatalog.CloseSelected(b, new[] { target }, true).Single().Closed && b.Scan().Any(a => a.Foreground)); });
    }
    static void NativeDeepFixtures()
    {
        var backend = new WindowsBackend(); var store = new JournalStore(backend.MachineScope); string folder = StartupFiles.DirectoryFor("user"); bool existed = Directory.Exists(folder);
        string name = "Lean81Fixture" + Guid.NewGuid().ToString("N") + ".lnk", path = Path.Combine(folder, name);
        string appsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lean81Fixture" + Guid.NewGuid().ToString("N")); Process process = null;
        try
        {
            Directory.CreateDirectory(folder); File.WriteAllBytes(path, Encoding.UTF8.GetBytes("Lean81 disposable shortcut fixture. Never executed."));
            var original = backend.Read(new ItemState { Kind = "startupfile", Scope = "user", Target = name });
            Test("native startup shortcut disable and byte/ACL/time exact recovery", () => { var engine = new ChangeEngine(backend, store); var r = engine.Apply(new[] { Item(original) }, Unused(), null); Assert(r.Status == "Applied", r.Entries[0].Error); Assert(!File.Exists(path)); var restored = engine.Restore(r.Id, null); Assert(restored.Status == "Restored", restored.Entries[0].Error); Assert(backend.Read(original).Fingerprint() == original.Fingerprint()); });
            Test("native power-plan inspection changes no plan", () => { var a = PowerPlans.Read(); var b = PowerPlans.Read(); Assert(a.Value == b.Value && Guid.Parse(a.Value) != Guid.Empty); });
            string appDirectory = Path.Combine(appsRoot, "Discord"); Directory.CreateDirectory(appDirectory);
            string app = Path.Combine(appDirectory, "Discord.exe"); File.Copy(typeof(Tests).Assembly.Location, app);
            process = Process.Start(new ProcessStartInfo(app, "--idle-fixture") { UseShellExecute = false, CreateNoWindow = true });
            RunningApp snapshot = null; var native = new WindowsRunningApps();
            for (int i = 0; i < 30 && snapshot == null; i++) { Thread.Sleep(100); snapshot = native.Scan().FirstOrDefault(a => a.Pid == process.Id); }
            Assert(snapshot != null, "Disposable owned companion fixture was not recognized");
            Test("native stale app identity cannot close the replacement", () => { var stale = new RunningApp { Pid = snapshot.Pid, Started = snapshot.Started + 1, Path = snapshot.Path, Owner = snapshot.Owner, Session = snapshot.Session, Family = snapshot.Family, Name = snapshot.Name }; Throws(() => native.Close(stale, true)); Assert(!process.HasExited); });
            Test("native reviewed app closure ends only the disposable process", () => { var result = native.Close(snapshot, true); Assert(result.Closed, result.Status); Assert(process.WaitForExit(2000)); });
        }
        finally
        {
            if (process != null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(2000); } process.Dispose(); }
            if (Directory.Exists(appsRoot)) Directory.Delete(appsRoot, true);
            if (File.Exists(path)) File.Delete(path);
            if (!existed && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
    }
}
