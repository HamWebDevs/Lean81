using System;
using System.Collections.Generic;
using System.Linq;

namespace Lean81
{
    public sealed class DemoBackend : IBackend
    {
        readonly Dictionary<string, ItemState> states = new Dictionary<string, ItemState>();
        public bool IsAdministrator { get { return true; } }
        public string MachineScope { get { return "lean81-synthetic-demo"; } }
        public DemoBackend()
        {
            Add(new ItemState { Kind = "startup", Target = "Cloud sync companion", Scope = "user", Exists = true, Enabled = true, Value = @"C:\Demo\CloudSync.exe", ValueKind = 1 });
            Add(new ItemState { Kind = "startup", Target = "Game launcher", Scope = "user", Exists = true, Enabled = true, Value = @"C:\Demo\Games.exe --background", ValueKind = 1 });
            Add(new ItemState { Kind = "startup", Target = "SecurityHealth", Scope = "machine64", Exists = true, Enabled = true, Value = @"C:\Demo\SecurityHealth.exe", ValueKind = 1 });
            Add(new ItemState { Kind = "task", Target = @"\DemoApp\Daily updater", Scope = "machine", Exists = true, Enabled = true, Definition = Product.Hash("synthetic updater") });
            Add(new ItemState { Kind = "task", Target = @"\Microsoft\Windows\Maintenance", Scope = "machine", Exists = true, Enabled = true, Definition = Product.Hash("synthetic system task") });
            foreach (var s in Policy.Services) Add(new ItemState { Kind = "service", Target = s.Name, Scope = "machine", Exists = true, Enabled = true, StartType = 2, Running = true, Definition = Product.Hash("synthetic " + s.Name) });
            foreach (var s in DesktopSettings.Options) Add(new ItemState { Kind = "setting", Target = s.Id, Scope = "user", Exists = true, Enabled = true, ValueKind = 0 });
            Add(new ItemState { Kind = "power", Target = "active-power-plan", Scope = "user", Exists = true, Enabled = true, Value = PowerPlans.Balanced });
        }
        void Add(ItemState state) { states.Add(state.Identity(), state); }
        public ItemState Read(ItemState identity)
        {
            var copy = states[identity.Identity()].Copy();
            if (copy.Kind == "service" && copy.Target == "HomeGroupListener")
                copy.Dependents = states.Values.Where(s => s.Kind == "service" && s.Target == "HomeGroupProvider" && s.StartType != 4).Select(s => s.Target).ToArray();
            return copy;
        }
        public void Preflight(ItemState state, UsageProfile profile, bool restoring) { Policy.Validate(state, profile, restoring); }
        public void Disable(ItemState original) { states[original.Identity()] = original.Disabled(); }
        public void Restore(ItemState original) { states[original.Identity()] = original.Copy(); }
        public ScanReport Scan(UsageProfile profile)
        {
            var report = new ScanReport();
            foreach (var stored in states.Values)
            {
                var state = Read(stored);
                var option = Policy.Services.FirstOrDefault(s => s.Name == state.Target);
                bool can = state.Exists && state.Enabled; try { Policy.Validate(state, profile, false); } catch { can = false; }
                var desktop = DesktopSettings.Options.FirstOrDefault(s => s.Id == state.Target);
                report.Items.Add(new ScanItem { Id = Product.Hash(state.Identity()), Name = state.Kind == "power" ? "High performance power plan" : desktop != null ? desktop.Title : option == null ? state.Target.Split('\\').Last() : option.Title,
                    Kind = state.Kind, Scope = state.Scope, Before = state.Copy(), Status = Policy.StateLabel(state), CanChange = can, Suggested = can && option != null,
                    Feature = state.Target == "Cloud sync companion" ? "sync" : state.Target == "Game launcher" ? "launchers" : state.Kind == "startup" ? StartupRules.Feature(state.Target + " " + state.Value) : null,
                    Detail = can ? "Synthetic example. Review before selecting." : "Kept by your profile or protected by policy.",
                    Tradeoff = desktop != null ? desktop.Tradeoff : option == null ? "Automatic startup will stop; manual launch remains available." : option.Tradeoff });
            }
            report.Apps.Add(new InstalledApp { Name = "Example photo editor", Publisher = "Demo software", Version = "2.4", Note = "Review manually in Programs and Features" });
            report.Apps.Add(new InstalledApp { Name = "Example trial suite", Publisher = "Demo software", Version = "1.0", Note = "Synthetic inventory entry" });
            report.Notes.Add("DEMO: all inventory and changes are synthetic. No Windows settings are changed.");
            return report;
        }
    }
}
