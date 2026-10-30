using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Lean81;
using Microsoft.Win32;

static partial class Tests
{
    static int passed, failed;
    static void Assert(bool yes, string message = "assertion failed") { if (!yes) throw new Exception(message); }
    static void Throws(Action action) { bool threw = false; try { action(); } catch { threw = true; } Assert(threw, "expected refusal"); }
    static void Test(string name, Action action) { try { action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + ": " + ex); } }
    static ItemState Startup(string name = "Example updater") { return new ItemState { Kind = "startup", Scope = "user", Target = name, Exists = true, Enabled = true, Value = @"%LOCALAPPDATA%\Example\app.exe --tray", ValueKind = 2 }; }
    static ItemState Service(string name = "WSearch") { return new ItemState { Kind = "service", Scope = "machine", Target = name, Exists = true, Enabled = true, StartType = 2, Running = true, Delayed = true, Definition = Product.Hash(name) }; }
    static ItemState TaskState(string path = @"\Example\Updater") { return new ItemState { Kind = "task", Scope = "machine", Target = path, Exists = true, Enabled = true, Definition = Product.Hash(path) }; }
    static UsageProfile Unused() { return Profiles.AllUnused(); }
    static ScanItem Item(ItemState state) { return new ScanItem { Id = Product.Hash(state.Identity()), Before = state.Copy(), Name = state.Target, CanChange = true }; }
    sealed class Fake : IBackend
    {
        public bool Admin = true, PreflightFails, DisableFails, WrongResult;
        public int Writes, Reads; public Action OnWrite, OnRead;
        public Dictionary<string, ItemState> States = new Dictionary<string, ItemState>();
        public string MachineScope { get { return "test-scope"; } }
        public bool IsAdministrator { get { return Admin; } }
        public Fake(params ItemState[] states) { foreach (var s in states) States[s.Identity()] = s.Copy(); }
        public ItemState Read(ItemState s) { Reads++; if (OnRead != null) OnRead(); return States[s.Identity()].Copy(); }
        public void Preflight(ItemState s, UsageProfile p, bool r) { Policy.Validate(s, p, r); if (PreflightFails) throw new Exception("fixture preflight failure"); }
        public void Disable(ItemState s) { Writes++; if (OnWrite != null) OnWrite(); if (DisableFails) throw new Exception("fixture write failure"); if (!WrongResult) States[s.Identity()] = s.Disabled(); }
        public void Restore(ItemState s) { Writes++; if (OnWrite != null) OnWrite(); States[s.Identity()] = s.Copy(); }
        public ScanReport Scan(UsageProfile p) { return new ScanReport(); }
    }
    sealed class FailingJournals : IJournalStore
    {
        public readonly MemoryJournals Inner = new MemoryJournals();
        public int Saves, FailAt;
        public IDisposable Lock() { return Inner.Lock(); }
        public void Save(ChangeReceipt r, bool create) { if (++Saves >= FailAt && FailAt > 0) throw new IOException("fixture disk failure"); Inner.Save(r, create); }
        public ChangeReceipt Load(string id) { return Inner.Load(id); }
        public IList<ChangeReceipt> List() { return Inner.List(); }
    }
    static void MainCore()
    {
        Test("defaults keep every optional feature", () => Assert(new[] { "printing", "bluetooth", "search", "touch", "homegroup", "media", "offline", "prefetch", "unknown" }.All(new UsageProfile().Uses)));
        Test("unknown features remain kept", () => Assert(Unused().Uses("unknown")));
        Test("optional service requires explicit unused feature", () => Throws(() => Policy.Validate(Service(), new UsageProfile(), false)));
        Test("unused service allowed", () => Policy.Validate(Service(), Unused(), false));
        Test("core service blocked", () => Throws(() => Policy.Validate(Service("RpcSs"), Unused(), false)));
        Test("network service blocked", () => Throws(() => Policy.Validate(Service("Dhcp"), Unused(), false)));
        Test("security service blocked", () => Throws(() => Policy.Validate(Service("WinDefend"), Unused(), false)));
        Test("dependent service prevents disable", () => { var s = Service(); s.Dependents = new[] { "DependentApp" }; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("invalid service startup type blocked", () => { var s = Service(); s.StartType = 0; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("service needs configuration identity", () => { var s = Service(); s.Definition = null; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("service undo preserves delayed automatic and running", () => { var s = Service(); var d = s.Disabled(); Assert(s.Delayed && s.Running && s.StartType == 2); Assert(!d.Delayed && !d.Running && d.StartType == 4 && !d.Enabled); });
        Test("bounded expand-string startup allowed", () => Policy.Validate(Startup(), new UsageProfile(), false));
        Test("security startup kept", () => Throws(() => Policy.Validate(Startup("Windows Defender"), Unused(), false)));
        Test("input startup kept", () => Throws(() => Policy.Validate(Startup("SynTPEnh touchpad"), Unused(), false)));
        Test("runonce outside policy", () => { var s = Startup(); s.Scope = "runonce"; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("startup path traversal blocked", () => Throws(() => Policy.Validate(Startup(@"Bad\Key"), Unused(), false)));
        Test("nonstring registry data blocked", () => { var s = Startup(); s.ValueKind = 3; Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("oversized startup value blocked", () => { var s = Startup(); s.Value = new string('a', 8193); Throws(() => Policy.Validate(s, Unused(), false)); });
        Test("startup disable removes value and kind", () => { var d = Startup().Disabled(); Assert(!d.Exists && !d.Enabled && d.Value == null && d.ValueKind == 0); });
        Test("third party task allowed", () => Policy.Validate(TaskState(), Unused(), false));
        Test("Microsoft task protected", () => Throws(() => Policy.Validate(TaskState(@"\Microsoft\Windows\Defrag"), Unused(), false)));
        Test("security task protected", () => Throws(() => Policy.Validate(TaskState(@"\Example\Antivirus"), Unused(), false)));
        Test("relative task path blocked", () => Throws(() => Policy.Validate(TaskState("Updater"), Unused(), false)));
        Test("task traversal blocked", () => Throws(() => Policy.Validate(TaskState(@"\Example\..\Updater"), Unused(), false)));
        Test("task disable preserves identity", () => { var s = TaskState(); var d = s.Disabled(); Assert(d.Definition == s.Definition && d.Exists && !d.Enabled); });
        Test("state copies are independent", () => { var s = Service(); var d = s.Copy(); d.StartType = 4; Assert(s.StartType == 2 && d.Fingerprint() != s.Fingerprint()); });
        Test("dependent ordering is stable", () => { var a = Service(); var b = a.Copy(); a.Dependents = new[] { "A", "B" }; b.Dependents = new[] { "B", "A" }; Assert(a.Fingerprint() == b.Fingerprint()); });
        Test("apply and restore exact startup value kind", () =>
        {
            var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var engine = new ChangeEngine(b, j);
            var r = engine.Apply(new[] { Item(s) }, Unused(), null); Assert(r.Status == "Applied" && b.Read(s).Fingerprint() == s.Disabled().Fingerprint());
            var restored = engine.Restore(r.Id, null); Assert(restored.Status == "Restored" && b.Read(s).Fingerprint() == s.Fingerprint() && b.Writes == 2);
        });
        Test("journal persisted before first system write", () =>
        {
            var s = Startup(); var b = new Fake(s); var j = new MemoryJournals();
            b.OnWrite = () => { var saved = j.List().Single(); Assert(saved.Status == "Applying" && saved.Entries.Single().Original.Fingerprint() == s.Fingerprint()); };
            Assert(new ChangeEngine(b, j).Apply(new[] { Item(s) }, Unused(), null).Status == "Applied");
        });
        Test("journal creation failure prevents all writes", () => { var s = Startup(); var b = new Fake(s); var j = new FailingJournals { FailAt = 1 }; Throws(() => new ChangeEngine(b, j).Apply(new[] { Item(s) }, Unused(), null)); Assert(b.Writes == 0 && j.List().Count == 0); });
        Test("intent update failure prevents system write", () => { var s = Startup(); var b = new Fake(s); var j = new FailingJournals { FailAt = 2 }; Throws(() => new ChangeEngine(b, j).Apply(new[] { Item(s) }, Unused(), null)); Assert(b.Writes == 0 && j.List().Single().Status == "Prepared"); });
        Test("post-write journal failure remains recoverable", () => { var s = Startup(); var b = new Fake(s); var j = new FailingJournals { FailAt = 3 }; var e = new ChangeEngine(b, j); Throws(() => e.Apply(new[] { Item(s) }, Unused(), null)); Assert(b.Writes == 1 && j.List().Single().Status == "Applying"); j.FailAt = 0; Assert(e.Restore(j.List().Single().Id, null).Status == "Restored" && b.Read(s).Fingerprint() == s.Fingerprint()); });
        Test("mixed batch restores service, task and Run state exactly", () =>
        {
            var states = new[] { Service(), TaskState(), Startup() }; var b = new Fake(states); var e = new ChangeEngine(b, new MemoryJournals());
            var r = e.Apply(states.Select(Item).ToArray(), Unused(), null); Assert(r.Status == "Applied");
            Assert(e.Restore(r.Id, null).Status == "Restored"); Assert(states.All(s => b.Read(s).Fingerprint() == s.Fingerprint()));
        });
        Test("manual stopped service stays stopped after recovery", () =>
        {
            var s = Service(); s.StartType = 3; s.Delayed = false; s.Running = false;
            var b = new Fake(s); var e = new ChangeEngine(b, new MemoryJournals()); var r = e.Apply(new[] { Item(s) }, Unused(), null);
            Assert(e.Restore(r.Id, null).Status == "Restored" && b.Read(s).StartType == 3 && !b.Read(s).Running);
        });
        Test("nonadmin apply refused before journaling", () => { var s = Startup(); var b = new Fake(s) { Admin = false }; var j = new MemoryJournals(); Throws(() => new ChangeEngine(b, j).Apply(new[] { Item(s) }, Unused(), null)); Assert(b.Writes == 0 && j.List().Count == 0); });
        Test("empty selection refused", () => Throws(() => new ChangeEngine(new Fake(), new MemoryJournals()).Apply(new ScanItem[0], Unused(), null)));
        Test("duplicate selection refused", () => { var s = Startup(); var b = new Fake(s); Throws(() => new ChangeEngine(b, new MemoryJournals()).Apply(new[] { Item(s), Item(s) }, Unused(), null)); Assert(b.Writes == 0); });
        Test("unsupported row refused", () => { var s = Startup(); var row = Item(s); row.CanChange = false; Throws(() => new ChangeEngine(new Fake(s), new MemoryJournals()).Apply(new[] { row }, Unused(), null)); });
        Test("batch size bounded", () => Throws(() => new ChangeEngine(new Fake(), new MemoryJournals()).Apply(Enumerable.Range(0, 41).Select(n => Item(Startup("entry" + n))).ToArray(), Unused(), null)));
        Test("stale scan refuses entire batch", () => { var a = Startup("A"); var z = Startup("Z"); var b = new Fake(a, z); b.States[z.Identity()].Value = "changed"; var j = new MemoryJournals(); Throws(() => new ChangeEngine(b, j).Apply(new[] { Item(a), Item(z) }, Unused(), null)); Assert(b.Writes == 0 && j.List().Count == 0); });
        Test("preflight failure writes nothing", () => { var s = Startup(); var b = new Fake(s) { PreflightFails = true }; var j = new MemoryJournals(); Throws(() => new ChangeEngine(b, j).Apply(new[] { Item(s) }, Unused(), null)); Assert(b.Writes == 0 && j.List().Count == 0); });
        Test("race after preflight stops remaining writes", () => { var s = Startup(); var b = new Fake(s); b.OnRead = () => { if (b.Reads == 2) b.States[s.Identity()].Value = "raced"; }; var r = new ChangeEngine(b, new MemoryJournals()).Apply(new[] { Item(s) }, Unused(), null); Assert(r.Status == "Needs review" && b.Writes == 0); });
        Test("write failure stops batch and preserves receipt", () => { var a = Startup("A"); var z = Startup("Z"); var b = new Fake(a, z) { DisableFails = true }; var j = new MemoryJournals(); var r = new ChangeEngine(b, j).Apply(new[] { Item(a), Item(z) }, Unused(), null); Assert(r.Status == "Needs review" && b.Writes == 1 && r.Entries[1].Status == "Pending" && j.Load(r.Id).Entries[0].Error != null); });
        Test("unverified result marked for review", () => { var s = Startup(); var b = new Fake(s) { WrongResult = true }; Assert(new ChangeEngine(b, new MemoryJournals()).Apply(new[] { Item(s) }, Unused(), null).Status == "Needs review"); });
        Test("restore refuses changed item before any writes", () => { var a = Startup("A"); var z = Startup("Z"); var b = new Fake(a, z); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var r = e.Apply(new[] { Item(a), Item(z) }, Unused(), null); b.States[a.Identity()] = a.Copy(); b.States[a.Identity()].Value = "external edit"; Throws(() => e.Restore(r.Id, null)); Assert(b.Writes == 2 && !b.States[z.Identity()].Exists); });
        Test("restore already original is idempotent", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var r = e.Apply(new[] { Item(s) }, Unused(), null); b.States[s.Identity()] = s.Copy(); Assert(e.Restore(r.Id, null).Status == "Restored" && b.Writes == 1); Assert(e.Restore(r.Id, null).Status == "Restored" && b.Writes == 1); });
        Test("nonadmin restore refused", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var r = e.Apply(new[] { Item(s) }, Unused(), null); b.Admin = false; Throws(() => e.Restore(r.Id, null)); Assert(b.Writes == 1); });
        Test("receipt scope prevents cross-user recovery", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var r = e.Apply(new[] { Item(s) }, Unused(), null); r.MachineScope = "different"; j.Save(r, false); Throws(() => e.Restore(r.Id, null)); Assert(b.Writes == 1); });
        Test("tampered expected state rejected", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var e = new ChangeEngine(b, j); var r = e.Apply(new[] { Item(s) }, Unused(), null); r.Entries[0].Expected.Target = "Other"; j.Save(r, false); Throws(() => e.Restore(r.Id, null)); Assert(b.Writes == 1); });
        Test("unsupported receipt status rejected", () => { var s = Startup(); var b = new Fake(s); var j = new MemoryJournals(); var r = new ChangeEngine(b, j).Apply(new[] { Item(s) }, Unused(), null); r.Status = "unknown"; Throws(() => ChangeEngine.ValidateReceipt(r, b.MachineScope)); });
        Test("CPU delta normalized and bounded", () => { Assert(CpuCapture.Percent(25, 100) == 75); Assert(CpuCapture.Percent(0, 0) == 0); Assert(CpuCapture.Percent(101, 100) == 0); });
        Test("synthetic export complete and names redacted", () =>
        {
            var path = Path.GetTempFileName(); try { CaptureResult.Demo().Export(path, false); var lines = File.ReadAllLines(path); Assert(lines.Length == 17); Assert(lines[0].Contains("\"synthetic\":true") && lines[0].Contains("\"process_names_enabled\":false")); Assert(!File.ReadAllText(path).Contains("ExampleApp")); Assert(lines[16].Contains("\"samples\":15")); } finally { File.Delete(path); }
        });
        Test("demo changes remain isolated", () => { var d = new DemoBackend(); var report = d.Scan(Unused()); var s = report.Items.First(x => x.CanChange); var j = new MemoryJournals(); var e = new ChangeEngine(d, j); var r = e.Apply(new[] { s }, Unused(), null); Assert(r.Status == "Applied"); Assert(e.Restore(r.Id, null).Status == "Restored"); });
    }
    static void Native()
    {
        if (Environment.GetEnvironmentVariable("LEAN81_NATIVE_FIXTURES") != "ephemeral-ci" || Environment.OSVersion.Platform != PlatformID.Win32NT)
            throw new InvalidOperationException("Native fixtures require explicit ephemeral CI opt-in on Windows.");
        var b = new WindowsBackend(); Assert(b.IsAdministrator, "native CI process must be administrator");
        var tag = "Lean81Fixture" + Guid.NewGuid().ToString("N"); string folderName = "\\" + tag;
        dynamic scheduler = null, root = null, folder = null, task = null, definition = null, action = null;
        var journalDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Lean81"); bool existed = Directory.Exists(journalDir);
        bool runKeyExisted; using (var key = Registry.CurrentUser.OpenSubKey(Product.RunKey)) runKeyExisted = key != null;
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(Product.RunKey)) key.SetValue(tag, @"%WINDIR%\System32\notepad.exe", RegistryValueKind.ExpandString);
            var original = b.Read(new ItemState { Kind = "startup", Scope = "user", Target = tag });
            var journals = new JournalStore(b.MachineScope); var engine = new ChangeEngine(b, journals);
            Test("native HKCU Run disable and exact recovery", () => { var r = engine.Apply(new[] { Item(original) }, Unused(), null); Assert(r.Status == "Applied", r.Entries[0].Error); Assert(!b.Read(original).Exists); Assert(engine.Restore(r.Id, null).Status == "Restored"); Assert(b.Read(original).Fingerprint() == original.Fingerprint()); });
            scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true)); scheduler.Connect(); root = scheduler.GetFolder(@"\"); folder = root.CreateFolder(folderName);
            definition = scheduler.NewTask(0); definition.RegistrationInfo.Author = "Lean81 CI fixture"; definition.Settings.Enabled = true; definition.Settings.AllowDemandStart = false;
            action = definition.Actions.Create(0); action.Path = Path.Combine(Environment.SystemDirectory, "cmd.exe"); action.Arguments = "/c exit 0";
            task = folder.RegisterTaskDefinition("Dormant", definition, 2, null, null, 3, null);
            var taskOriginal = b.Read(new ItemState { Kind = "task", Scope = "machine", Target = folderName + "\\Dormant" });
            Test("native dormant scheduled task disable and recovery", () => { var r = engine.Apply(new[] { Item(taskOriginal) }, Unused(), null); Assert(r.Status == "Applied", r.Entries[0].Error); Assert(!b.Read(taskOriginal).Enabled); Assert(engine.Restore(r.Id, null).Status == "Restored"); Assert(b.Read(taskOriginal).Fingerprint() == taskOriginal.Fingerprint()); });
            Test("native journal ACL and validated listing", () => Assert(journals.List().Count >= 2));
            Test("native journal traversal rejected", () => Throws(() => journals.Load("..\\x")));
            Test("native recovery listing isolates other user scopes", () => Assert(new JournalStore(Product.Hash("different user scope")).List().Count == 0));
            Test("native CPU capture has real bounded samples", () => { var r = CpuCapture.Run(4, CancellationToken.None, null); Assert(!r.Synthetic && r.Error == null && r.Samples.Count >= 2 && r.LogicalCpus > 0); Assert(r.Samples.All(s => Convert.ToDouble(s.metrics["cpu_pct"]) >= 0 && Convert.ToDouble(s.metrics["cpu_pct"]) <= 100 && s.processes.Count <= 20)); Assert(r.Samples.All(s => ResourceAnalysis.Number(s.metrics, "commit_pct", 100).HasValue && ResourceAnalysis.Number(s.metrics, "kernel_nonpaged_bytes").HasValue && s.metrics.ContainsKey("disk_busy_pct"))); Assert(r.Samples.SelectMany(s => s.processes).Any(p => p.private_bytes.HasValue && p.io_bytes_per_sec.HasValue)); Assert(r.Samples.Any(s => ResourceAnalysis.Number(s.metrics, "disk_busy_pct", 100).HasValue), "Expected installed PhysicalDisk counters on the Windows CI image"); });
            Test("native desktop settings are inspection only during fixtures", () => { foreach (var option in DesktopSettings.Options) { var s = b.Read(new ItemState { Kind = "setting", Scope = "user", Target = option.Id }); Assert(s.Kind == "setting" && s.Scope == "user" && s.Target == option.Id); } });
            Test("native device snapshot reads memory, uptime and fixed-drive capacity", () => { var d = DeviceSnapshot.Read(); Assert(d.TotalMemory > 0 && d.AvailableMemory <= d.TotalMemory && d.LogicalCpus > 0 && d.UptimeMilliseconds > 0 && d.Disks.Count > 0 && d.Disks.All(x => x.FreeBytes <= x.TotalBytes)); });
            NativeDeepFixtures();
            Test("native optional-service inventory is protected by default", () => { var report = b.Scan(new UsageProfile()); Assert(report.Items.Any(x => x.Kind == "service"), "expected at least one installed optional service on the Windows runner"); Assert(report.Items.Where(x => x.Kind == "service").All(x => !x.CanChange && x.Before.Definition.Length == 64)); });
        }
        finally
        {
            using (var key = Registry.CurrentUser.OpenSubKey(Product.RunKey, true)) if (key != null) key.DeleteValue(tag, false);
            if (!runKeyExisted) using (var key = Registry.CurrentUser.OpenSubKey(Product.RunKey)) { if (key != null && key.ValueCount == 0 && key.SubKeyCount == 0) { key.Dispose(); Registry.CurrentUser.DeleteSubKey(Product.RunKey, false); } }
            if (folder != null) try { folder.DeleteTask("Dormant", 0); } catch { }
            if (root != null) try { root.DeleteFolder(folderName, 0); } catch { }
            foreach (object com in new object[] { action, definition, task, folder, root, scheduler }) if (com != null && System.Runtime.InteropServices.Marshal.IsComObject(com)) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(com);
            if (!existed && Directory.Exists(journalDir)) Directory.Delete(journalDir, true);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        if (args.Contains("--idle-fixture")) { if (Environment.GetEnvironmentVariable("LEAN81_NATIVE_FIXTURES") != "ephemeral-ci") return 3; Thread.Sleep(30000); return 0; }
        if (args.Length == 2 && args[0] == "--idle-service") { if (Environment.UserInteractive || !FixtureServiceName(args[1])) return 3; ServiceBaseRun(args[1]); return 0; }
        if (args.Contains("--resource-probe")) Test("x86 native resource layout and bounded capture", NativeResourceProbe);
        else { MainCore(); FeatureTests(); ResourceTests(); DeepTests(); SessionTests(); SelectionTests(); if (args.Contains("--native")) Test("Windows native fixture suite", Native); }
        Console.WriteLine(passed + " passed, " + failed + " failed"); return failed == 0 ? 0 : 1;
    }
}
