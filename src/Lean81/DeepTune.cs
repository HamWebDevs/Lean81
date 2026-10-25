using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lean81
{
    public static class StartupRules
    {
        public static string Feature(string text)
        {
            text = text ?? "";
            if (Policy.IsProtectedText(text)) return null;
            if (Regex.IsMatch(text, @"(?i)(onedrive|dropbox)")) return "sync";
            if (Regex.IsMatch(text, @"(?i)(discord|skype|teams\.exe|slack)")) return "chat";
            if (Regex.IsMatch(text, @"(?i)(steam|epicgameslauncher|epicwebhelper|galaxyclient|battle\.net)")) return "launchers";
            if (Regex.IsMatch(text, @"(?i)(spotify|ituneshelper)")) return "music";
            if (Regex.IsMatch(text, @"(?i)(googleupdate|adobearm|adobe.*updat|jusched|java.*updat|apple.*software.*updat)")) return "updaters";
            return null;
        }
    }
    public sealed class DeepTunePlan
    {
        public readonly List<ScanItem> Items = new List<ScanItem>();
        public readonly List<string> Kept = new List<string>();
        public UsageProfile Profile;
        public static DeepTunePlan Build(ScanReport report, UsageProfile profile, bool effects, bool power, bool startup)
        {
            if (report == null || profile == null) throw new InvalidOperationException("Scan the laptop before preparing a deep tune.");
            var plan = new DeepTunePlan { Profile = new UsageProfile() }; Profiles.Copy(profile, plan.Profile);
            foreach (var item in report.Items.Where(i => Policy.NeedsReduction(i.Before)))
            {
                var state = item.Before; bool choose = false;
                if (state.Kind == "service")
                {
                    var option = Policy.Services.FirstOrDefault(o => String.Equals(o.Name, state.Target, StringComparison.OrdinalIgnoreCase));
                    choose = option != null && !profile.Uses(option.Feature);
                }
                else if (state.Kind == "setting") choose = effects;
                else if (state.Kind == "power") choose = power;
                else if (startup && new[] { "startup", "startupfile", "task" }.Contains(state.Kind))
                {
                    string feature = item.Feature ?? (state.Kind == "startup" ? StartupRules.Feature(state.Target + " " + state.Value) : null);
                    choose = feature != null && !profile.Uses(feature) && item.CanChange;
                }
                if (!choose) continue;
                var test = state.Copy(); if (test.Kind == "service") test.Dependents = new string[0];
                try { Policy.Validate(test, profile, false); }
                catch (InvalidOperationException) { continue; }
                plan.Items.Add(new ScanItem { Id = item.Id, Name = item.Name, Kind = state.Kind, Scope = state.Scope, Status = item.Status, Detail = item.Detail, Tradeoff = item.Tradeoff, CanChange = true, Before = state.Copy(), Feature = item.Feature });
            }
            // A parent is offered only if every enabled dependent is also offered.
            bool changed;
            do
            {
                changed = false; var names = new HashSet<string>(plan.Items.Where(i => i.Kind == "service").Select(i => i.Before.Target), StringComparer.OrdinalIgnoreCase);
                foreach (var item in plan.Items.Where(i => i.Kind == "service" && i.Before.Dependents.Any(d => !names.Contains(d))).ToArray())
                { plan.Items.Remove(item); plan.Kept.Add(item.Name + ": an enabled dependent is being kept."); changed = true; }
            } while (changed);
            // Detect dependency cycles before any mutation.
            var remaining = plan.Items.Where(i => i.Kind == "service").ToList();
            while (remaining.Count > 0)
            {
                var names = new HashSet<string>(remaining.Select(i => i.Before.Target), StringComparer.OrdinalIgnoreCase);
                var leaves = remaining.Where(i => !i.Before.Dependents.Any(names.Contains)).ToArray();
                if (leaves.Length == 0) { foreach (var item in remaining) { plan.Items.Remove(item); plan.Kept.Add(item.Name + ": unresolved dependency cycle."); } break; }
                foreach (var item in leaves) remaining.Remove(item);
            }
            if (plan.Items.Count > 128 || plan.Items.Select(i => i.Before.Identity()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Items.Count)
                throw new InvalidOperationException("This plan has too many or duplicate items. Choose fewer settings.");
            return plan;
        }
    }
    public sealed class DeepTuneResult
    {
        public string GroupId, Status = "Applied";
        public int Applied;
        public readonly List<ChangeReceipt> Receipts = new List<ChangeReceipt>();
    }
    public sealed class DeepTuneEngine
    {
        readonly IBackend backend; readonly IJournalStore journals;
        public DeepTuneEngine(IBackend backend, IJournalStore journals) { this.backend = backend; this.journals = journals; }
        // The outer lease stays held across all passes. Inner engines borrow it.
        sealed class BorrowedStore : IJournalStore
        {
            readonly IJournalStore inner; sealed class Lease : IDisposable { public void Dispose() { } }
            public BorrowedStore(IJournalStore inner) { this.inner = inner; }
            public IDisposable Lock() { return new Lease(); }
            public void Save(ChangeReceipt r, bool create) { inner.Save(r, create); }
            public ChangeReceipt Load(string id) { return inner.Load(id); }
            public IList<ChangeReceipt> List() { return inner.List(); }
        }
        public DeepTuneResult Apply(DeepTunePlan plan, Action<string> progress)
        {
            if (!backend.IsAdministrator || plan == null || plan.Profile == null || plan.Items.Count == 0 || plan.Items.Count > 128) throw new InvalidOperationException("Enable changes and prepare a nonempty deep-tune review first.");
            if (plan.Items.Any(i => i == null || i.Before == null || !i.CanChange) || plan.Items.Select(i => i.Before.Identity()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plan.Items.Count) throw new InvalidOperationException("Invalid deep-tune selection.");
            var result = new DeepTuneResult { GroupId = Guid.NewGuid().ToString("N") };
            using (journals.Lock())
            {
                var remaining = plan.Items.ToList(); var store = new BorrowedStore(journals); var engine = new ChangeEngine(backend, store);
                var serviceNames = new HashSet<string>(remaining.Where(i => i.Kind == "service").Select(i => i.Before.Target), StringComparer.OrdinalIgnoreCase);
                foreach (var item in remaining)
                {
                    var current = backend.Read(item.Before);
                    if (current.Fingerprint() != item.Before.Fingerprint()) throw new InvalidOperationException("The laptop changed after the review. Prepare a fresh deep tune.");
                    var check = current.Copy(); if (current.Kind == "service")
                    {
                        if (current.Dependents.Any(d => !serviceNames.Contains(d))) throw new InvalidOperationException("A service now has a dependent outside the reviewed tune.");
                        check.Dependents = new string[0];
                    }
                    Policy.Validate(check, plan.Profile, false);
                    if (current.Kind != "service" || current.Dependents.Length == 0) backend.Preflight(current, plan.Profile, false);
                }
                while (remaining.Count > 0)
                {
                    var batch = new List<ScanItem>();
                    foreach (var item in remaining)
                    {
                        var current = backend.Read(item.Before);
                        if (current.Fingerprint() != item.Before.Fingerprint()) throw new InvalidOperationException("A reviewed item changed during the tune. Earlier changes remain in Recovery.");
                        if (current.Kind == "service" && current.Dependents.Length != 0) continue;
                        batch.Add(new ScanItem { Id = item.Id, Name = item.Name, Kind = item.Kind, Scope = item.Scope, Before = current, CanChange = true });
                        if (batch.Count == Product.MaxChanges) break;
                    }
                    if (batch.Count == 0) { result.Status = "Stopped: enabled dependents remain"; return result; }
                    var receipt = engine.Apply(batch, plan.Profile, progress, result.GroupId, result.Receipts.Count + 1);
                    result.Receipts.Add(receipt); result.Applied += receipt.Entries.Count(e => e.Status == "Applied");
                    if (receipt.Status != "Applied") { result.Status = "Needs review"; return result; }
                    var done = new HashSet<string>(batch.Select(i => i.Before.Identity()), StringComparer.OrdinalIgnoreCase); remaining.RemoveAll(i => done.Contains(i.Before.Identity()));
                }
            }
            return result;
        }
        public int Restore(string groupId, Action<string> progress)
        {
            Guid id; if (!backend.IsAdministrator || !Guid.TryParseExact(groupId, "N", out id)) throw new InvalidOperationException("Enable changes and select a saved tune group.");
            using (journals.Lock())
            {
                var receipts = journals.List().Where(r => r.GroupId == groupId).OrderByDescending(r => r.GroupSequence).ToArray();
                if (receipts.Length == 0 || receipts.Length > 128) throw new InvalidOperationException("No supported saved tune group was found.");
                var entries = receipts.SelectMany(r => r.Entries).ToArray();
                if (entries.Select(e => e.Original.Identity()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length) throw new InvalidOperationException("The tune group contains overlapping changes; inspect receipts individually.");
                var engine = new ChangeEngine(backend, new BorrowedStore(journals));
                foreach (var receipt in receipts)
                {
                    ChangeEngine.ValidateReceipt(receipt, backend.MachineScope);
                    if (receipt.Status == "Restored") continue;
                    if (engine.Inspect(receipt.Id).Any(c => !c.CanRestore)) throw new InvalidOperationException("A tune item changed independently. No group settings were restored.");
                    foreach (var entry in receipt.Entries.Where(e => e.Status != "Restored")) backend.Preflight(entry.Original, new UsageProfile(), true);
                }
                int restored = 0;
                foreach (var receipt in receipts.Where(r => r.Status != "Restored"))
                {
                    var outcome = engine.Restore(receipt.Id, progress); if (outcome.Status != "Restored") throw new InvalidOperationException("Group recovery stopped. Inspect the remaining receipts in Recovery.");
                    restored += outcome.Entries.Count;
                }
                return restored;
            }
        }
    }
}
