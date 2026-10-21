using System;
using System.Collections.Generic;
using System.Linq;

namespace Lean81
{
    public sealed class ChangeEngine
    {
        readonly IBackend backend;
        readonly IJournalStore journals;
        public ChangeEngine(IBackend backend, IJournalStore journals) { this.backend = backend; this.journals = journals; }
        public ChangeReceipt Apply(IList<ScanItem> selection, UsageProfile profile, Action<string> progress)
        {
            if (!backend.IsAdministrator) throw new InvalidOperationException("Enable administrator mode to make changes.");
            if (selection == null || selection.Count == 0 || selection.Count > Product.MaxChanges) throw new InvalidOperationException("Select between 1 and 40 supported changes.");
            if (selection.Any(i => i == null || !i.CanChange || i.Before == null) || selection.Select(i => i.Before.Identity()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selection.Count)
                throw new InvalidOperationException("Selection contains unsupported or duplicate changes.");
            using (journals.Lock())
            {
                var receipt = new ChangeReceipt { MachineScope = backend.MachineScope };
                foreach (var item in selection)
                {
                    Policy.Validate(item.Before, profile, false);
                    var current = backend.Read(item.Before);
                    if (current.Fingerprint() != item.Before.Fingerprint()) throw new InvalidOperationException("An item changed since the scan. Rescan before applying anything.");
                    backend.Preflight(current, profile, false);
                    if (!current.Exists || !current.Enabled) throw new InvalidOperationException("An item is already disabled or missing. Rescan first.");
                    receipt.Entries.Add(new JournalEntry { Name = item.Name, Original = current, Expected = current.Disabled(), Status = "Pending" });
                }
                // Persist the complete before-state before the first system write.
                journals.Save(receipt, true);
                foreach (var entry in receipt.Entries)
                {
                    try
                    {
                        if (progress != null) progress("Disabling " + entry.Name);
                        if (backend.Read(entry.Original).Fingerprint() != entry.Original.Fingerprint()) throw new InvalidOperationException("Item changed after preflight; remaining changes stopped.");
                        backend.Preflight(entry.Original, profile, false);
                        entry.Status = "Applying"; receipt.Status = "Applying"; journals.Save(receipt, false);
                        backend.Disable(entry.Original);
                        var after = backend.Read(entry.Original);
                        if (after.Fingerprint() != entry.Expected.Fingerprint()) throw new InvalidOperationException("The final state could not be verified. Review this receipt in Recovery.");
                        entry.Status = "Applied"; journals.Save(receipt, false);
                    }
                    catch (Exception ex)
                    {
                        entry.Status = "Needs review"; entry.Error = Product.Clean(ex.Message, 400); receipt.Status = "Needs review";
                        journals.Save(receipt, false);
                        return receipt;
                    }
                }
                receipt.Status = "Applied"; journals.Save(receipt, false); return receipt;
            }
        }

        public ChangeReceipt Restore(string id, Action<string> progress)
        {
            if (!backend.IsAdministrator) throw new InvalidOperationException("Enable administrator mode to restore settings.");
            using (journals.Lock())
            {
                var receipt = journals.Load(id);
                ValidateReceipt(receipt, backend.MachineScope);
                var pending = receipt.Entries.Where(e => e.Status != "Restored").Reverse().ToList();
                // Preflight the entire restore before writing anything.
                foreach (var entry in pending)
                {
                    Policy.Validate(entry.Original, new UsageProfile(), true);
                    backend.Preflight(entry.Original, new UsageProfile(), true);
                    var current = backend.Read(entry.Original);
                    if (current.Fingerprint() != entry.Original.Fingerprint() && current.Fingerprint() != entry.Expected.Fingerprint())
                        throw new InvalidOperationException("A saved item has changed independently or has an unverified partial state. No settings were restored. Review its current state first.");
                }
                foreach (var entry in pending)
                {
                    try
                    {
                        if (progress != null) progress("Restoring " + entry.Name);
                        var current = backend.Read(entry.Original);
                        if (current.Fingerprint() != entry.Original.Fingerprint())
                        {
                            if (current.Fingerprint() != entry.Expected.Fingerprint()) throw new InvalidOperationException("Item changed after restore preflight.");
                            entry.Status = "Restoring"; receipt.Status = "Restoring"; journals.Save(receipt, false);
                            backend.Restore(entry.Original);
                            if (backend.Read(entry.Original).Fingerprint() != entry.Original.Fingerprint()) throw new InvalidOperationException("Restored state could not be verified.");
                        }
                        entry.Status = "Restored"; entry.Error = null; journals.Save(receipt, false);
                    }
                    catch (Exception ex)
                    {
                        entry.Status = "Needs review"; entry.Error = Product.Clean(ex.Message, 400); receipt.Status = "Needs review";
                        journals.Save(receipt, false); return receipt;
                    }
                }
                receipt.Status = "Restored"; journals.Save(receipt, false); return receipt;
            }
        }
        public static void ValidateReceipt(ChangeReceipt receipt, string scope)
        {
            Guid parsed;
            if (receipt == null || receipt.Schema != 1 || !Guid.TryParseExact(receipt.Id, "N", out parsed)
                || receipt.MachineScope != scope || receipt.Entries == null || receipt.Entries.Count == 0 || receipt.Entries.Count > Product.MaxChanges)
                throw new InvalidOperationException("Unsupported receipt or different machine/user scope.");
            if (!new[] { "Prepared", "Applying", "Applied", "Restoring", "Restored", "Needs review" }.Contains(receipt.Status))
                throw new InvalidOperationException("Unsupported receipt status.");
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in receipt.Entries)
            {
                if (entry == null || entry.Original == null || entry.Expected == null || !identities.Add(entry.Original.Identity()))
                    throw new InvalidOperationException("Receipt contains invalid or duplicate entries.");
                Policy.Validate(entry.Original, new UsageProfile(), true);
                if (!new[] { "Pending", "Applying", "Applied", "Restoring", "Restored", "Needs review" }.Contains(entry.Status))
                    throw new InvalidOperationException("Unsupported entry status.");
                if (!entry.Original.Exists || !entry.Original.Enabled || entry.Expected.Fingerprint() != entry.Original.Disabled().Fingerprint())
                    throw new InvalidOperationException("Receipt states do not match a supported disable operation.");
            }
        }
    }
}
