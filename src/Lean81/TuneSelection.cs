using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Lean81
{
    // A keep choice can only remove an action from a freshly built plan. Saved
    // hashes are never interpreted as commands, states or permission to apply.
    public static class TuneSelection
    {
        public static string Key(ScanItem item) { return Product.Hash(item.Before.Identity()); }
        public static void ValidateKept(IList<string> keys)
        {
            if (keys == null || keys.Count > Product.MaxItems || keys.Any(k => k == null || k.Length != 64 || !Regex.IsMatch(k, "^[0-9a-f]{64}$"))
                || keys.Distinct(StringComparer.Ordinal).Count() != keys.Count)
                throw new InvalidOperationException("Unsupported saved keep choices.");
        }
        public static DeepTunePlan WithKeepList(DeepTunePlan proposed, IList<string> kept)
        {
            ValidateKept(kept);
            if (proposed == null) throw new InvalidOperationException("Prepare a fresh tune before selecting settings.");
            var keys = new HashSet<string>(kept, StringComparer.Ordinal);
            return Select(proposed, proposed.Items.Where(i => !keys.Contains(Key(i))).Select(Key));
        }
        public static DeepTunePlan Select(DeepTunePlan proposed, IEnumerable<string> selected)
        {
            if (proposed == null || proposed.Profile == null || proposed.Items.Count > 128 || selected == null
                || proposed.Items.Any(i => i == null || i.Before == null || !i.CanChange))
                throw new InvalidOperationException("Prepare a fresh tune before selecting settings.");
            var keys = selected.Take(129).ToArray();
            var offered = new HashSet<string>(proposed.Items.Select(Key), StringComparer.Ordinal);
            if (keys.Length > 128 || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length
                || offered.Count != proposed.Items.Count || keys.Any(k => !offered.Contains(k)))
                throw new InvalidOperationException("The selection contains an item outside this review.");
            var chosen = new HashSet<string>(keys, StringComparer.Ordinal);
            var result = new DeepTunePlan { Profile = new UsageProfile() };
            Profiles.Copy(proposed.Profile, result.Profile); result.Kept.AddRange(proposed.Kept);
            foreach (var item in proposed.Items)
            {
                if (!chosen.Contains(Key(item))) { result.Kept.Add(item.Name + ": kept in this review."); continue; }
                result.Items.Add(new ScanItem { Id = item.Id, Name = item.Name, Kind = item.Kind, Scope = item.Scope, Status = item.Status,
                    Detail = item.Detail, Tradeoff = item.Tradeoff, CanChange = item.CanChange, Suggested = item.Suggested,
                    Before = item.Before.Copy(), Feature = item.Feature });
            }
            // Keeping a dependent also keeps its provider, transitively. Do not
            // add the dependent back or rely only on the initial feature answers.
            bool removed;
            do
            {
                removed = false;
                var names = new HashSet<string>(result.Items.Where(i => i.Before.Kind == "service").Select(i => i.Before.Target), StringComparer.OrdinalIgnoreCase);
                foreach (var item in result.Items.Where(i => i.Before.Kind == "service" && i.Before.Dependents.Any(d => !names.Contains(d))).ToArray())
                { result.Items.Remove(item); result.Kept.Add(item.Name + ": kept because a dependent is retained."); removed = true; }
            } while (removed);
            return result;
        }
        public static List<string> Remember(IList<string> existing, DeepTunePlan offered, DeepTunePlan chosen)
        {
            ValidateKept(existing);
            if (chosen == null) throw new InvalidOperationException("Choose settings from a fresh review first.");
            // Revalidate membership and dependency closure, even for callers
            // other than the desktop review. Do not persist a mutable plan.
            var accepted = Select(offered, chosen.Items.Select(Key));
            var offeredKeys = new HashSet<string>(offered.Items.Select(Key), StringComparer.Ordinal);
            var chosenKeys = new HashSet<string>(accepted.Items.Select(Key), StringComparer.Ordinal);
            var result = existing.Where(k => !offeredKeys.Contains(k)).Concat(offeredKeys.Where(k => !chosenKeys.Contains(k))).OrderBy(k => k, StringComparer.Ordinal).ToList();
            ValidateKept(result); return result;
        }
        public static string Effect(ScanItem item)
        {
            switch (item.Before.Kind)
            {
                case "service": return "Stop now; disable future starts";
                case "startup": case "startupfile": return "Prevent launch at sign-in";
                case "task": return "Disable future scheduled runs";
                case "setting": return "Reduce effect; may need sign-out";
                case "power": return "Switch plan until changed / restored";
                default: return "Review required";
            }
        }
    }
}
