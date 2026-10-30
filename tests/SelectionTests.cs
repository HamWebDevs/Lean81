using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lean81;

static partial class Tests
{
    static DeepTunePlan Offered(ServiceGraph backend) { return DeepTunePlan.Build(backend.Scan(Unused()), Unused(), true, false, true); }
    static void SelectionTests()
    {
        Test("selective review preserves an unchecked provider while allowing its dependent", () => {
            var b = Graph(); var p = Offered(b); var child = p.Items.Single(i => i.Before.Target == "HomeGroupProvider");
            var chosen = TuneSelection.Select(p, new[] { TuneSelection.Key(child) });
            var j = new MemoryJournals(); var e = new DeepTuneEngine(b, j); var r = e.Apply(chosen, null);
            Assert(r.Applied == 1 && b.Writes.SequenceEqual(new[] { "disable:HomeGroupProvider" }) && b.States.Values.Single(i => i.Target == "HomeGroupListener").Running);
            Assert(e.Restore(r.GroupId, null) == 1 && b.States.Values.All(i => i.Running));
        });
        Test("keeping a dependent prevents disabling its selected provider", () => {
            var p = Offered(Graph()); var parent = p.Items.Single(i => i.Before.Target == "HomeGroupListener");
            var selected = TuneSelection.Select(p, new[] { TuneSelection.Key(parent) }); Assert(selected.Items.Count == 0 && selected.Kept.Any(k => k.Contains("dependent is retained")));
        });
        Test("dependency protection propagates across multiple kept providers", () => {
            var b = new ServiceGraph(Service("WSearch"), Service("Spooler"), Service("stisvc")); b.Edges["WSearch"] = new[] { "Spooler" }; b.Edges["Spooler"] = new[] { "stisvc" };
            var p = Offered(b); Assert(p.Items.Count == 3); var chosen = TuneSelection.Select(p, p.Items.Where(i => i.Before.Target != "stisvc").Select(TuneSelection.Key)); Assert(chosen.Items.Count == 0);
        });
        Test("empty selection only keeps settings and creates no action", () => { var p = Offered(Graph()); var chosen = TuneSelection.Select(p, new string[0]); Assert(chosen.Items.Count == 0 && chosen.Kept.Count == 2 && p.Items.Count == 2); });
        Test("selection cannot introduce unknown targets or duplicate actions", () => { var p = Offered(Graph()); string key = TuneSelection.Key(p.Items[0]); Throws(() => TuneSelection.Select(p, new[] { Product.Hash("foreign") })); Throws(() => TuneSelection.Select(p, new[] { key, key })); });
        Test("selection refuses invalid or excessive plans", () => { Throws(() => TuneSelection.Select(null, new string[0])); var p = Offered(Graph()); p.Items.Add(p.Items[0]); Throws(() => TuneSelection.Select(p, p.Items.Select(TuneSelection.Key))); p = new DeepTunePlan { Profile = Unused() }; for (int i = 0; i < 129; i++) p.Items.Add(Item(Startup("Entry" + i))); Throws(() => TuneSelection.Select(p, new string[0])); });
        Test("selection clones states and feature answers instead of sharing a plan", () => { var p = Offered(Graph()); var chosen = TuneSelection.Select(p, p.Items.Select(TuneSelection.Key)); chosen.Items[0].Before.StartType = 4; chosen.Profile.Printing = true; Assert(p.Items[0].Before.StartType == 2 && !p.Profile.Printing); });
        Test("a saved keep choice still protects required service providers", () => { var p = Offered(Graph()); string child = TuneSelection.Key(p.Items.Single(i => i.Before.Target == "HomeGroupProvider")); Assert(TuneSelection.WithKeepList(p, new[] { child }).Items.Count == 0 && p.Items.Count == 2); });
        Test("unrelated saved keep identities never add or remove current choices", () => { var p = Offered(Graph()); Assert(TuneSelection.WithKeepList(p, new[] { Product.Hash("other") }).Items.Count == 2); });
        Test("remembering keeps preserves unrelated entries and removes re-included entries", () => {
            var p = Offered(Graph()); string unrelated = Product.Hash("previously kept"); var existing = p.Items.Select(TuneSelection.Key).Concat(new[] { unrelated }).ToList();
            var selected = TuneSelection.Select(p, p.Items.Select(TuneSelection.Key)); Assert(TuneSelection.Remember(existing, p, selected).SequenceEqual(new[] { unrelated }));
            var saved = TuneSelection.Remember(new[] { unrelated }, p, TuneSelection.Select(p, new string[0])); Assert(saved.Count == 3 && saved.Contains(unrelated));
        });
        Test("saved keep data contains hashes rather than startup commands or paths", () => { var p = new DeepTunePlan { Profile = Unused() }; p.Items.Add(Item(Startup("Private updater"))); var keys = TuneSelection.Remember(new List<string>(), p, TuneSelection.Select(p, new string[0])); string json = Product.Json().Serialize(keys); Assert(keys.Single().Length == 64 && !json.Contains("updater") && !json.Contains("app.exe") && !json.Contains("LOCALAPPDATA")); });
        Test("keep identity distinguishes user and machine settings with the same name", () => { var a = Item(Startup()); var b = Item(Startup()); b.Before.Scope = "machine"; Assert(TuneSelection.Key(a) != TuneSelection.Key(b)); });
        Test("keep data rejects commands invalid hashes duplicates and nulls", () => { foreach (var keys in new IList<string>[] { null, new[] { "cmd /c anything" }, new[] { new string('A', 64) }, new string[] { null }, new[] { Product.Hash("x"), Product.Hash("x") } }) Throws(() => TuneSelection.ValidateKept(keys)); });
        Test("saved keep list is bounded rather than silently losing old choices", () => { var full = Enumerable.Range(0, Product.MaxItems).Select(i => Product.Hash("saved" + i)).ToList(); TuneSelection.ValidateKept(full); var p = Offered(Graph()); Throws(() => TuneSelection.Remember(full, p, TuneSelection.Select(p, new string[0]))); Assert(full.Count == Product.MaxItems); });
        Test("older workspace files start with no kept tune settings", () => { var w = Product.Json().Deserialize<UserWorkspace>("{\"Schema\":1,\"Profile\":{},\"Workload\":\"Earlier version\",\"History\":[]}"); UserWorkspace.Validate(w); Assert(w.KeptTuneItems != null && w.KeptTuneItems.Count == 0 && w.Profile.Printing); });
        Test("keep choices round trip through the existing atomic workspace store", () => {
            string folder = Path.Combine(Path.GetTempPath(), "Lean81KeepFixture" + Guid.NewGuid().ToString("N"));
            try { var w = new UserWorkspace(); w.KeptTuneItems.Add(Product.Hash("a saved setting")); var store = new WorkspaceStore(folder); store.Save(w); Assert(store.Load().KeptTuneItems.SequenceEqual(w.KeptTuneItems)); w.KeptTuneItems.Clear(); store.Save(w); Assert(store.Load().KeptTuneItems.Count == 0); }
            finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        });
        Test("selective review still refuses a setting that changed after review", () => {
            var b = Graph(); var p = Offered(b); var chosen = TuneSelection.Select(p, p.Items.Where(i => i.Before.Target == "HomeGroupProvider").Select(TuneSelection.Key)); b.States.Values.Single(i => i.Target == "HomeGroupProvider").Definition = Product.Hash("changed");
            Throws(() => new DeepTuneEngine(b, new MemoryJournals()).Apply(chosen, null)); Assert(b.Writes.Count == 0);
        });
        Test("review timing distinguishes service startup task desktop and power actions", () => {
            Assert(TuneSelection.Effect(Item(Service())).Contains("Stop now") && TuneSelection.Effect(Item(Startup())).Contains("sign-in") && TuneSelection.Effect(Item(TaskState())).Contains("future scheduled")
                && TuneSelection.Effect(Item(new ItemState { Kind = "setting" })).Contains("sign-out") && TuneSelection.Effect(Item(new ItemState { Kind = "power" })).Contains("until changed"));
        });
    }
}
